using System.Text.Json.Serialization;
using MovieWorker.Core.Media;
using MovieWorker.Core.Projects;
using MovieWorker.Core.Time;

namespace MovieWorker.Core.Timeline;

public enum TrackKind
{
    Video,
    Audio,
}

/// <summary>Wie die Farben eines Clips für den Export umgerechnet werden.</summary>
public enum ColorTransform
{
    /// <summary>Keine Umrechnung (Rec.709-Material, z. B. EOS 70D oder R6 Mark II mit Standard-Bildstil).</summary>
    None,

    /// <summary>Eine 3D-LUT (.cube) anwenden – typisch für Canon Log 3 → BT.709.</summary>
    Lut,

    /// <summary>HDR (PQ/HLG) per Tone-Mapping auf SDR Rec.709 bringen.</summary>
    HdrToSdr,
}

/// <summary>Projekteinstellungen: Auflösung und Bildrate der Timeline (= des Exports).</summary>
public sealed class ProjectSettings
{
    public int Width { get; set; } = 3840;

    public int Height { get; set; } = 2160;

    public int FrameRateNumerator { get; set; } = 25;

    public int FrameRateDenominator { get; set; } = 1;

    public int AudioSampleRate { get; set; } = 48000;

    public bool DropFrameTimecode { get; set; }

    [JsonIgnore]
    public FrameRate FrameRate
    {
        get => new(FrameRateNumerator, FrameRateDenominator);
        set
        {
            FrameRateNumerator = value.Numerator;
            FrameRateDenominator = value.Denominator;
        }
    }

    [JsonIgnore]
    public string Label => $"{Width}×{Height} · {FrameRate} fps";

    public ProjectSettings Clone() => (ProjectSettings)MemberwiseClone();

    /// <summary>Übernimmt Auflösung und Bildrate aus einem Clip (Zeitlupen-Clips ergeben eine normale Bildrate).</summary>
    public static ProjectSettings FromMedia(MediaInfo media)
    {
        var settings = new ProjectSettings();
        if (media.Video is { } video)
        {
            settings.Width = video.Width;
            settings.Height = video.Height;
            settings.FrameRate = video.FrameRate.Value > 61
                ? (video.FrameRate.Denominator == 1001 ? FrameRate.Fps29_97 : FrameRate.Fps25)
                : video.FrameRate;
        }

        return settings;
    }
}

/// <summary>
/// Ein Clip auf der Timeline. Positionen auf der Timeline zählen in Projektbildern, die Stelle in der
/// Quelldatei in Sekunden – so können Clips mit anderer Bildrate (z. B. Zeitlupe) frei gemischt werden.
/// </summary>
public sealed class TimelineClip
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Verweis auf den Eintrag im Medienpool (für Proxy und Kamera-Infos).</summary>
    public Guid? MediaId { get; set; }

    /// <summary>Pfade der Quelldatei(en) – mehrere bei an der 4-GB-Grenze geteilten Aufnahmen.</summary>
    public List<string> SourcePaths { get; set; } = [];

    /// <summary>Länge der einzelnen Teildateien in Sekunden (gleiche Reihenfolge wie <see cref="SourcePaths"/>).</summary>
    public List<double> PartDurations { get; set; } = [];

    public string Name { get; set; } = string.Empty;

    /// <summary>Gesamtlänge der Quelle in Sekunden (begrenzt das Trimmen).</summary>
    public double SourceDuration { get; set; }

    /// <summary>Startpunkt in der Quelle (Sekunden).</summary>
    public double SourceIn { get; set; }

    /// <summary>Erstes Bild auf der Timeline.</summary>
    public long StartFrame { get; set; }

    /// <summary>Länge auf der Timeline in Projektbildern.</summary>
    public long LengthFrames { get; set; }

    /// <summary>Abspielgeschwindigkeit: 1 = normal, 0,2 = fünffache Zeitlupe.</summary>
    public double Speed { get; set; } = 1.0;

    public bool HasVideo { get; set; } = true;

    public bool HasAudio { get; set; } = true;

    /// <summary>Lautstärke des Kameratons in dB.</summary>
    public double VolumeDb { get; set; }

    public bool AudioMuted { get; set; }

    public long FadeInFrames { get; set; }

    public long FadeOutFrames { get; set; }

    public ColorTransform ColorTransform { get; set; }

    public string? LutPath { get; set; }

    /// <summary>Optionale Proxy-Datei für flüssige Vorschau (wird beim Export nicht verwendet).</summary>
    public string? ProxyPath { get; set; }

    [JsonIgnore]
    public long EndFrame => StartFrame + LengthFrames;

    [JsonIgnore]
    public string PrimaryPath => SourcePaths.Count > 0 ? SourcePaths[0] : string.Empty;

    public bool Contains(long frame) => frame >= StartFrame && frame < EndFrame;

    /// <summary>Wie viele Sekunden der Quelle der Clip verbraucht.</summary>
    public double SourceLength(FrameRate rate) => rate.FramesToSeconds(LengthFrames) * Speed;

    public double SourceOut(FrameRate rate) => SourceIn + SourceLength(rate);

    /// <summary>Stelle in der Quelle (Sekunden) für ein Timeline-Bild.</summary>
    public double SourceTimeAt(long frame, FrameRate rate) => SourceIn + rate.FramesToSeconds(frame - StartFrame) * Speed;

    public TimelineClip Clone()
    {
        var copy = (TimelineClip)MemberwiseClone();
        copy.SourcePaths = [.. SourcePaths];
        copy.PartDurations = [.. PartDurations];
        return copy;
    }
}

