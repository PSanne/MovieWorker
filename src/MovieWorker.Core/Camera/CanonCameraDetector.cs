using MovieWorker.Core.Media;

namespace MovieWorker.Core.Camera;

public enum DetectionConfidence
{
    Unknown,
    Likely,
    Certain,
}

/// <summary>Ergebnis der Kamera-Erkennung für einen Clip.</summary>
public sealed record CameraDetection(
    CanonCamera Camera,
    DetectionConfidence Confidence,
    ColorTransfer Transfer,
    bool IsHighFrameRate,
    IReadOnlyList<string> Hints)
{
    public string CameraName => CanonCameraProfile.For(Camera)?.DisplayName ?? "Unbekannte Kamera";
}

/// <summary>
/// Erkennt, ob ein Clip von der EOS R6 Mark II oder der EOS 70D stammt, und gibt Schnitt-Hinweise.
/// Canon schreibt das Kameramodell in eigene Datenblöcke, die ffprobe nicht ausliest. Deshalb wird – wenn
/// kein Modell-Tag vorhanden ist – anhand typischer Merkmale (Container, Codec, Ton, Auflösung) geschätzt.
/// Die Kamera lässt sich in der Oberfläche jederzeit von Hand korrigieren.
/// </summary>
public static class CanonCameraDetector
{
    /// <summary>Ab dieser Bildrate gilt ein Clip als Zeitlupen-Material (High Frame Rate).</summary>
    public const double HighFrameRateThreshold = 61;

    /// <param name="media">Der Clip.</param>
    /// <param name="manualCamera">Von Hand festgelegte Kamera – hat Vorrang vor der Erkennung.</param>
    public static CameraDetection Detect(MediaInfo media, CanonCamera? manualCamera = null)
    {
        var camera = DetectFromTags(media, out var certain);
        var confidence = certain ? DetectionConfidence.Certain : DetectionConfidence.Unknown;
        if (manualCamera is { } manual)
        {
            camera = manual;
            confidence = DetectionConfidence.Certain;
        }
        else if (camera == CanonCamera.Unknown)
        {
            camera = DetectFromFormat(media);
            confidence = camera == CanonCamera.Unknown ? DetectionConfidence.Unknown : DetectionConfidence.Likely;
        }

        var transfer = DetectTransfer(media, camera);
        var hfr = media.Video is { } v && v.FrameRate.Value > HighFrameRateThreshold;
        return new CameraDetection(camera, confidence, transfer, hfr, BuildHints(media, camera, transfer, hfr));
    }

    /// <summary>
    /// Bei der R6 Mark II zeichnet die Kamera nur mit Canon Log 3 oder HDR PQ in 10 Bit auf.
    /// HDR PQ ist in den Metadaten markiert – 10 Bit ohne PQ/HLG-Kennzeichen ist daher sehr wahrscheinlich Canon Log 3.
    /// </summary>
    public static ColorTransfer DetectTransfer(MediaInfo media, CanonCamera camera)
    {
        var fromMetadata = media.MetadataTransfer;
        if (fromMetadata != ColorTransfer.Rec709)
        {
            return fromMetadata;
        }

        if (camera == CanonCamera.EosR6MarkII && media.Video is { BitDepth: >= 10 })
        {
            return ColorTransfer.CanonLog3;
        }

        return ColorTransfer.Rec709;
    }

    private static CanonCamera DetectFromTags(MediaInfo media, out bool certain)
    {
        certain = false;
        foreach (var key in new[] { "model", "com.apple.quicktime.model", "camera_model", "make", "encoder", "comment" })
        {
            if (!media.Tags.TryGetValue(key, out var value))
            {
                continue;
            }

            var normalized = value.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
            if (normalized.Contains("R6MARKII", StringComparison.Ordinal) || normalized.Contains("R6MK2", StringComparison.Ordinal)
                || normalized.Contains("R6M2", StringComparison.Ordinal))
            {
                certain = true;
                return CanonCamera.EosR6MarkII;
            }

            if (normalized.Contains("EOS70D", StringComparison.Ordinal))
            {
                certain = true;
                return CanonCamera.Eos70D;
            }
        }

        return CanonCamera.Unknown;
    }

    private static CanonCamera DetectFromFormat(MediaInfo media)
    {
        if (media.Video is not { } video)
        {
            return CanonCamera.Unknown;
        }

        var extension = Path.GetExtension(media.Path).ToUpperInvariant();
        var canonName = Path.GetFileName(media.Path).ToUpperInvariant() is var name
            && (name.StartsWith("MVI_", StringComparison.Ordinal) || name.StartsWith("IMG_", StringComparison.Ordinal)
                || name.StartsWith("_MVI", StringComparison.Ordinal));

        // EOS 70D: MOV, H.264 8 Bit, höchstens Full HD, Ton immer LPCM 16 Bit.
        if (extension == ".MOV" && video.Codec == "h264" && video.Width <= 1920 && video.BitDepth == 8
            && (media.Audio is null || media.Audio.Codec == "pcm_s16le") && video.FrameRate.Value <= 61)
        {
            return CanonCamera.Eos70D;
        }

        // EOS R6 Mark II: MP4 mit H.264 oder HEVC (10 Bit 4:2:2 ist praktisch ein Fingerabdruck).
        if (extension == ".MP4" && (video.Codec == "hevc" || video.Codec == "h264"))
        {
            var hevc422 = video.Codec == "hevc" && video.ChromaSubsampling == "4:2:2";
            if (hevc422 || canonName || video.Width >= 3840 || video.FrameRate.Value > HighFrameRateThreshold)
            {
                return CanonCamera.EosR6MarkII;
            }
        }

        return CanonCamera.Unknown;
    }

    private static List<string> BuildHints(MediaInfo media, CanonCamera camera, ColorTransfer transfer, bool hfr)
    {
        var hints = new List<string>();
        if (transfer == ColorTransfer.CanonLog3)
        {
            hints.Add("Canon Log 3: Für normale Farben eine LUT (Canon Log 3 → BT.709) zuweisen.");
        }
        else if (transfer == ColorTransfer.HdrPq)
        {
            hints.Add("HDR PQ: Wird beim SDR-Export automatisch per Tone-Mapping nach Rec.709 umgerechnet.");
        }
        else if (transfer == ColorTransfer.HdrHlg)
        {
            hints.Add("HDR HLG: Für SDR-Export Tone-Mapping aktivieren.");
        }

        if (hfr && media.Video is { } video)
        {
            hints.Add($"Zeitlupen-Material ({video.FrameRate} fps): Im Clip „Als Zeitlupe konformieren“ wählen.");
        }

        if (media.Video is { Codec: "hevc", IsUhd: true })
        {
            hints.Add("4K-HEVC: Proxy erstellen, damit Vorschau und Schnitt flüssig laufen.");
        }

        if (camera == CanonCamera.Eos70D && media.FileSize >= SplitRecordingDetector.SplitSizeThreshold)
        {
            hints.Add("Datei hat die 4-GB-Grenze erreicht – die Aufnahme geht in der nächsten Datei weiter.");
        }

        return hints;
    }
}
