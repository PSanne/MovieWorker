using System.Globalization;
using MovieWorker.Core.Timeline;

namespace MovieWorker.Core.Export;

/// <summary>Hilfsfunktionen für ffmpeg-Argumente und Filter.</summary>
public static class FfmpegArgs
{
    public static string Seconds(double value) =>
        Math.Max(0, value).ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>
    /// Pfade in Filtern (z. B. <c>lut3d=file=…</c>) müssen besonders maskiert werden:
    /// Windows-Laufwerksdoppelpunkt und Apostrophe würden sonst als Filter-Syntax gelesen.
    /// </summary>
    public static string EscapeFilterPath(string path)
    {
        var p = path.Replace('\\', '/')
            .Replace(":", "\\:", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);
        return $"'{p}'";
    }

    /// <summary>Tone-Mapping von HDR (PQ/HLG) nach SDR Rec.709.</summary>
    public const string HdrToSdrFilter =
        "zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=hable:desat=0,zscale=t=bt709:m=bt709:r=tv";

    public static string ToneMapFilter(Color.ToneMapping mapping) =>
        HdrToSdrFilter.Replace("tonemap=hable", "tonemap=" + mapping.ToString().ToLowerInvariant(), StringComparison.Ordinal);

    /// <summary>SDR (Rec.709) → HDR10 (PQ, BT.2020), SDR-Weiß bei 203 Nits.</summary>
    public const string SdrToPqFilter =
        "setparams=color_primaries=bt709:color_trc=bt709:colorspace=bt709,zscale=t=linear:npl=203,format=gbrpf32le,zscale=p=bt2020:t=smpte2084:m=bt2020nc:r=tv";

    /// <summary>
    /// Farbkette eines Clips: Eingangs-Umrechnung (LUT oder Tone-Mapping) und Farbkorrektur (als berechnete LUT).
    /// Bei HDR-Ausgabe bleibt HDR-Material in PQ und SDR-Material wird nach PQ umgerechnet.
    /// </summary>
    public static string? ColorFilter(TimelineClip clip, bool hdrOutput = false)
    {
        var filters = new List<string>();
        switch (clip.ColorTransform)
        {
            case ColorTransform.Lut when !string.IsNullOrWhiteSpace(clip.LutPath):
                filters.Add($"lut3d=file={EscapeFilterPath(clip.LutPath!)}:interp=tetrahedral");
                break;
            case ColorTransform.HdrToSdr when !hdrOutput:
                filters.Add(ToneMapFilter(clip.Grade.ToneMap));
                break;
        }

        if (!clip.Grade.IsNeutral)
        {
            filters.Add($"lut3d=file={EscapeFilterPath(Color.GradeLutCache.Ensure(clip.Grade))}:interp=tetrahedral");
        }

        if (hdrOutput && clip.ColorTransform != ColorTransform.HdrToSdr)
        {
            filters.Add(SdrToPqFilter);
        }

        return filters.Count == 0 ? null : string.Join(',', filters);
    }

    /// <summary>
    /// Liest den Fortschritt aus <c>-progress pipe:1</c> (Zeilen wie „out_time_us=1234567“).
    /// </summary>
    public static bool TryParseProgress(string? line, out double seconds)
    {
        seconds = 0;
        if (line is null)
        {
            return false;
        }

        foreach (var key in new[] { "out_time_us=", "out_time_ms=" })
        {
            if (line.StartsWith(key, StringComparison.Ordinal)
                && long.TryParse(line.AsSpan(key.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var micro))
            {
                // Hinweis: „out_time_ms“ enthält bei ffmpeg trotz des Namens ebenfalls Mikrosekunden.
                seconds = micro / 1_000_000.0;
                return true;
            }
        }

        return false;
    }

    /// <summary>Ein einzelnes Vorschaubild als PNG auf die Standardausgabe.</summary>
    public static IReadOnlyList<string> Thumbnail(string path, double seconds, int height, string? colorFilter = null)
    {
        var vf = colorFilter is null ? $"scale=-2:{height}" : $"{colorFilter},scale=-2:{height}";
        return
        [
            "-v", "error", "-ss", Seconds(seconds), "-i", path, "-frames:v", "1",
            "-vf", vf, "-f", "image2pipe", "-vcodec", "png", "-",
        ];
    }

    /// <summary>Einzelbild in voller Auflösung als PNG-Datei (mit Farbumrechnung des Clips).</summary>
    public static IReadOnlyList<string> StillFrame(string path, double seconds, string? colorFilter, string output)
    {
        var args = new List<string> { "-y", "-v", "error", "-ss", Seconds(seconds), "-i", path, "-frames:v", "1" };
        if (colorFilter is not null)
        {
            args.AddRange(["-vf", colorFilter]);
        }

        args.Add(output);
        return args;
    }

    /// <summary>
    /// Proxy für flüssige Vorschau: 540p H.264 mit kurzen GOPs (schnelles Springen), Ton als AAC.
    /// </summary>
    public static IReadOnlyList<string> Proxy(string input, string output) =>
    [
        "-y", "-v", "error", "-i", input,
        "-vf", "scale=-2:540", "-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-g", "12",
        "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "160k",
        "-progress", "pipe:1", "-nostats", output,
    ];
}