public sealed class TimelineTrack
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public TrackKind Kind { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool Muted { get; set; }

    public double VolumeDb { get; set; }

    public List<TimelineClip> Clips { get; set; } = [];

    [JsonIgnore]
    public IEnumerable<TimelineClip> OrderedClips => Clips.OrderBy(c => c.StartFrame);

    public TimelineClip? ClipAt(long frame) => Clips.FirstOrDefault(c => c.Contains(frame));

    /// <summary>Prüft, ob der Bereich frei ist (optional ohne einen bestimmten Clip).</summary>
    public bool IsFree(long start, long length, Guid? ignore = null) =>
        start >= 0 && Clips.All(c => c.Id == ignore || c.EndFrame <= start || c.StartFrame >= start + length);

    public TimelineTrack Clone()
    {
        var copy = (TimelineTrack)MemberwiseClone();
        copy.Clips = Clips.Select(c => c.Clone()).ToList();
        return copy;
    }
}

/// <summary>Das komplette Schnittprojekt.</summary>
public sealed class MovieProject
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;

    public ProjectSettings Settings { get; set; } = new();

    public List<TimelineTrack> Tracks { get; set; } = [];

    /// <summary>Medienpool: alle importierten Aufnahmen (auch die, die noch nicht in der Timeline liegen).</summary>
    public List<MediaItem> Media { get; set; } = [];

    public MediaItem? FindMedia(Guid? id) => id is { } value ? Media.FirstOrDefault(m => m.Id == value) : null;

    [JsonIgnore]
    public IEnumerable<TimelineTrack> VideoTracks => Tracks.Where(t => t.Kind == TrackKind.Video);

    [JsonIgnore]
    public IEnumerable<TimelineTrack> AudioTracks => Tracks.Where(t => t.Kind == TrackKind.Audio);

    [JsonIgnore]
    public long DurationFrames => Tracks.SelectMany(t => t.Clips).Select(c => c.EndFrame).DefaultIfEmpty(0).Max();

    [JsonIgnore]
    public IEnumerable<TimelineClip> AllClips => Tracks.SelectMany(t => t.Clips);

    /// <summary>Neues Projekt mit einer Videospur (V1) und zwei Tonspuren (Musik, Sprache/Atmo).</summary>
    public static MovieProject CreateDefault(ProjectSettings? settings = null) => new()
    {
        Settings = settings ?? new ProjectSettings(),
        Tracks =
        [
            new TimelineTrack { Kind = TrackKind.Video, Name = "V1" },
            new TimelineTrack { Kind = TrackKind.Audio, Name = "A1 Musik" },
            new TimelineTrack { Kind = TrackKind.Audio, Name = "A2 Ton" },
        ],
    };

    public (TimelineTrack Track, TimelineClip Clip)? FindClip(Guid clipId)
    {
        foreach (var track in Tracks)
        {
            var clip = track.Clips.FirstOrDefault(c => c.Id == clipId);
            if (clip is not null)
            {
                return (track, clip);
            }
        }

        return null;
    }

    public MovieProject Clone() => new()
    {
        FormatVersion = FormatVersion,
        Settings = Settings.Clone(),
        Tracks = Tracks.Select(t => t.Clone()).ToList(),
        Media = [.. Media],
    };
}
