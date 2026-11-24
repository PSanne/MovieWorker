using MovieWorker.Core.Camera;
using MovieWorker.Core.Export;
using MovieWorker.Core.Time;
using MovieWorker.Core.Timeline;

namespace MovieWorker.Core.Tests;

public class ExportTests
{
    private static TimelineEditor Editor()
    {
        var editor = new TimelineEditor(MovieProject.CreateDefault(new ProjectSettings { Width = 3840, Height = 2160, FrameRate = FrameRate.Fps25 }));
        return editor;
    }

    [Fact]
    public void EmptyTimeline_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => ExportBuilder.Build(Editor().Project, ExportPreset.Default, "out.mp4"));
    }

    [Fact]
    public void Build_ContainsClipsGapsAndMusic()
    {
        var editor = Editor();
        var v1 = editor.Project.VideoTracks.First();
        editor.Append(v1, editor.CreateClip(new RecordingGroup([TestMedia.Eos70D()]), 5, 15));
        var second = editor.CreateClip(new RecordingGroup([TestMedia.R6Clog()]));
        second.ColorTransform = ColorTransform.Lut;
        second.LutPath = @"C:\LUTs\Canon Log3.cube";
        editor.Insert(v1, second, 500);
        var music = editor.CreateClip(new RecordingGroup([TestMedia.Eos70D("/music/song.mov")]), 0, 30);
        music.VolumeDb = -6;
        editor.Insert(editor.Project.AudioTracks.First(), music, 0);

        var job = ExportBuilder.Build(editor.Project, ExportPreset.All.First(p => p.Id == "prores-hq"), "out.mov");
        var args = string.Join(" ", job.Arguments);
        var graph = job.Arguments[job.Arguments.ToList().IndexOf("-filter_complex") + 1];

        Assert.Contains("-ss 5 -t 10 -i /card/DCIM/100CANON/MVI_0001.MOV", args);
        Assert.Contains("color=c=black:s=3840x2160", graph);                 // Lücke 250..500
        Assert.Contains("lut3d=file='C\\:/LUTs/Canon Log3.cube'", graph);    // maskierter Windows-Pfad
        Assert.Contains("concat=n=3:v=1:a=1[vout][acam]", graph);
        Assert.Contains("volume=-6dB", graph);
        Assert.Contains("amix=inputs=2", graph);
        Assert.Contains("prores_ks", args);
        Assert.Equal((500 + 751) / 25.0, job.DurationSeconds, 6); // 30,03 s = 751 Bilder
    }

    [Fact]
    public void Build_SplitRecordingUsesOnlyTouchedParts()
    {
        var editor = Editor();
        var parts = new[]
        {
            TestMedia.Eos70D("/card/DCIM/100CANON/MVI_0010.MOV", 4_294_000_000),
            TestMedia.Eos70D("/card/DCIM/100CANON/MVI_0011.MOV", 4_294_000_000),
            TestMedia.Eos70D("/card/DCIM/100CANON/MVI_0012.MOV", 1000),
        };
        var group = SplitRecordingDetector.Group(parts).Single();
        editor.Append(editor.Project.VideoTracks.First(), editor.CreateClip(group, 100, 150)); // überspannt Teil 1→2

        var args = ExportBuilder.Build(editor.Project, ExportPreset.Default, "o.mp4").Arguments;
        var joined = string.Join(" ", args);
        Assert.Contains("-ss 100 -t 20 -i /card/DCIM/100CANON/MVI_0010.MOV", joined);
        Assert.Contains("-ss 0 -t 30 -i /card/DCIM/100CANON/MVI_0011.MOV", joined);
        Assert.DoesNotContain("MVI_0012", joined);
        Assert.Contains("concat=n=2:v=1:a=0", joined);
    }

    [Fact]
    public void SlowMotionClip_HasSilentCameraAudio()
    {
        var editor = Editor();
        editor.Append(editor.Project.VideoTracks.First(), editor.CreateClip(new RecordingGroup([TestMedia.Eos70D()]), 0, 2));
        editor.ConformToProjectRate(editor.Project.AllClips.Single().Id, 100);
        var graph = string.Join(" ", ExportBuilder.Build(editor.Project, ExportPreset.Default, "o.mp4").Arguments);
        Assert.Contains("setpts=(PTS-STARTPTS)/0.25", graph);
        Assert.Contains("anullsrc", graph);
    }

    [Fact]
    public void OutputSize_ScalesDownForFullHdPreset()
    {
        var settings = new ProjectSettings { Width = 3840, Height = 2160 };
        Assert.Equal((1920, 1080), ExportBuilder.OutputSize(settings, ExportPreset.All.First(p => p.MaxHeight == 1080)));
        Assert.Equal((3840, 2160), ExportBuilder.OutputSize(settings, ExportPreset.Default));
    }

    [Theory]
    [InlineData("out_time_us=2500000", 2.5)]
    [InlineData("out_time_ms=1000000", 1.0)]
    public void Progress_IsParsed(string line, double expected)
    {
        Assert.True(FfmpegArgs.TryParseProgress(line, out var seconds));
        Assert.Equal(expected, seconds, 6);
    }

    [Fact]
    public void Progress_IgnoresOtherLines() => Assert.False(FfmpegArgs.TryParseProgress("frame=12", out _));
}
