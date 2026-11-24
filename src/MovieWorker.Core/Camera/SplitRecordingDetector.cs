using System.Text.RegularExpressions;
using MovieWorker.Core.Media;

namespace MovieWorker.Core.Camera;

/// <summary>Eine zusammenhängende Aufnahme, die auf der Karte aus einer oder mehreren Dateien besteht.</summary>
public sealed record RecordingGroup(IReadOnlyList<MediaInfo> Parts)
{
    public MediaInfo First => Parts[0];

    public bool IsSplit => Parts.Count > 1;

    public TimeSpan TotalDuration => TimeSpan.FromTicks(Parts.Sum(p => p.Duration.Ticks));

    public string DisplayName => IsSplit
        ? $"{First.FileName} (+{Parts.Count - 1} Teil{(Parts.Count > 2 ? "e" : string.Empty)})"
        : First.FileName;
}

/// <summary>
/// Kameras wie die EOS 70D beenden eine Datei bei knapp 4 GB (FAT32-Grenze) und filmen nahtlos in der
/// nächsten Datei weiter (MVI_0001.MOV → MVI_0002.MOV). Diese Klasse findet solche Folgen,
/// damit sie im Schnitt als ein einziger Clip erscheinen.
/// </summary>
public static partial class SplitRecordingDetector
{
    /// <summary>Ab dieser Größe gilt eine Datei als „an der 4-GB-Grenze abgeschnitten“.</summary>
    public const long SplitSizeThreshold = 3_900_000_000;

    public static IReadOnlyList<RecordingGroup> Group(IEnumerable<MediaInfo> files)
    {
        var ordered = files.OrderBy(f => Path.GetDirectoryName(f.Path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => Path.GetFileName(f.Path), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var groups = new List<RecordingGroup>();
        var current = new List<MediaInfo>();
        foreach (var file in ordered)
        {
            if (current.Count > 0 && !IsContinuation(current[^1], file))
            {
                groups.Add(new RecordingGroup(current));
                current = [];
            }

            current.Add(file);
        }

        if (current.Count > 0)
        {
            groups.Add(new RecordingGroup(current));
        }

        return groups;
    }

    /// <summary>Ist <paramref name="next"/> die Fortsetzung von <paramref name="previous"/>?</summary>
    public static bool IsContinuation(MediaInfo previous, MediaInfo next)
    {
        if (previous.FileSize < SplitSizeThreshold)
        {
            return false;
        }

        if (!string.Equals(Path.GetDirectoryName(previous.Path), Path.GetDirectoryName(next.Path), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!TryParseName(previous.Path, out var prefixA, out var numberA, out var extA)
            || !TryParseName(next.Path, out var prefixB, out var numberB, out var extB))
        {
            return false;
        }

        var consecutive = numberB == numberA + 1 || (numberA == 9999 && numberB == 1);
        if (!consecutive || !string.Equals(prefixA, prefixB, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(extA, extB, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return previous.Video is { } a && next.Video is { } b
            && a.Codec == b.Codec && a.Width == b.Width && a.Height == b.Height && a.FrameRate == b.FrameRate;
    }

    private static bool TryParseName(string path, out string prefix, out int number, out string extension)
    {
        var match = CanonFileName().Match(Path.GetFileName(path));
        prefix = match.Success ? match.Groups["prefix"].Value : string.Empty;
        number = match.Success ? int.Parse(match.Groups["number"].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
        extension = match.Success ? match.Groups["ext"].Value : string.Empty;
        return match.Success;
    }

    [GeneratedRegex(@"^(?<prefix>[A-Z_]{3,5}?_?)(?<number>\d{4})\.(?<ext>[A-Za-z0-9]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex CanonFileName();
}
