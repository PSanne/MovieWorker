using MovieWorker.Core.Time;

namespace MovieWorker.Core.Media;

/// <summary>Kennlinie/Farbraum des Videos – entscheidet, ob vor dem Export umgerechnet werden muss.</summary>
public enum ColorTransfer
{
    /// <summary>Normales SDR-Video (Rec.709) – so filmen z. B. alle Bildstile der EOS 70D.</summary>
    Rec709,

    /// <summary>HDR mit PQ-Kennlinie (Rec.2100 PQ, z. B. „HDR PQ“ der EOS R6 Mark II).</summary>
    HdrPq,

    /// <summary>HDR mit HLG-Kennlinie.</summary>
    HdrHlg,

    /// <summary>Canon Log 3 – flaches Log-Material, das eine LUT bzw. Farbkorrektur braucht.</summary>
    CanonLog3,
}

/// <summary>Technische Daten des Videostroms.</summary>
public sealed record VideoStreamInfo(
    string Codec,
    string? Profile,
    int Width,
    int Height,
    FrameRate FrameRate,
    string PixelFormat,
    int BitDepth,
    string ChromaSubsampling,
    long? BitRate,
    string? ColorTransferRaw,
    string? ColorPrimariesRaw)
{
    public bool IsUhd => Width >= 3840;

    /// <summary>Kurzbezeichnung wie „HEVC 10 Bit 4:2:2“.</summary>
    public string CodecLabel
    {
        get
        {
            var name = Codec switch
            {
                "h264" => "H.264",
                "hevc" => "H.265/HEVC",
                "prores" => "ProRes",
                "mjpeg" => "Motion-JPEG",
                _ => Codec.ToUpperInvariant(),
            };
            return $"{name} {BitDepth} Bit {ChromaSubsampling}";
        }
    }
}

/// <summary>Technische Daten des Tonstroms.</summary>
public sealed record AudioStreamInfo(string Codec, int SampleRate, int Channels, int? BitsPerSample)
{
    public bool IsLinearPcm => Codec.StartsWith("pcm_", StringComparison.Ordinal);

    public string Label => IsLinearPcm
        ? $"LPCM {SampleRate / 1000.0:0.#} kHz{(BitsPerSample is { } b ? $" {b} Bit" : string.Empty)}"
        : $"{Codec.ToUpperInvariant()} {SampleRate / 1000.0:0.#} kHz";
}

/// <summary>Alles, was MovieWorker über eine Mediendatei wissen muss.</summary>
public sealed record MediaInfo(
    string Path,
    string Container,
    TimeSpan Duration,
    long FileSize,
    DateTimeOffset? CreationTime,
    VideoStreamInfo? Video,
    AudioStreamInfo? Audio,
    IReadOnlyDictionary<string, string> Tags)
{
    public string FileName => System.IO.Path.GetFileName(Path);

    public bool HasVideo => Video is not null;

    public bool HasAudio => Audio is not null;

    /// <summary>Kennlinie aus den Metadaten (Canon Log 3 lässt sich daraus allein nicht erkennen – siehe CanonCameraDetector).</summary>
    public ColorTransfer MetadataTransfer => Video?.ColorTransferRaw switch
    {
        "smpte2084" => ColorTransfer.HdrPq,
        "arib-std-b67" => ColorTransfer.HdrHlg,
        _ => ColorTransfer.Rec709,
    };
}
