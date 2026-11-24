using MovieWorker.Core.Media;

namespace MovieWorker.Core.Export;

/// <summary>Hersteller der Grafikkarten-Kodierung.</summary>
public enum HardwareVendor
{
    Nvidia,
    Intel,
    Amd,
}

/// <summary>Ein nutzbarer Hardware-Encoder (H.264 bzw. H.265 auf der Grafikkarte).</summary>
public sealed record HardwareEncoder(HardwareVendor Vendor, bool Hevc)
{
    public string Codec => (Hevc ? "hevc_" : "h264_") + Vendor switch
    {
        HardwareVendor.Nvidia => "nvenc",
        HardwareVendor.Intel => "qsv",
        _ => "amf",
    };

    public string VendorName => Vendor switch
    {
        HardwareVendor.Nvidia => "NVIDIA NVENC",
        HardwareVendor.Intel => "Intel Quick Sync",
        _ => "AMD AMF",
    };

    public override string ToString() => $"{VendorName} ({(Hevc ? "H.265" : "H.264")})";

    /// <summary>Kodier-Argumente in hoher Qualität (vergleichbar mit CRF 18–20 der Software-Encoder).</summary>
    public IReadOnlyList<string> Arguments(bool tenBit) => Vendor switch
    {
        HardwareVendor.Nvidia => ["-c:v", Codec, "-preset", "p6", "-tune", "hq", "-rc", "vbr", "-cq", Hevc ? "21" : "19", "-b:v", "0",
            "-profile:v", Hevc ? (tenBit ? "main10" : "main") : "high", "-spatial-aq", "1"],
        HardwareVendor.Intel => ["-c:v", Codec, "-preset", "slow", "-global_quality", Hevc ? "22" : "20",
            "-profile:v", Hevc ? (tenBit ? "main10" : "main") : "high"],
        _ => ["-c:v", Codec, "-quality", "quality", "-rc", "cqp", "-qp_i", Hevc ? "20" : "18", "-qp_p", Hevc ? "22" : "20",
            "-profile:v", Hevc ? "main" : "high"],
    };
}

/// <summary>
/// Erkennt nutzbare Grafikkarten-Encoder und stellt Export-Vorlagen darauf um.
/// FFmpeg listet Encoder auch ohne passende Grafikkarte – deshalb wird jeder mit einer winzigen Probe-Kodierung geprüft.
/// </summary>
public static class HardwareEncoders
{
    private static readonly HardwareVendor[] Order = [HardwareVendor.Nvidia, HardwareVendor.Intel, HardwareVendor.Amd];

    /// <summary>Kandidaten aus der Ausgabe von <c>ffmpeg -encoders</c>.</summary>
    public static IReadOnlyList<HardwareEncoder> ParseListed(string encodersOutput)
    {
        var listed = new List<HardwareEncoder>();
        foreach (var vendor in Order)
        {
            foreach (var hevc in new[] { false, true })
            {
                var encoder = new HardwareEncoder(vendor, hevc);
                if (encodersOutput.Split('\n').Any(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) == encoder.Codec))
                {
                    listed.Add(encoder);
                }
            }
        }

        return listed;
    }

    /// <summary>ffmpeg-Aufruf für die Probe-Kodierung (ein paar Bilder ins Nichts).</summary>
    public static IReadOnlyList<string> ProbeArguments(HardwareEncoder encoder) =>
    [
        "-hide_banner", "-v", "error", "-f", "lavfi", "-i", "testsrc2=s=640x360:r=25:d=0.2",
        .. encoder.Arguments(tenBit: false), "-pix_fmt", "yuv420p", "-f", "null", "-",
    ];

    /// <summary>Prüft alle gelisteten Encoder mit einer Probe-Kodierung; liefert die nutzbaren.</summary>
    public static async Task<IReadOnlyList<HardwareEncoder>> DetectAsync(FfmpegClient client, CancellationToken token = default)
    {
        var listed = ParseListed(await client.ListEncodersAsync(token).ConfigureAwait(false));
        var usable = new List<HardwareEncoder>();
        foreach (var encoder in listed)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            try
            {
                await client.RunAsync(ProbeArguments(encoder), 0, null, timeout.Token).ConfigureAwait(false);
                usable.Add(encoder);
            }
            catch (Exception ex) when (ex is FfmpegException or OperationCanceledException)
            {
                // Encoder vorhanden, aber keine passende Grafikkarte bzw. kein Treiber.
            }
        }

        return usable;
    }

    /// <summary>Kann diese Vorlage auf die Grafikkarte umgestellt werden? (H.264/H.265 ohne HDR-Metadaten)</summary>
    public static bool Supports(ExportPreset preset) =>
        !preset.AudioOnly && !preset.IsStillImage && !preset.HdrOutput && !preset.UsesHardware
        && (preset.VideoArguments.Contains("libx264") || preset.VideoArguments.Contains("libx265"));

    /// <summary>
    /// Vorlage mit Grafikkarten-Encoder (gleicher Codec wie vorher: H.264 bleibt H.264, H.265 bleibt H.265).
    /// Liefert die Vorlage unverändert, wenn es keinen passenden Encoder gibt.
    /// </summary>
    public static ExportPreset Apply(ExportPreset preset, IReadOnlyList<HardwareEncoder> available)
    {
        if (!Supports(preset))
        {
            return preset;
        }

        var hevc = preset.VideoArguments.Contains("libx265");
        if (available.FirstOrDefault(e => e.Hevc == hevc) is not { } encoder)
        {
            return preset;
        }

        var tenBit = preset.PixelFormat.Contains("10", StringComparison.Ordinal);
        var args = new List<string>(encoder.Arguments(tenBit));

        // Container-Einstellungen und Bitratengrenzen der Vorlage übernehmen.
        var source = preset.VideoArguments;
        for (var i = 0; i < source.Count - 1; i++)
        {
            if (source[i] is "-movflags" or "-tag:v" or "-maxrate" or "-bufsize")
            {
                args.Add(source[i]);
                args.Add(source[i + 1]);
            }
        }

        return preset with
        {
            Id = preset.Id + "-gpu",
            Name = preset.Name + " · " + encoder.VendorName,
            VideoArguments = args,
            PixelFormat = tenBit ? "p010le" : preset.PixelFormat,
            HardwareEncoder = encoder,
        };
    }
}
