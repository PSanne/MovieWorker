using MovieWorker.Core.Time;

namespace MovieWorker.Core.Camera;

public enum CanonCamera
{
    Unknown,
    EosR6MarkII,
    Eos70D,
}

/// <summary>Ein Aufnahmemodus der Kamera (Auflösung × Bildrate).</summary>
public sealed record CameraVideoMode(int Width, int Height, FrameRate FrameRate, string Note = "");

/// <summary>
/// Steckbrief einer unterstützten Kamera: was sie aufnimmt und was das für den Schnitt bedeutet.
/// </summary>
public sealed record CanonCameraProfile(
    CanonCamera Camera,
    string DisplayName,
    string Container,
    IReadOnlyList<string> VideoCodecs,
    IReadOnlyList<string> AudioFormats,
    bool SupportsCanonLog3,
    bool SupportsHdrPq,
    bool SplitsAt4GB,
    IReadOnlyList<CameraVideoMode> Modes,
    IReadOnlyList<string> EditingTips)
{
    /// <summary>Die beiden Kameras, für die MovieWorker optimiert ist.</summary>
    public static IReadOnlyList<CanonCameraProfile> All { get; } =
    [
        new(
            CanonCamera.EosR6MarkII,
            "Canon EOS R6 Mark II",
            "MP4",
            ["H.264 8 Bit 4:2:0", "H.265/HEVC 10 Bit 4:2:2 (bei Canon Log 3 und HDR PQ)"],
            ["AAC", "LPCM 48 kHz"],
            SupportsCanonLog3: true,
            SupportsHdrPq: true,
            SplitsAt4GB: false,
            Modes:
            [
                new(3840, 2160, FrameRate.Fps59_94),
                new(3840, 2160, FrameRate.Fps50),
                new(3840, 2160, FrameRate.Fps29_97),
                new(3840, 2160, FrameRate.Fps25),
                new(3840, 2160, FrameRate.Fps23_976),
                new(1920, 1080, FrameRate.Fps59_94),
                new(1920, 1080, FrameRate.Fps50),
                new(1920, 1080, FrameRate.Fps29_97),
                new(1920, 1080, FrameRate.Fps25),
                new(1920, 1080, FrameRate.Fps23_976),
                new(1920, 1080, new FrameRate(180000, 1001), "High Frame Rate, ohne Ton"),
                new(1920, 1080, new FrameRate(150), "High Frame Rate, ohne Ton"),
            ],
            EditingTips:
            [
                "4K-HEVC in 10 Bit ist sehr rechenintensiv: Für flüssigen Schnitt Proxys erstellen.",
                "Canon Log 3 braucht eine LUT (z. B. die offizielle Canon-LUT „Canon Log 3 → BT.709“) für normale Farben.",
                "HDR-PQ-Material wird für normale Bildschirme/YouTube-SDR per Tone-Mapping nach Rec.709 umgerechnet.",
                "High-Frame-Rate-Clips (150/180 fps) als Zeitlupe auf die Projektbildrate konformieren.",
            ]),
        new(
            CanonCamera.Eos70D,
            "Canon EOS 70D",
            "MOV",
            ["H.264 8 Bit 4:2:0 (ALL-I oder IPB)"],
            ["LPCM 48 kHz 16 Bit Stereo"],
            SupportsCanonLog3: false,
            SupportsHdrPq: false,
            SplitsAt4GB: true,
            Modes:
            [
                new(1920, 1080, FrameRate.Fps29_97),
                new(1920, 1080, FrameRate.Fps25),
                new(1920, 1080, FrameRate.Fps23_976),
                new(1280, 720, FrameRate.Fps59_94),
                new(1280, 720, FrameRate.Fps50),
                new(640, 480, FrameRate.Fps29_97),
                new(640, 480, FrameRate.Fps25),
            ],
            EditingTips:
            [
                "Aufnahmen über 4 GB teilt die Kamera in mehrere Dateien – MovieWorker fügt sie beim Import wieder zusammen.",
                "Die .THM-Dateien neben den Videos sind nur Vorschaubilder und werden beim Import übersprungen.",
                "In einem 4K-Projekt zusammen mit R6-Mark-II-Material wird Full HD hochskaliert – besser 1080p-Projekt oder 70D als B-Kamera für Nahaufnahmen.",
                "Neutraler Bildstil mit reduziertem Kontrast/Schärfe lässt beim Farbabgleich mehr Spielraum.",
            ]),
    ];

    public static CanonCameraProfile? For(CanonCamera camera) => All.FirstOrDefault(p => p.Camera == camera);
}
