using System.Security.Cryptography;

namespace MovieWorker.Core.Camera;

/// <summary>Eine geplante Kopie von der Karte auf die Festplatte.</summary>
public sealed record CopyOperation(string Source, string Destination, long Size)
{
    /// <summary>Ziel existiert bereits mit gleicher Größe – wird übersprungen.</summary>
    public bool AlreadyPresent { get; init; }
}

/// <summary>Fortschritt beim Kopieren.</summary>
public sealed record CopyProgress(int FileIndex, int FileCount, string FileName, long BytesDone, long BytesTotal, string Stage);

/// <summary>Ergebnis einer kopierten Datei.</summary>
public sealed record CopyResult(CopyOperation Operation, bool Verified, string? Checksum);

/// <summary>
/// Kopiert Videos von der Speicherkarte in einen Projektordner – Struktur: Ziel\JJJJ-MM-TT\Kamera\Datei.
/// Auf Wunsch wird jede Kopie per SHA-256 mit dem Original verglichen, bevor die Karte formatiert wird.
/// </summary>
public static class CardImporter
{
    public static IReadOnlyList<CopyOperation> Plan(IEnumerable<string> sourceFiles, string destinationRoot, DateTime date,
        string cameraFolder, Func<string, long>? fileSize = null)
    {
        fileSize ??= p => new FileInfo(p).Length;
        var targetFolder = Path.Combine(destinationRoot, date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            SanitizeFolderName(cameraFolder));
        var operations = new List<CopyOperation>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in CardScanner.FilterVideos(sourceFiles))
        {
            var size = fileSize(source);
            var name = Path.GetFileName(source);
            var destination = Path.Combine(targetFolder, name);

            // Gleicher Dateiname aus zwei Kartenordnern (z. B. 100CANON und 101CANON): Ordner voranstellen.
            if (!usedNames.Add(name))
            {
                var parent = Path.GetFileName(Path.GetDirectoryName(source)) ?? "x";
                destination = Path.Combine(targetFolder, $"{parent}_{name}");
            }

            var present = File.Exists(destination) && new FileInfo(destination).Length == size;
            operations.Add(new CopyOperation(source, destination, size) { AlreadyPresent = present });
        }

        return operations;
    }

    public static async Task<IReadOnlyList<CopyResult>> ExecuteAsync(IReadOnlyList<CopyOperation> plan, bool verify,
        IProgress<CopyProgress>? progress = null, CancellationToken token = default)
    {
        var results = new List<CopyResult>();
        var total = plan.Where(p => !p.AlreadyPresent).Sum(p => p.Size) * (verify ? 2 : 1);
        long done = 0;
        for (var i = 0; i < plan.Count; i++)
        {
            var operation = plan[i];
            var name = Path.GetFileName(operation.Source);
            if (operation.AlreadyPresent)
            {
                results.Add(new CopyResult(operation, Verified: false, Checksum: null));
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(operation.Destination)!);
            var temp = operation.Destination + ".part";
            string sourceHash;
            await using (var input = new FileStream(operation.Source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true))
            await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[4 << 20];
                int read;
                while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    done += read;
                    progress?.Report(new CopyProgress(i, plan.Count, name, done, total, "Kopieren"));
                }

                sourceHash = Convert.ToHexString(hash.GetHashAndReset());
            }

            File.Move(temp, operation.Destination, overwrite: true);
            File.SetLastWriteTimeUtc(operation.Destination, File.GetLastWriteTimeUtc(operation.Source));

            if (verify)
            {
                var copyHash = await HashFileAsync(operation.Destination, bytes =>
                {
                    done += bytes;
                    progress?.Report(new CopyProgress(i, plan.Count, name, done, total, "Prüfen"));
                }, token).ConfigureAwait(false);
                if (!string.Equals(copyHash, sourceHash, StringComparison.Ordinal))
                {
                    File.Delete(operation.Destination);
                    throw new IOException($"Prüfsumme stimmt nicht: {name}. Die Kopie wurde gelöscht – bitte Karte/Leser prüfen.");
                }
            }

            results.Add(new CopyResult(operation, verify, sourceHash));
        }

        return results;
    }

    public static async Task<string> HashFileAsync(string path, Action<int>? onRead = null, CancellationToken token = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[4 << 20];
        int read;
        while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            onRead?.Invoke(read);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['\\', '/', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "Kamera" : cleaned;
    }
}
