using System.Text.Json.Serialization;
using MovieWorker.Core.Camera;
using MovieWorker.Core.Media;

namespace MovieWorker.Core.Projects;

/// <summary>
/// Eine Aufnahme im Medienpool des Projekts. Die technischen Daten werden beim Import einmal
/// mit ffprobe gelesen und im Projekt gespeichert – so öffnet ein Projekt sofort, auch mit vielen Clips.
/// </summary>
public sealed class MediaItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>Die Datei(en) der Aufnahme – mehrere bei an der 4-GB-Grenze geteilten Aufnahmen.</summary>
    public List<MediaInfo> Parts { get; set; } = [];

    /// <summary>Von Hand festgelegte Kamera (null = automatisch erkennen).</summary>
    public CanonCamera? CameraOverride { get; set; }

    /// <summary>Optionale Proxy-Datei (kleine H.264-Kopie für flüssige Vorschau).</summary>
    public string? ProxyPath { get; set; }

    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>Ordner im Medienpool (null = nicht einsortiert).</summary>
    public Guid? BinId { get; set; }

    /// <summary>Bewertung 0–5 Sterne.</summary>
    public int Rating { get; set; }

    public List<string> Keywords { get; set; } = [];

    public string Notes { get; set; } = string.Empty;

    public string Scene { get; set; } = string.Empty;

    public string Take { get; set; } = string.Empty;

    /// <summary>Per Ton-Synchronisation verknüpfter externer Ton.</summary>
    public SyncedAudio? SyncedAudio { get; set; }

    [JsonIgnore]
    public MediaInfo First => Parts[0];

    [JsonIgnore]
    public RecordingGroup Recording => new(Parts);

    [JsonIgnore]
    public TimeSpan Duration => Recording.TotalDuration;

    [JsonIgnore]
    public string PrimaryPath => Parts.Count > 0 ? Parts[0].Path : string.Empty;

    [JsonIgnore]
    public CameraDetection Detection => CanonCameraDetector.Detect(First, CameraOverride);

    [JsonIgnore]
    public bool IsAudioOnly => !First.HasVideo && First.HasAudio;

    [JsonIgnore]
    public bool HasProxy => !string.IsNullOrEmpty(ProxyPath) && File.Exists(ProxyPath);

    /// <summary>Datei für die Vorschau: Proxy, falls vorhanden (und erlaubt), sonst das Original.</summary>
    [JsonIgnore]
    public string PreviewPath => HasProxy && UseProxies ? ProxyPath! : PrimaryPath;

    /// <summary>Vorschau mit Proxys (Einstellung für alle Aufnahmen, wird von der App gesetzt).</summary>
    public static bool UseProxies { get; set; } = true;

    public static MediaItem FromRecording(RecordingGroup recording) => new()
    {
        Name = recording.DisplayName,
        Parts = [.. recording.Parts],
    };
}
