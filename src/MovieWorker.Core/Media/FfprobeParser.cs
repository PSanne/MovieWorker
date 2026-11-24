using System.Globalization;
using System.Text.Json;
using MovieWorker.Core.Time;

namespace MovieWorker.Core.Media;

/// <summary>
/// Wertet die JSON-Ausgabe von
/// <c>ffprobe -v quiet -print_format json -show_format -show_streams DATEI</c> aus.
/// </summary>
public static class FfprobeParser
{
    /// <summary>Die Argumente, mit denen ffprobe aufgerufen werden muss.</summary>
    public static IReadOnlyList<string> BuildArguments(string path) =>
        ["-v", "quiet", "-print_format", "json", "-show_format", "-show_streams", path];

    public static MediaInfo Parse(string path, string json, long fileSize = 0)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        VideoStreamInfo? video = null;
        AudioStreamInfo? audio = null;
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (root.TryGetProperty("streams", out var streams))
        {
            foreach (var stream in streams.EnumerateArray())
            {
                var type = GetString(stream, "codec_type");
                if (type == "video" && video is null && !IsAttachedPicture(stream))
                {
                    video = ParseVideo(stream);
                }
                else if (type == "audio" && audio is null)
                {
                    audio = ParseAudio(stream);
                }
            }
        }

        var container = string.Empty;
        var duration = TimeSpan.Zero;
        DateTimeOffset? creation = null;
        if (root.TryGetProperty("format", out var format))
        {
            container = GetString(format, "format_name") ?? string.Empty;
            if (double.TryParse(GetString(format, "duration"), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            {
                duration = TimeSpan.FromSeconds(seconds);
            }

            if (fileSize == 0 && long.TryParse(GetString(format, "size"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size))
            {
                fileSize = size;
            }

            if (format.TryGetProperty("tags", out var formatTags))
            {
                foreach (var tag in formatTags.EnumerateObject())
                {
                    tags[tag.Name] = tag.Value.ToString();
                }
            }

            if (tags.TryGetValue("creation_time", out var created)
                && DateTimeOffset.TryParse(created, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                creation = parsed;
            }
        }

        return new MediaInfo(path, container, duration, fileSize, creation, video, audio, tags);
    }

    private static VideoStreamInfo ParseVideo(JsonElement s)
    {
        var pixFmt = GetString(s, "pix_fmt") ?? "unknown";
        var rateText = GetString(s, "avg_frame_rate");
        if (!FrameRate.TryParse(rateText, out var rate))
        {
            FrameRate.TryParse(GetString(s, "r_frame_rate"), out rate);
        }

        if (rate == default)
        {
            rate = FrameRate.Fps25;
        }

        var bitDepth = int.TryParse(GetString(s, "bits_per_raw_sample"), out var bits) && bits > 0
            ? bits
            : BitDepthFromPixelFormat(pixFmt);

        return new VideoStreamInfo(
            Codec: GetString(s, "codec_name") ?? "unknown",
            Profile: GetString(s, "profile"),
            Width: GetInt(s, "width"),
            Height: GetInt(s, "height"),
            FrameRate: rate,
            PixelFormat: pixFmt,
            BitDepth: bitDepth,
            ChromaSubsampling: ChromaFromPixelFormat(pixFmt),
            BitRate: long.TryParse(GetString(s, "bit_rate"), out var br) ? br : null,
            ColorTransferRaw: GetString(s, "color_transfer"),
            ColorPrimariesRaw: GetString(s, "color_primaries"));
    }

    private static AudioStreamInfo ParseAudio(JsonElement s)
    {
        int? bits = int.TryParse(GetString(s, "bits_per_sample"), out var b) && b > 0 ? b
            : int.TryParse(GetString(s, "bits_per_raw_sample"), out var r) && r > 0 ? r
            : null;
        return new AudioStreamInfo(
            GetString(s, "codec_name") ?? "unknown",
            int.TryParse(GetString(s, "sample_rate"), out var rate) ? rate : 48000,
            GetInt(s, "channels"),
            bits);
    }

    internal static int BitDepthFromPixelFormat(string pixFmt) =>
        pixFmt.Contains("p10", StringComparison.Ordinal) ? 10
        : pixFmt.Contains("p12", StringComparison.Ordinal) ? 12
        : 8;

    internal static string ChromaFromPixelFormat(string pixFmt) =>
        pixFmt.StartsWith("yuv444", StringComparison.Ordinal) || pixFmt.StartsWith("yuvj444", StringComparison.Ordinal) ? "4:4:4"
        : pixFmt.StartsWith("yuv422", StringComparison.Ordinal) || pixFmt.StartsWith("yuvj422", StringComparison.Ordinal) ? "4:2:2"
        : pixFmt.StartsWith("yuv420", StringComparison.Ordinal) || pixFmt.StartsWith("yuvj420", StringComparison.Ordinal) ? "4:2:0"
        : "?";

    private static bool IsAttachedPicture(JsonElement stream) =>
        stream.TryGetProperty("disposition", out var d)
        && d.TryGetProperty("attached_pic", out var pic)
        && pic.ValueKind == JsonValueKind.Number
        && pic.GetInt32() == 1;

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null,
            }
            : null;

    private static int GetInt(JsonElement e, string name) =>
        int.TryParse(GetString(e, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
