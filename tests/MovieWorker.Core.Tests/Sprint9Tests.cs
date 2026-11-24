using MovieWorker.Core.Camera;
using MovieWorker.Core.Export;
using MovieWorker.Core.Media;
using MovieWorker.Core.Preview;
using MovieWorker.Core.Projects;
using MovieWorker.Core.Time;
using MovieWorker.Core.Timeline;

namespace MovieWorker.Core.Tests;

public sealed class Sprint9Tests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "mw-s9-" + Guid.NewGuid().ToString("N"));

    public Sprint9Tests() => Directory.CreateDirectory(_temp);

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    private static TimelineEditor Setup(out MediaItem media)
    {
        var editor = new TimelineEditor(MovieProject.CreateDefault(new ProjectSettings { Width = 1920, Height = 1080, FrameRate = FrameRate.Fps25 }));
        media = MediaItem.FromRecording(new RecordingGroup([TestMedia.Eos70D()]));
        editor.AddMedia([media]);
        var v1 = editor.Project.VideoTracks.First();
        editor.Append(v1, editor.CreateClip(media, 10, 14));
        editor.Append(v1, editor.CreateClip(media, 20, 24));
        editor.Append(v1, editor.CreateClip(media, 30, 34));
        return editor;
    }

    [Fact]
    public void Transition_UsesPrerollAndKeepsTiming()
    {
        var editor = Setup(out _);
        var clips = editor.Project.VideoTracks.First().OrderedClips.ToList();
        Assert.True(editor.SetTransition(clips[1].Id, new Transition { Type = TransitionType.WipeLeft, DurationFrames = 25 }));
        var job = ExportBuilder.Build(editor.Project, ExportPreset.Default, "o.mp4");
        var args = string.Join(' ', job.Arguments);
        Assert.Contains("-ss 19 -t 5 -i", args);                     // 1 s Vorlauf aus der Quelle
        Assert.Contains("xfade=transition=wipeleft:duration=1:offset=3", args);
        Assert.Contains("acrossfade=d=1", args);
        Assert.Contains("concat=n=2:v=1:a=1[vjoin][acam]", args);    // dritter Clip ohne Übergang
        Assert.Equal(12.0, job.DurationSeconds, 6);
    }

    [Fact]
    public void Transition_LimitedByAvailableHead()
    {
        var editor = Setup(out var media);
        var v1 = editor.Project.VideoTracks.First();
        editor.Append(v1, editor.CreateClip(media, 0.4, 2)); // nur 10 Bilder Vorlauf
        var last = editor.Project.VideoTracks.First().OrderedClips.Last();
        editor.SetTransition(last.Id, new Transition { DurationFrames = 50 });
        var args = string.Join(' ', ExportBuilder.Build(editor.Project, ExportPreset.Default, "o.mp4").Arguments);
        Assert.Contains("xfade=transition=fade:duration=0.4:", args);
    }

    [Fact]
    public void Titles_TrackAndExportFilters()
    {
        var editor = Setup(out _);
        var title = editor.AddTitle(TitleSpec.FromStyle(TitleStyle.LowerThird), 25, 100);
        Assert.Equal(TrackKind.Title, editor.Project.Tracks[0].Kind);
        Assert.Equal("Name Nachname", title.Name);
        editor.AddTitle(TitleSpec.FromStyle(TitleStyle.Headline), 50, 50); // überlappt → zweite Titelspur
        Assert.Equal(2, editor.Project.TitleTracks.Count());

        var args = string.Join(' ', ExportBuilder.Build(editor.Project, ExportPreset.Default, "o.mp4").Arguments);
        Assert.Contains("drawtext=", args);
        Assert.Contains("text='Name Nachname'", args);
        Assert.Contains("text='Funktion'", args);
        Assert.Contains("box=1:boxcolor=black@0.6", args);
        Assert.Contains("enable='between(t,1,5)'", args);
        Assert.Contains("expansion=none", args);
        Assert.Equal(12.0, ExportBuilder.Build(editor.Project, ExportPreset.Default, "o.mp4").DurationSeconds, 6);

        var json = ProjectSerializer.Serialize(editor.Project);
        var loaded = ProjectSerializer.Deserialize(json);
        Assert.Equal(TitleStyle.LowerThird, loaded.TitleTracks.SelectMany(t => t.Clips).First(c => c.StartFrame == 25).Title!.Style);
    }

    [Fact]
    public void Titles_TypewriterCreditsAndSanitizing()
    {
        var spec = TitleSpec.FromStyle(TitleStyle.Caption);
        spec.Text = "It's 100% \\ok";
        var clip = new TimelineClip { Title = spec, StartFrame = 0, LengthFrames = 100 };
        var filters = TitleRenderer.ForClip(clip, FrameRate.Fps25, 1920, 1080, null);
        Assert.Equal(spec.Text.Length, filters.Count); // ein Filter pro Buchstabe
        Assert.DoesNotContain(filters, f => f.Contains("It's"));
        Assert.Contains(filters, f => f.Contains("It’s 100％ /ok"));

        var credits = new TimelineClip { Title = TitleSpec.FromStyle(TitleStyle.Credits), LengthFrames = 250 };
        Assert.All(TitleRenderer.ForClip(credits, FrameRate.Fps25, 1920, 1080, null), f => Assert.Contains("y='1080-(t-0)/10*", f));

        var still = TitleRenderer.ForClip(clip, FrameRate.Fps25, 1920, 1080, 1.0);
        Assert.Single(still); // zu diesem Zeitpunkt genau ein Schreibmaschinen-Schritt sichtbar
    }

    [Fact]
    public void Preview_IncludesTitlesAndTitleOnlyFrames()
    {
        var editor = Setup(out _);
        editor.AddTitle(TitleSpec.FromStyle(TitleStyle.Headline), 0, 400);
        var source = PlaybackPlanner.VideoAt(editor.Project, 10)!;
        Assert.Contains("drawtext", PlaybackPlanner.PreviewFilter(editor.Project, source, 10, 540));
        Assert.Contains("drawtext", PlaybackPlanner.TitleOnlyFilter(editor.Project, 350, 540));
        Assert.Null(PlaybackPlanner.TitleOnlyFilter(editor.Project, 450, 540));
        Assert.Null(PlaybackPlanner.VideoAt(editor.Project, 350)); // Titel sind kein Video
    }

    [Fact]
    public void Fonts_ResolveWindowsOrFallback()
    {
        var windir = Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows";
        var expected = Path.Combine(windir, "Fonts", "arialbd.ttf");
        Assert.Equal(expected, FontResolver.Resolve("Arial", bold: true, p => p == expected));
        Assert.Equal("/usr/share/fonts/truetype/dejavu/DejaVuSerif.ttf",
            FontResolver.Resolve("Georgia", false, p => p.EndsWith("DejaVuSerif.ttf", StringComparison.Ordinal)));
        Assert.Null(FontResolver.Resolve("Arial", false, _ => false));
    }

    [Fact]
    public async Task RealFfmpeg_TransitionsAndAllTitleStyles()
    {
        if (FfmpegLocator.LocateDefault() is not { } paths)
        {
            return;
        }

        var client = new FfmpegClient(paths);
        var source = Path.Combine(_temp, "MVI_0001.MOV");
        await client.RunAsync(["-y", "-v", "error", "-f", "lavfi", "-i", "testsrc2=s=640x360:r=25:d=8", "-f", "lavfi", "-i", "sine=d=8",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "pcm_s16le", source], 0);
        var media = MediaItem.FromRecording(new RecordingGroup([await client.ProbeAsync(source)]));
        var editor = new TimelineEditor(MovieProject.CreateDefault(new ProjectSettings { Width = 640, Height = 360, FrameRate = FrameRate.Fps25 }));
        editor.AddMedia([media]);
        var v1 = editor.Project.VideoTracks.First();
        editor.Append(v1, editor.CreateClip(media, 0, 2));
        editor.Append(v1, editor.CreateClip(media, 3, 5));
        editor.Append(v1, editor.CreateClip(media, 6, 8));
        var clips = editor.Project.VideoTracks.First().OrderedClips.ToList();
        editor.SetTransition(clips[1].Id, new Transition { Type = TransitionType.Crossfade, DurationFrames = 12 });
        editor.SetTransition(clips[2].Id, new Transition { Type = TransitionType.CircleOpen, DurationFrames = 12 });
        var t = 0L;
        foreach (var style in Enum.GetValues<TitleStyle>())
        {
            editor.AddTitle(TitleSpec.FromStyle(style), t, 40);
            t += 30;
        }

        var output = Path.Combine(_temp, "titel.mp4");
        var job = ExportBuilder.Build(editor.Project, ExportPreset.Default, output);
        await client.RunAsync(job.Arguments, job.DurationSeconds);
        var info = await client.ProbeAsync(output);
        Assert.Equal(6.0, info.Duration.TotalSeconds, 0);
        Assert.True(info.HasAudio);

        var png = Path.Combine(_temp, "titel.png");
        await client.RunAsync(["-y", "-v", "error", "-f", "lavfi", "-i", "color=c=black:s=640x360:d=1", "-frames:v", "1",
            "-vf", PlaybackPlanner.TitleOnlyFilter(editor.Project, 35, 360)!, png], 0);
        Assert.True(File.Exists(png));
    }
}
