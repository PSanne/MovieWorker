namespace MovieWorker.Core.Export;

/// <summary>Eine Export-Vorlage: Codec, Qualität, Container.</summary>
public sealed record ExportPreset(
    string Id,
    string Name,
    string Description,
    string Extension,
    string PixelFormat,
    IReadOnlyList<string> VideoArguments,
    IReadOnlyList<string> AudioArguments,
    int? MaxHeight = null)
{
    public override string ToString() => Name;

    public static IReadOnlyList<ExportPreset> All { get; } =
    [
        new(
            "web-h264",
            "YouTube / Web – H.264 (Projektauflösung)",
            "Hohe Qualität (CRF 18), passt überall. Ideal zum Hochladen und Weitergeben.",
            ".mp4",
            "yuv420p",
            ["-c:v", "libx264", "-preset", "slow", "-crf", "18", "-profile:v", "high", "-movflags", "+faststart"],
            ["-c:a", "aac", "-b:a", "320k"]),
        new(
            "web-h264-1080",
            "YouTube / Web – H.264 Full HD",
            "Wie oben, aber auf 1080p verkleinert – kleinere Datei, schneller Upload.",
            ".mp4",
            "yuv420p",
            ["-c:v", "libx264", "-preset", "slow", "-crf", "18", "-profile:v", "high", "-movflags", "+faststart"],
            ["-c:a", "aac", "-b:a", "320k"],
            MaxHeight: 1080),
        new(
            "hevc-10bit",
            "H.265/HEVC 10 Bit",
            "Sehr effizient bei voller Qualität, 10 Bit ohne Banding. Für Archiv und moderne Geräte.",
            ".mp4",
            "yuv420p10le",
            ["-c:v", "libx265", "-preset", "medium", "-crf", "18", "-tag:v", "hvc1", "-movflags", "+faststart"],
            ["-c:a", "aac", "-b:a", "320k"]),
        new(
            "prores-hq",
            "Master – Apple ProRes 422 HQ",
            "Profi-Mastering-Format (10 Bit 4:2:2, unkomprimierter Ton). Sehr große Dateien.",
            ".mov",
            "yuv422p10le",
            ["-c:v", "prores_ks", "-profile:v", "3", "-vendor", "apl0"],
            ["-c:a", "pcm_s24le"]),
        new(
            "dnxhr-hqx",
            "Master – Avid DNxHR HQX",
            "Profi-Austauschformat (10 Bit 4:2:2), z. B. für DaVinci Resolve oder Avid.",
            ".mov",
            "yuv422p10le",
            ["-c:v", "dnxhd", "-profile:v", "dnxhr_hqx"],
            ["-c:a", "pcm_s24le"]),
    ];

    public static ExportPreset Default => All[0];
}
