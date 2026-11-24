using System.Security.Cryptography;
using System.Text;
using MovieWorker.Core.Export;
using MovieWorker.Core.Media;
using MovieWorker.Core.Projects;

namespace MovieWorker.Core.Preview;

public enum ProxyKind
{
    /// <summary>Die Aufnahme lässt sich direkt flüssig abspielen.</summary>
    None,

    /// <summary>Nur neu verpacken (Bild unverändert, Ton als AAC) – dauert Sekunden.</summary>
    Remux,

    /// <summary>Neu kodieren als 540p-H.264 – für HEVC, 10 Bit, 4:2:2, HDR oder Formate ohne Windows-Decoder.</summary>
    Transcode,
}

/// <summary>
/// Entscheidet, ob und wie für eine Aufnahme ein Proxy (Vorschaudatei) erstellt wird.
/// Windows spielt MP4 mit H.264 (8 Bit, 4:2:0) und AAC zuverlässig ab; alles andere bekommt einen Proxy.
/// </summary>
public static class ProxyPlanner
{
    public static ProxyKind Decide(MediaItem media)
    {
        var first = media.First;
        if (first.Video is not { } video)
        {
            // Reiner Ton: WAV/MP3/AAC spielt Windows direkt, andere (z. B. FLAC in alten Windows-Versionen) werden umgewandelt.
            var ext = Path.GetExtension(first.Path).ToLowerInvariant();
            return ext is ".wav" or ".mp3" or ".m4a" or ".aac" or ".wma" && media.Parts.Count == 1 ? ProxyKind.None : ProxyKind.Remux;
        }

        var h264Simple = video.Codec == "h264" && video.BitDepth == 8 && video.ChromaSubsampling == "4:2:0"
            && media.First.MetadataTransfer == ColorTransfer.Rec709 && video.Height <= 2160;
        if (!h264Simple)
        {
            return ProxyKind.Transcode;
        }

        var isMp4 = Path.GetExtension(first.Path).Equals(".mp4", StringComparison.OrdinalIgnoreCase);
        var aacOrNone = first.Audio is null || first.Audio.Codec == "aac";
        return isMp4 && aacOrNone && media.Parts.Count == 1 ? ProxyKind.None : ProxyKind.Remux;
    }

    /// <summary>Eindeutiger, stabiler Dateiname für den Proxy einer Aufnahme.</summary>
    public static string ProxyPath(MediaItem media, string proxyFolder)
    {
        var key = string.Join('|', media.Parts.Select(p => $"{p.Path}|{p.FileSize}"));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..10];
        var name = Path.GetFileNameWithoutExtension(media.PrimaryPath);
        return Path.Combine(proxyFolder, $"{name}_{hash}.mp4");
    }

    /// <summary>
    /// ffmpeg-Argumente für den Proxy. Geteilte Aufnahmen werden dabei zu einer Datei zusammengefügt;
    /// HDR wird für die Vorschau nach SDR umgerechnet, Log-Material optional mit einer LUT versehen.
    /// </summary>
    public static IReadOnlyList<string> Arguments(MediaItem media, ProxyKind kind, string output, string? viewingLut = null)
    {
        var args = new List<string> { "-y", "-v", "error", "-nostdin" };
        foreach (var part in media.Parts)
        {
            args.AddRange(["-i", part.Path]);
        }

        var hasVideo = media.First.HasVideo;
        var hasAudio = media.First.HasAudio;
        var n = media.Parts.Count;
        var filters = new List<string>();
        string? videoLabel = null;
        string? audioLabel = null;

        if (n > 1)
        {
            var inputs = string.Concat(Enumerable.Range(0, n).Select(i => (hasVideo ? $"[{i}:v:0]" : string.Empty) + (hasAudio ? $"[{i}:a:0]" : string.Empty)));
            filters.Add($"{inputs}concat=n={n}:v={(hasVideo ? 1 : 0)}:a={(hasAudio ? 1 : 0)}{(hasVideo ? "[cv]" : string.Empty)}{(hasAudio ? "[ca]" : string.Empty)}");
            videoLabel = hasVideo ? "[cv]" : null;
            audioLabel = hasAudio ? "[ca]" : null;
        }

        if (kind == ProxyKind.Transcode && hasVideo)
        {
            var vf = new List<string>();
            var transfer = media.Detection.Transfer;
            if (transfer is ColorTransfer.HdrPq or ColorTransfer.HdrHlg)
            {
                vf.Add(FfmpegArgs.HdrToSdrFilter);
            }
            else if (transfer == ColorTransfer.CanonLog3 && !string.IsNullOrEmpty(viewingLut))
            {
                vf.Add($"lut3d=file={FfmpegArgs.EscapeFilterPath(viewingLut)}");
            }

            vf.Add("scale=-2:540");
            vf.Add("format=yuv420p");
            filters.Add($"{videoLabel ?? "[0:v:0]"}{string.Join(',', vf)}[pv]");
            videoLabel = "[pv]";
        }

        if (filters.Count > 0)
        {
            args.AddRange(["-filter_complex", string.Join(';', filters)]);
        }

        if (hasVideo)
        {
            args.AddRange(["-map", videoLabel ?? "0:v:0"]);
            args.AddRange(kind == ProxyKind.Transcode
                ? ["-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-g", "12", "-pix_fmt", "yuv420p"]
                : n > 1 ? ["-c:v", "libx264", "-preset", "veryfast", "-crf", "18", "-pix_fmt", "yuv420p"] : ["-c:v", "copy"]);
        }

        if (hasAudio)
        {
            args.AddRange(["-map", audioLabel ?? "0:a:0", "-c:a", "aac", "-b:a", "192k", "-ac", "2"]);
        }

        args.AddRange(["-movflags", "+faststart", "-progress", "pipe:1", "-nostats", output]);
        return args;
    }
}
