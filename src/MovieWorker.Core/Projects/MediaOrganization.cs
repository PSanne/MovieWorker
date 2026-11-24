using MovieWorker.Core.Camera;

namespace MovieWorker.Core.Projects;

/// <summary>Ein Ordner (Bin) im Medienpool.</summary>
public sealed class MediaBin
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "Neuer Ordner";
}

/// <summary>Verknüpfung einer Kameraaufnahme mit externem Ton (Rekorder), per Synchronisation ermittelt.</summary>
public sealed class SyncedAudio
{
    public Guid AudioMediaId { get; set; }

    /// <summary>Zeit im externen Ton = Zeit im Video + Offset (Sekunden).</summary>
    public double OffsetSeconds { get; set; }

    public double Confidence { get; set; }

    /// <summary>Beim Einfügen in die Timeline den Kameraton stummschalten.</summary>
    public bool ReplaceCameraAudio { get; set; } = true;
}

public enum MediaKindFilter
{
    All,
    Video,
    Audio,
}

public enum MediaSort
{
    Name,
    Recorded,
    Rating,
    Duration,
}

/// <summary>Suche und Filter im Medienpool.</summary>
public sealed record MediaFilter(
    string? Text = null,
    int MinRating = 0,
    MediaKindFilter Kind = MediaKindFilter.All,
    CanonCamera? Camera = null,
    Guid? BinId = null,
    bool OnlyUnsorted = false)
{
    public bool Matches(MediaItem item)
    {
        if (item.Rating < MinRating)
        {
            return false;
        }

        if (Kind == MediaKindFilter.Video && item.IsAudioOnly || Kind == MediaKindFilter.Audio && !item.IsAudioOnly)
        {
            return false;
        }

        if (Camera is { } camera && item.Detection.Camera != camera)
        {
            return false;
        }

        if (BinId is { } bin && item.BinId != bin)
        {
            return false;
        }

        if (OnlyUnsorted && item.BinId is not null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(Text))
        {
            return true;
        }

        // Alle Suchwörter müssen vorkommen – in Name, Schlagwörtern, Notizen, Szene oder Take.
        var haystack = string.Join(' ', [item.Name, item.Notes, item.Scene, item.Take, .. item.Keywords]).ToLowerInvariant();
        return Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(word => haystack.Contains(word.ToLowerInvariant()));
    }

    public static IEnumerable<MediaItem> Sort(IEnumerable<MediaItem> items, MediaSort sort) => sort switch
    {
        MediaSort.Recorded => items.OrderBy(i => i.First.CreationTime ?? DateTimeOffset.MaxValue).ThenBy(i => i.Name),
        MediaSort.Rating => items.OrderByDescending(i => i.Rating).ThenBy(i => i.Name),
        MediaSort.Duration => items.OrderByDescending(i => i.Duration),
        _ => items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase),
    };

    /// <summary>Wandelt „Hochzeit, Kirche,  Ringe“ in eine saubere Schlagwortliste.</summary>
    public static List<string> ParseKeywords(string? text) =>
        (text ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
