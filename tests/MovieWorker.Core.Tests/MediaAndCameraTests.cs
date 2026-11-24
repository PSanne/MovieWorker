using MovieWorker.Core.Camera;
using MovieWorker.Core.Media;

namespace MovieWorker.Core.Tests;

public class MediaAndCameraTests
{
    [Fact]
    public void Parser_ReadsEos70DClip()
    {
        var media = TestMedia.Eos70D();
        Assert.Equal(TimeSpan.FromSeconds(120), media.Duration);
        Assert.Equal(675000000, media.FileSize);
        Assert.NotNull(media.CreationTime);
        var video = Assert.IsType<VideoStreamInfo>(media.Video);
        Assert.Equal("h264", video.Codec);
        Assert.Equal(8, video.BitDepth);
        Assert.Equal("4:2:0", video.ChromaSubsampling);
        Assert.Equal(25, video.FrameRate.Value);
        var audio = Assert.IsType<AudioStreamInfo>(media.Audio);
        Assert.True(audio.IsLinearPcm);
        Assert.Equal(16, audio.BitsPerSample);
    }

    [Fact]
    public void Parser_ReadsR6HevcClip()
    {
        var media = TestMedia.R6Clog();
        Assert.Equal(10, media.Video!.BitDepth);
        Assert.Equal("4:2:2", media.Video.ChromaSubsampling);
        Assert.Equal("H.265/HEVC 10 Bit 4:2:2", media.Video.CodecLabel);
        Assert.True(media.Video.IsUhd);
    }

    [Fact]
    public void Parser_SkipsCoverArt()
    {
        const string json = """
        { "streams": [
            { "codec_type": "video", "codec_name": "mjpeg", "width": 300, "height": 300, "pix_fmt": "yuvj420p", "disposition": { "attached_pic": 1 } },
            { "codec_type": "audio", "codec_name": "mp3", "sample_rate": "44100", "channels": 2 } ],
          "format": { "duration": "200" } }
        """;
        var media = FfprobeParser.Parse("song.mp3", json);
        Assert.False(media.HasVideo);
        Assert.True(media.HasAudio);
    }

    [Fact]
    public void Detector_Recognizes70D()
    {
        var result = CanonCameraDetector.Detect(TestMedia.Eos70D());
        Assert.Equal(CanonCamera.Eos70D, result.Camera);
        Assert.Equal(DetectionConfidence.Likely, result.Confidence);
        Assert.Equal(ColorTransfer.Rec709, result.Transfer);
        Assert.False(result.IsHighFrameRate);
    }

    [Fact]
    public void Detector_RecognizesR6CanonLog3()
    {
        var result = CanonCameraDetector.Detect(TestMedia.R6Clog());
        Assert.Equal(CanonCamera.EosR6MarkII, result.Camera);
        Assert.Equal(ColorTransfer.CanonLog3, result.Transfer);
        Assert.Contains(result.Hints, h => h.Contains("LUT"));
        Assert.Contains(result.Hints, h => h.Contains("Proxy"));
    }

    [Fact]
    public void Detector_RecognizesR6HdrPq()
    {
        var media = FfprobeParser.Parse("MVI_0100.MP4", TestMedia.R6PqJson);
        var result = CanonCameraDetector.Detect(media);
        Assert.Equal(CanonCamera.EosR6MarkII, result.Camera);
        Assert.Equal(ColorTransfer.HdrPq, result.Transfer);
    }

    [Fact]
    public void Detector_RecognizesHighFrameRate()
    {
        var media = FfprobeParser.Parse("MVI_0200.MP4", TestMedia.R6HfrJson);
        var result = CanonCameraDetector.Detect(media);
        Assert.Equal(CanonCamera.EosR6MarkII, result.Camera);
        Assert.True(result.IsHighFrameRate);
    }

    [Fact]
    public void Detector_UsesModelTagWhenPresent()
    {
        var media = TestMedia.Eos70D("clip.mov") with
        {
            Tags = new Dictionary<string, string> { ["com.apple.quicktime.model"] = "Canon EOS R6 Mark II" },
        };
        var result = CanonCameraDetector.Detect(media);
        Assert.Equal(CanonCamera.EosR6MarkII, result.Camera);
        Assert.Equal(DetectionConfidence.Certain, result.Confidence);
    }

    [Fact]
    public void Detector_UnknownForForeignFootage()
    {
        var media = FfprobeParser.Parse("phone.mp4", """
            { "streams": [ { "codec_type": "video", "codec_name": "h264", "width": 1920, "height": 1080, "pix_fmt": "yuv420p", "avg_frame_rate": "30/1" } ],
              "format": { "duration": "5" } }
            """);
        Assert.Equal(CanonCamera.Unknown, CanonCameraDetector.Detect(media).Camera);
    }

    [Fact]
    public void SplitDetector_JoinsFilesAt4GBLimit()
    {
        var files = new[]
        {
            TestMedia.Eos70D("/card/DCIM/100CANON/MVI_0010.MOV", 4_294_000_000),
            TestMedia.Eos70D("/card/DCIM/100CANON/MVI_0011.MOV", 4_294_000_000),
            TestMedia.Eos70D("/card/DCIM/100CANON/MVI_0012.MOV", 1_000_000_000),
            TestMedia.Eos70D("/card/DCIM/100CANON/MVI_0013.MOV", 500_000_000),
        };

        var groups = SplitRecordingDetector.Group(files.Reverse());
        Assert.Equal(2, groups.Count);
        Assert.Equal(3, groups[0].Parts.Count);
        Assert.Equal("MVI_0010.MOV (+2 Teile)", groups[0].DisplayName);
        Assert.Equal(TimeSpan.FromSeconds(360), groups[0].TotalDuration);
        Assert.False(groups[1].IsSplit);
    }

    [Fact]
    public void SplitDetector_DoesNotJoinGaps()
    {
        var a = TestMedia.Eos70D("/card/DCIM/100CANON/MVI_0010.MOV", 4_294_000_000);
        var b = TestMedia.Eos70D("/card/DCIM/100CANON/MVI_0012.MOV", 100);
        Assert.False(SplitRecordingDetector.IsContinuation(a, b));
    }

    [Fact]
    public void CardScanner_FiltersThumbnailsAndHiddenFiles()
    {
        var files = CardScanner.FilterVideos([
            "DCIM/100CANON/MVI_0002.MOV", "DCIM/100CANON/MVI_0002.THM", "DCIM/100CANON/._MVI_0002.MOV",
            "DCIM/100CANON/IMG_0003.JPG", "DCIM/100CANON/MVI_0001.MOV", "DCIM/CANONMSC/M0100.CTG",
        ]);
        Assert.Equal(["DCIM/100CANON/MVI_0001.MOV", "DCIM/100CANON/MVI_0002.MOV"], files);
    }

    [Fact]
    public void Profiles_CoverBothCameras()
    {
        Assert.True(CanonCameraProfile.For(CanonCamera.EosR6MarkII)!.SupportsCanonLog3);
        Assert.True(CanonCameraProfile.For(CanonCamera.Eos70D)!.SplitsAt4GB);
        Assert.Null(CanonCameraProfile.For(CanonCamera.Unknown));
    }
}
