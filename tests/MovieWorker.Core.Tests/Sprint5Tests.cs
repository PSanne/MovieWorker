using MovieWorker.Core.Camera;
using MovieWorker.Core.Export;
using MovieWorker.Core.Media;
using MovieWorker.Core.Projects;
using MovieWorker.Core.Time;
using MovieWorker.Core.Timeline;

namespace MovieWorker.Core.Tests;

public sealed class Sprint5Tests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "mw-s5-" + Guid.NewGuid().ToString("N"));

    public Sprint5Tests() => Directory.CreateDirectory(_temp);

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    private static TimelineEditor Editor(int width = 3840, int height = 2160)
    {
        var editor = new TimelineEditor(MovieProject.CreateDefault(new ProjectSettings { Width = width, Height = height, FrameRate = FrameRate.Fps25 }));
        return editor;
    }

    [Fact]
    public void Range_TrimsClipsAndShiftsToZero()
    {
        var editor = Editor();
        var v1 = editor.Project.VideoTracks.First();
        editor.Append(v1, editor.CreateClip(new RecordingGroup([TestMedia.Eos70D()]), 0, 4)); // 0–100
        editor.Append(v1, editor.CreateClip(new RecordingGroup([TestMedia.Eos70D()]), 10, 14)); // 100–200
        editor.UpdateClip(editor.Project.VideoTracks.First().OrderedClips.First().Id, "Blende", c => c.FadeOutFrames = 20);

        var cut = ExportRange.Extract(editor.Project, 50, 150);
        var clips = cut.VideoTracks.First().OrderedClips.ToList();
        Assert.Equal(2, clips.Count);
        Assert.Equal(0, clips[0].StartFrame);
        Assert.Equal(50, clips[0].LengthFrames);
        Assert.Equal(2.0, clips[0].SourceIn, 6);
        Assert.Equal(20, clips[0].FadeOutFrames);
        Assert.Equal(50, clips[1].StartFrame);
        Assert.Equal(10.0, clips[1].SourceIn, 6);
        Assert.Equal(100, cut.DurationFrames);
        Assert.Equal(2, editor.Project.VideoTracks.First().Clips.Count); // Original unverändert
    }

    [Fact]
    public void Build_RangeVerticalAndFrameRateCap()
    {
        var editor = Editor();
        editor.Append(editor.Project.VideoTracks.First(), editor.CreateClip(new RecordingGroup([TestMedia.R6Clog()])));
        var vertical = ExportPreset.Find("social-vertical")!;
        var job = ExportBuilder.Build(editor.Project, vertical, "o.mp4", 25, 75);
        var graph = job.Arguments[job.Arguments.ToList().IndexOf("-filter_complex") + 1];
        Assert.Equal(2.0, job.DurationSeconds, 6);
        Assert.Contains("-ss 1 -t 2 -i", string.Join(' ', job.Arguments));
        Assert.Contains("boxblur", graph);
        Assert.Contains("overlay=(W-w)/2:(H-h)/2", graph);
        Assert.Contains("scale=3414:1920", graph); // Arbeitsgröße: nur so groß wie nötig
        Assert.Equal((1080, 1920), ExportBuilder.OutputSize(editor.Project.Settings, vertical));

        var fill = ExportBuilder.Build(editor.Project, vertical.WithReframe(ReframeMode.Fill), "o.mp4");
        Assert.Contains("crop=1080:1920", string.Join(' ', fill.Arguments));

        Assert.Equal(new FrameRate(30000, 1001), ExportBuilder.OutputFrameRate(new FrameRate(120000, 1001), ExportPreset.Find("social-vertical")! with { MaxFrameRate = 30 }));
        Assert.Equal(FrameRate.Fps50, ExportBuilder.OutputFrameRate(FrameRate.Fps50, vertical));
    }

    [Fact]
    public void Build_AudioOnlyMapsOnlyAudio()
    {
        var editor = Editor(1920, 1080);
        editor.Append(editor.Project.VideoTracks.First(), editor.CreateClip(new RecordingGroup([TestMedia.Eos70D()]), 0, 2));
        var args = ExportBuilder.Build(editor.Project, ExportPreset.Find("audio-wav")!, "o.wav").Arguments;
        var joined = string.Join(' ', args);
        Assert.Contains("[vout]nullsink", joined);
        Assert.DoesNotContain("-map [vout]", joined);
        Assert.Contains("pcm_s24le", joined);
    }

    [Fact]
    public void Preflight_FindsTypicalProblems()
    {
        var editor = Editor();
        var log = MediaItem.FromRecording(new RecordingGroup([TestMedia.R6Clog()]));
        var hfr = MediaItem.FromRecording(new RecordingGroup([FfprobeParser.Parse("/c/MVI_0200.MP4", TestMedia.R6HfrJson)]));
        var old = MediaItem.FromRecording(new RecordingGroup([TestMedia.Eos70D()]));
        editor.AddMedia([log, hfr, old]);
        var v1 = editor.Project.VideoTracks.First();
        editor.Append(v1, editor.CreateClip(log));
        editor.Append(v1, editor.CreateClip(hfr));
        editor.Append(v1, editor.CreateClip(old));

        var issues = ExportPreflight.Check(editor.Project, ExportPreset.Default, fileExists: _ => true);
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Warning && i.Message.Contains("Canon Log 3"));
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Warning && i.Message.Contains("konformiert"));
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Warning && i.Message.Contains("hochskaliert"));
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Info && i.Message.Contains("23,976") || i.Message.Contains("23.976"));
        Assert.False(ExportPreflight.HasErrors(issues));

        var missing = ExportPreflight.Check(editor.Project, ExportPreset.Default, fileExists: _ => false);
        Assert.True(ExportPreflight.HasErrors(missing));

        Assert.True(ExportPreflight.HasErrors(ExportPreflight.Check(editor.Project, ExportPreset.Default, 100, 50, _ => true)));
        Assert.True(ExportPreflight.HasErrors(ExportPreflight.Check(Editor().Project, ExportPreset.Default)));
        Assert.Contains(ExportPreflight.Check(editor.Project, ExportPreset.Find("social-vertical")!, fileExists: _ => true),
            i => i.Message.Contains("unscharfem"));
    }

    [Fact]
    public void Eta_And_Naming()
    {
        var eta = new EtaEstimator();
        Assert.Null(eta.Add(0, 0));
        Assert.Null(eta.Add(0.5, 0.05));
        var remaining = eta.Add(10, 0.25)!.Value;
        Assert.Equal(30, remaining.TotalSeconds, 0);
        Assert.Equal("noch 30 s", EtaEstimator.Format(remaining));
        Assert.Equal("noch 1 h 5 min", EtaEstimator.Format(TimeSpan.FromMinutes(65)));

        Assert.Equal("Urlaub_Teil 1 - Hochformat.mp4", ExportNaming.Suggest("Urlaub/Teil 1", ExportPreset.Find("social-vertical")!));
        var existing = new HashSet<string> { Path.Combine("x", "a.mp4"), Path.Combine("x", "a (2).mp4") };
        Assert.Equal(Path.Combine("x", "a (3).mp4"), ExportNaming.MakeUnique(Path.Combine("x", "a.mp4"), existing.Contains));
    }

    [Fact]
    public async Task RealFfmpeg_VerticalRangeAudioAndStill()
    {
        if (FfmpegLocator.LocateDefault() is not { } paths)
        {
            return;
        }

        var client = new FfmpegClient(paths);
        var source = Path.Combine(_temp, "MVI_0001.MOV");
        await client.RunAsync(["-y", "-v", "error", "-f", "lavfi", "-i", "testsrc2=s=640x360:r=25:d=4", "-f", "lavfi", "-i", "sine=d=4",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "pcm_s16le", source], 0);
        var media = MediaItem.FromRecording(new RecordingGroup([await client.ProbeAsync(source)]));
        var editor = Editor(640, 360);
        editor.AddMedia([media]);
        editor.Append(editor.Project.VideoTracks.First(), editor.CreateClip(media));

        foreach (var (preset, w, h) in new[]
                 {
                     (ExportPreset.Find("social-vertical")! with { OutputWidth = 360, OutputHeight = 640 }, 360, 640),
                     (ExportPreset.Find("social-square")! with { OutputWidth = 360, OutputHeight = 360 }, 360, 360),
                 })
        {
            var output = Path.Combine(_temp, preset.Id + preset.Extension);
            var job = ExportBuilder.Build(editor.Project, preset, output, 25, 75);
            await client.RunAsync(job.Arguments, job.DurationSeconds);
            var info = await client.ProbeAsync(output);
            Assert.Equal(w, info.Video!.Width);
            Assert.Equal(h, info.Video.Height);
            Assert.Equal(2.0, info.Duration.TotalSeconds, 1);
        }

        var wav = Path.Combine(_temp, "ton.wav");
        var audioJob = ExportBuilder.Build(editor.Project, ExportPreset.Find("audio-wav")!, wav);
        await client.RunAsync(audioJob.Arguments, audioJob.DurationSeconds);
        var audio = await client.ProbeAsync(wav);
        Assert.False(audio.HasVideo);
        Assert.Equal(4.0, audio.Duration.TotalSeconds, 1);

        var png = Path.Combine(_temp, "bild.png");
        await client.RunAsync(FfmpegArgs.StillFrame(source, 1.5, null, png), 0);
        Assert.True(File.Exists(png));
    }
}
