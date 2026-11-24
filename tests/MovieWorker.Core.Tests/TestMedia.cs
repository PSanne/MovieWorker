using MovieWorker.Core.Media;

namespace MovieWorker.Core.Tests;

/// <summary>ffprobe-Ausgaben, wie sie typische Clips der beiden Kameras liefern.</summary>
internal static class TestMedia
{
    public const string Eos70DJson = """
    {
      "streams": [
        { "codec_type": "video", "codec_name": "h264", "profile": "Main", "width": 1920, "height": 1080,
          "pix_fmt": "yuvj420p", "avg_frame_rate": "25/1", "r_frame_rate": "25/1", "bit_rate": "45000000" },
        { "codec_type": "audio", "codec_name": "pcm_s16le", "sample_rate": "48000", "channels": 2, "bits_per_sample": 16 }
      ],
      "format": { "format_name": "mov,mp4,m4a,3gp,3g2,mj2", "duration": "120.000000", "size": "675000000",
                  "tags": { "creation_time": "2024-05-01T10:00:00.000000Z" } }
    }
    """;

    public const string R6ClogJson = """
    {
      "streams": [
        { "codec_type": "video", "codec_name": "hevc", "profile": "Rext", "width": 3840, "height": 2160,
          "pix_fmt": "yuv422p10le", "avg_frame_rate": "24000/1001", "color_transfer": "bt709", "color_primaries": "bt709" },
        { "codec_type": "audio", "codec_name": "aac", "sample_rate": "48000", "channels": 2 }
      ],
      "format": { "format_name": "mov,mp4,m4a,3gp,3g2,mj2", "duration": "30.030000", "size": "500000000" }
    }
    """;

    public const string R6PqJson = """
    {
      "streams": [
        { "codec_type": "video", "codec_name": "hevc", "width": 3840, "height": 2160,
          "pix_fmt": "yuv422p10le", "avg_frame_rate": "25/1", "color_transfer": "smpte2084", "color_primaries": "bt2020" }
      ],
      "format": { "format_name": "mov,mp4,m4a,3gp,3g2,mj2", "duration": "10.0" }
    }
    """;

    public const string R6HfrJson = """
    {
      "streams": [
        { "codec_type": "video", "codec_name": "h264", "width": 1920, "height": 1080,
          "pix_fmt": "yuv420p", "avg_frame_rate": "180000/1001" }
      ],
      "format": { "format_name": "mov,mp4,m4a,3gp,3g2,mj2", "duration": "8.0" }
    }
    """;

    public static MediaInfo Eos70D(string path = "/card/DCIM/100CANON/MVI_0001.MOV", long size = 0) =>
        FfprobeParser.Parse(path, Eos70DJson, size);

    public static MediaInfo R6Clog(string path = "/card/DCIM/100EOSR6/MVI_0042.MP4") => FfprobeParser.Parse(path, R6ClogJson);
}
