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

    /// <summary>Feste Bildlage (Position, Zoom, Drehung, Deckkraft).</summary>
    public ClipTransform Transform { get; set; } = new();

    /// <summary>Keyframes je Eigenschaft (Bilder relativ zum Clipanfang).</summary>
    public List<KeyframeTrack> Keyframes { get; set; } = [];

    /// <summary>Verschachtelter Clip: enthält eigene Spuren (null = normaler Clip).</summary>
    public List<TimelineTrack>? NestedTracks { get; set; }

    [JsonIgnore]
    public bool IsNested => NestedTracks is not null;

    public KeyframeTrack? KeyframesFor(AnimatedProperty property) =>
        Keyframes.FirstOrDefault(k => k.Property == property && k.Keys.Count > 0);

    public bool IsAnimated(AnimatedProperty property) => KeyframesFor(property) is not null;

    /// <summary>Wert einer Eigenschaft an einem Bild relativ zum Clipanfang (Keyframes, sonst fester Wert).</summary>
    public double ValueAt(AnimatedProperty property, double frameInClip) =>
        KeyframesFor(property) is { } track
            ? track.ValueAt(frameInClip)
            : property == AnimatedProperty.Volume ? VolumeDb : Transform.Get(property);

    /// <summary>Setzt den Wert – mit Keyframes als Keyframe an dieser Stelle, sonst als festen Wert.</summary>
    public void SetValue(AnimatedProperty property, double value, long frameInClip, bool addKeyframe)
    {
        var track = Keyframes.FirstOrDefault(k => k.Property == property);
        if (addKeyframe || track is { Keys.Count: > 0 })
        {
            if (track is null)
            {
                track = new KeyframeTrack { Property = property };
                Keyframes.Add(track);
            }

            var key = track.Keys.FirstOrDefault(k => k.Frame == frameInClip);
            if (key is null)
            {
                track.Keys.Add(new Keyframe { Frame = frameInClip, Value = value });
            }
            else
            {
                key.Value = value;
            }
        }
        else if (property == AnimatedProperty.Volume)
        {
            VolumeDb = value;
        }
        else
        {
            Transform.Set(property, value);
        }
    }

    /// <summary>Verschiebt alle Keyframes (wenn sich der Clipanfang ändert, bleiben sie am selben Bildinhalt).</summary>
    public void ShiftKeyframes(long delta)
    {
        if (delta == 0)
        {
            return;
        }

        foreach (var key in Keyframes.SelectMany(k => k.Keys))
        {
            key.Frame += delta;
        }
    }

    /// <summary>Hat der Clip eine Bildlage, die beim Export berechnet werden muss?</summary>
    [JsonIgnore]
    public bool HasTransform =>
        Keyframes.Any(k => k.Property != AnimatedProperty.Volume && k.Keys.Count > 0)
        || Math.Abs(Transform.PositionX) > 1e-6 || Math.Abs(Transform.PositionY) > 1e-6
        || Math.Abs(Transform.Scale - 100) > 1e-6 || Math.Abs(Transform.Rotation) > 1e-6 || Math.Abs(Transform.Opacity - 100) > 1e-6;

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
        copy.Transform = Transform.Clone();
        copy.Keyframes = Keyframes.Select(k => k.Clone()).ToList();
        copy.NestedTracks = NestedTracks?.Select(t => t.Clone()).ToList();
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

    /// <summary>In-Marke der Timeline (z. B. für „nur Bereich exportieren“).</summary>
    public long? MarkIn { get; set; }

    /// <summary>Out-Marke der Timeline (exklusiv).</summary>
    public long? MarkOut { get; set; }

    [JsonIgnore]
    public bool HasRange => MarkIn is { } i && MarkOut is { } o && o > i;

    /// <summary>Medienpool: alle importierten Aufnahmen (auch die, die noch nicht in der Timeline liegen).</summary>
    public List<MediaItem> Media { get; set; } = [];

    /// <summary>Ordner (Bins) des Medienpools.</summary>
    public List<MediaBin> Bins { get; set; } = [];

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
        Bins = [.. Bins],
        MarkIn = MarkIn,
        MarkOut = MarkOut,
    };
}
