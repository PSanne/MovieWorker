using MovieWorker.Core.Camera;
using MovieWorker.Core.Media;

namespace MovieWorker.Core.Projects;

/// <summary>Ergebnis eines Imports: neue Einträge für den Medienpool und Dateien, die nicht gelesen werden konnten.</summary>
public sealed record MediaImportResult(IReadOnlyList<MediaItem> Items, IReadOnlyList<(string Path, string Error)> Failed);

/// <summary>Liest Dateien mit ffprobe ein (parallel) und fasst geteilte Aufnahmen zusammen.</summary>
public static class MediaImport
{
    public static async Task<MediaImportResult> ProbeAsync(IEnumerable<string> paths, Func<string, CancellationToken, Task<MediaInfo>> probe,
        IProgress<(int Done, int Total)>? progress = null, int parallelism = 4, CancellationToken token = default)
    {
        var files = paths.Where(CardScanner.IsMediaFile)
            .Where(p => !Path.GetFileName(p).StartsWith("._", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var infos = new MediaInfo?[files.Count];
        var failed = new List<(string, string)>();
        var done = 0;
        using var gate = new SemaphoreSlim(parallelism);
        var tasks = files.Select(async (file, index) =>
        {
            await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var info = await probe(file, token).ConfigureAwait(false);
                if (info.HasVideo || info.HasAudio)
                {
                    infos[index] = info;
                }
                else
                {
                    lock (failed)
                    {
                        failed.Add((file, "Enthält weder Bild noch Ton."));
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lock (failed)
                {
                    failed.Add((file, ex.Message));
                }
            }
            finally
            {
                gate.Release();
                progress?.Report((Interlocked.Increment(ref done), files.Count));
            }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);

        var groups = SplitRecordingDetector.Group(infos.Where(i => i is not null)!);
        return new MediaImportResult(groups.Select(MediaItem.FromRecording).ToList(), failed);
    }
}
