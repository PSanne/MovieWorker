namespace MovieWorker.Core.Export;

public enum PresetCategory
{
    Web,
    Social,
    Master,
    Archiv,
    Ton,
}

/// <summary>Wie das Bild an ein anderes Seitenverhältnis angepasst wird (z. B. 16:9 → 9:16).</summary>
public enum ReframeMode
{
    /// <summary>Ganzes Bild zeigen, Rand schwarz.</summary>
    Fit,

    /// <summary>Bildfüllend, Ränder werden abgeschnitten (Mitte bleibt).</summary>
    Fill,

    /// <summary>Ganzes Bild, Hintergrund mit unscharfer, vergrößerter Kopie gefüllt (typisch für Hochformat).</summary>
    BlurBackground,
}

/// <summary>Eine Export-Vorlage: Codec, Qualität, Container und ggf. feste Ausgabegröße.</summary>
public sealed record ExportPreset(
    string Id,
    string Name,
    string Description,
    PresetCategory Category,
    string Extension,
    string PixelFormat,
    IReadOnlyList<string> VideoArguments,
    IReadOnlyList<string> AudioArguments,
    int? MaxHeight = null,
    int? OutputWidth = null,
    int? OutputHeight = null,
    ReframeMode Reframe = ReframeMode.BlurBackground,
    bool AudioOnly = false,
    double? MaxFrameRate = null,
    bool HdrOutput = false)
{
    public override string ToString() => Name;

    public string CategoryLabel => Category switch
    {
        PresetCategory.Web => "Web & YouTube",
        PresetCategory.Social => "Social Media",
        PresetCategory.Master => "Master & Austausch",
        PresetCategory.Archiv => "Archiv",
        _ => "Nur Ton",
    };

    public bool HasFixedSize => OutputWidth is not null && OutputHeight is not null;

    private static readonly string[] X264Web = ["-c:v", "libx264", "-preset", "slow", "-crf", "18", "-profile:v", "high", "-movflags", "+faststart"];
    private static readonly string[] X264Social = ["-c:v", "libx264", "-preset", "slow", "-crf", "19", "-profile:v", "high", "-maxrate", "25M", "-bufsize", "50M", "-movflags", "+faststart"];
    private static readonly string[] Aac320 = ["-c:a", "aac", "-b:a", "320k"];
    private static readonly string[] Pcm24 = ["-c:a", "pcm_s24le"];

    public static IReadOnlyList<ExportPreset> All { get; } =
    [
        new("web-h264", "YouTube – Projektauflösung (H.264)",
            "Hohe Qualität (CRF 18) in der Auflösung des Projekts, z. B. 4K. Passt für YouTube, Vimeo und zum Weitergeben.",
            PresetCategory.Web, ".mp4", "yuv420p", X264Web, Aac320),
        new("web-h264-1080", "YouTube / Vimeo – Full HD (H.264)",
            "Auf 1080p verkleinert – kleinere Datei, schneller Upload, läuft überall.",
            PresetCategory.Web, ".mp4", "yuv420p", X264Web, Aac320, MaxHeight: 1080),
        new("web-h265-4k", "YouTube – 4K effizient (H.265)",
            "Gleiche Qualität bei etwa halber Dateigröße. YouTube und moderne Geräte spielen H.265 ab.",
            PresetCategory.Web, ".mp4", "yuv420p", ["-c:v", "libx265", "-preset", "medium", "-crf", "20", "-tag:v", "hvc1", "-movflags", "+faststart"], Aac320),
        new("social-vertical", "Instagram Reels / TikTok / Shorts – Hochformat 9:16",
            "1080×1920, H.264. Querformat wird mittig gezeigt, der Rand mit unscharfem Bild gefüllt (umstellbar: zuschneiden oder schwarz).",
            PresetCategory.Social, ".mp4", "yuv420p", X264Social, ["-c:a", "aac", "-b:a", "256k"],
            OutputWidth: 1080, OutputHeight: 1920, Reframe: ReframeMode.BlurBackground, MaxFrameRate: 60),
        new("social-square", "Instagram Feed – Quadrat 1:1",
            "1080×1080, H.264. Querformat wird mittig zugeschnitten.",
            PresetCategory.Social, ".mp4", "yuv420p", X264Social, ["-c:a", "aac", "-b:a", "256k"],
            OutputWidth: 1080, OutputHeight: 1080, Reframe: ReframeMode.Fill, MaxFrameRate: 60),
        new("social-portrait", "Instagram Feed – Hochkant 4:5",
            "1080×1350, H.264. Querformat wird mittig gezeigt, Rand unscharf gefüllt.",
            PresetCategory.Social, ".mp4", "yuv420p", X264Social, ["-c:a", "aac", "-b:a", "256k"],
            OutputWidth: 1080, OutputHeight: 1350, Reframe: ReframeMode.BlurBackground, MaxFrameRate: 60),
        new("prores-hq", "Master – Apple ProRes 422 HQ",
            "Profi-Mastering-Format (10 Bit 4:2:2, unkomprimierter Ton). Sehr große Dateien.",
            PresetCategory.Master, ".mov", "yuv422p10le", ["-c:v", "prores_ks", "-profile:v", "3", "-vendor", "apl0"], Pcm24),
        new("prores-lt", "Master – Apple ProRes 422 LT",
            "Wie HQ, aber etwa halb so groß – gut für Weitergabe an Kunden oder Tonmischung.",
            PresetCategory.Master, ".mov", "yuv422p10le", ["-c:v", "prores_ks", "-profile:v", "1", "-vendor", "apl0"], Pcm24),
        new("dnxhr-hqx", "Master – Avid DNxHR HQX",
            "Profi-Austauschformat (10 Bit 4:2:2), z. B. für DaVinci Resolve oder Avid.",
            PresetCategory.Master, ".mov", "yuv422p10le", ["-c:v", "dnxhd", "-profile:v", "dnxhr_hqx"], Pcm24),
        new("hevc-10bit", "Archiv – H.265 10 Bit",
            "Sehr effizient bei voller Qualität, 10 Bit ohne Banding. Für das eigene Archiv.",
            PresetCategory.Archiv, ".mp4", "yuv420p10le", ["-c:v", "libx265", "-preset", "medium", "-crf", "16", "-tag:v", "hvc1", "-movflags", "+faststart"], Aac320),
        new("hevc-hdr10", "HDR10 – H.265 10 Bit (PQ, BT.2020)",
            "Echtes HDR für HDR-Fernseher, YouTube HDR und moderne Smartphones. HDR-PQ-Aufnahmen der R6 Mark II bleiben HDR, normales Material wird umgerechnet.",
            PresetCategory.Archiv, ".mp4", "yuv420p10le",
            ["-c:v", "libx265", "-preset", "medium", "-crf", "18", "-tag:v", "hvc1",
             "-x265-params", "hdr-opt=1:repeat-headers=1:colorprim=bt2020:transfer=smpte2084:colormatrix=bt2020nc:master-display=G(13250,34500)B(7500,3000)R(34000,16000)WP(15635,16450)L(10000000,1):max-cll=1000,400",
             "-movflags", "+faststart"],
            Aac320, HdrOutput: true),
        new("audio-wav", "Nur Ton – WAV (24 Bit)",
            "Die fertige Tonmischung unkomprimiert, z. B. für Mastering oder Podcast.",
            PresetCategory.Ton, ".wav", "", [], Pcm24, AudioOnly: true),
        new("audio-mp3", "Nur Ton – MP3 (320 kbit/s)",
            "Die fertige Tonmischung als MP3.",
            PresetCategory.Ton, ".mp3", "", [], ["-c:a", "libmp3lame", "-b:a", "320k"], AudioOnly: true),
    ];

    public static ExportPreset Default => All[0];

    /// <summary>Interne Vorlage: ein einzelnes PNG-Bild über die Export-Kette (für exakte Vorschau-Standbilder).</summary>
    public static ExportPreset StillImage(int height) => new(
        "still", "Standbild", "Vorschau", PresetCategory.Web, ".png", "yuv420p", [], [], MaxHeight: Math.Max(2, height / 2 * 2));

    /// <summary>Interne Vorlage: Rohbild (RGB24) für Messinstrumente.</summary>
    public static ExportPreset RawImage(int height) => new(
        "stillraw", "Rohbild", "Messinstrumente", PresetCategory.Web, ".rgb", "yuv420p", [], [], MaxHeight: Math.Max(2, height / 2 * 2));

    public bool IsStillImage => Id is "still" or "stillraw";

    public static ExportPreset? Find(string? id) => All.FirstOrDefault(p => p.Id == id);

    /// <summary>Kopie mit anderer Bildanpassung (z. B. Hochformat zuschneiden statt unscharfem Rand).</summary>
    public ExportPreset WithReframe(ReframeMode mode) => this with { Reframe = mode };
}
