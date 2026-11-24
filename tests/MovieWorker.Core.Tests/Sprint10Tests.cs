using MovieWorker.Core.Camera;
using MovieWorker.Core.Export;
using MovieWorker.Core.Media;
using MovieWorker.Core.Preview;
using MovieWorker.Core.Projects;
using MovieWorker.Core.Time;
using MovieWorker.Core.Timeline;

namespace MovieWorker.Core.Tests;

public sealed class Sprint10Tests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "mw-s10-" + Guid.NewGuid().ToString("N"));

    public Sprint10Tests() => Directory.CreateDirectory(_temp);

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    private static MediaItem Item(string path, double offsetSeconds = 0) =>
        MediaItem.FromRecording(new RecordingGroup([TestMedia.Eos70D(path)]));

    [Fact]
    public void Overlay_PipBlendCropFlipAndAudio()
    {
        var editor = new TimelineEditor(MovieProject.CreateDefault(new ProjectSettings { Width = 1920, Height = 1080, FrameRate = FrameRate.Fps25 }));
        var a = Item("/c/MVI_0001.MOV");
        var b = Item("/c/MVI_0002.MOV");
        editor.AddMedia([a, b]);
        editor.Append(editor.Project.VideoTracks.First(), editor.CreateClip(a, 0, 10));
        var v2 = editor.AddTrack(TrackKind.Video);
        var pip = editor.CreateClip(b, 0, 4);
        pip.Transform.Scale = 30;
        pip.Transform.PositionX = 30;
        pip.Transform.Opacity = 80;
        pip.Transform.CropLeft = 10;
        pip.Transform.FlipHorizontal = true;
        pip.SetValue(AnimatedProperty.PositionY, -30, 0, true);
        pip.SetValue(AnimatedProperty.PositionY, 30, 99, true);
        editor.Insert(v2, pip, 25);
        var v3 = editor.AddTrack(TrackKind.Video);
        var leak = editor.CreateClip(b, 5, 7);
        leak.Transform.Blend = BlendMode.Screen;
        leak.Transform.Opacity = 50;
        editor.Insert(v3, leak, 100);

        var graph = string.Join(' ', ExportBuilder.Build(editor.Project, ExportPreset.Default, "o.mp4").Arguments);
        Assert.Contains("scale=576:324:force_original_aspect_ratio=decrease", graph);
        Assert.Contains("crop=", graph);
        Assert.Contains("hflip", graph);
        Assert.Contains("colorchannelmixer=aa=0.8", graph);
        Assert.Contains("setpts=PTS-STARTPTS+1/TB", graph);
        Assert.Contains("overlay=x='(W-w)/2+576':y='(H-h)/2+(if(lt((t-1),0),-30", graph);
        Assert.Contains("enable='between(t,1,5)'", graph);
        Assert.Contains("blend=all_mode=screen:all_opacity=0.5:enable='between(t,4,6)'", graph);
        Assert.Contains("tpad=start_duration=4:color=black", graph);
        Assert.Contains("adelay=delays=1000:all=1", graph); // Ton des PiP-Clips wird gemischt
    }

    [Fact]
    public void Still_UsesExportChainWithoutProgress()
    {
        var editor = new TimelineEditor(MovieProject.CreateDefault(new ProjectSettings { Width = 1920, Height = 1080, FrameRate = FrameRate.Fps25 }));
        var a = Item("/c/MVI_0001.MOV");
        editor.AddMedia([a]);
        editor.Append(editor.Project.VideoTracks.First(), editor.CreateClip(a, 0, 10));
        editor.AddTitle(TitleSpec.FromStyle(TitleStyle.Headline), 0, 100);
        var job = ExportBuilder.BuildStill(editor.Project, 50, 541);
        var args = string.Join(' ', job.Arguments);
        Assert.DoesNotContain("-progress", args);
        Assert.Contains("-frames:v 1 -f image2pipe -vcodec png -", args);
        Assert.Contains("[aout]anullsink", args);
        Assert.Contains("scale=960:540", args);           // gerade Vorschauhöhe
        Assert.Contains("enable='between(t,-2,2)'", args); // Titel behält seine Animationsphase
    }

    [Fact]
    public void Multicam_CreateSwitchAndResolve()
    {
        var rate = FrameRate.Fps25;
        var r6 = MediaItem.FromRecording(new RecordingGroup([TestMedia.R6Clog()]));  // 30,03 s, AAC
        var eos70 = Item("/c/MVI_0001.MOV");                                         // 120 s
        var clip = MulticamResolver.Create([r6, eos70], [0, 12.5], rate, "Interview");
        Assert.Equal(0, clip.SourceIn);
        Assert.Equal(rate.SecondsToFrames(30.03), clip.LengthFrames);

        var late = MulticamResolver.Create([r6, eos70], [0, -5], rate, "Spät");   // 70D startet 5 s später
        Assert.Equal(5, late.SourceIn, 6);

        var editor = new TimelineEditor(MovieProject.CreateDefault(new ProjectSettings { Width = 1920, Height = 1080, FrameRate = rate }));
        editor.AddMedia([r6, eos70]);
        editor.Append(editor.Project.VideoTracks.First(), clip);
        Assert.True(editor.SwitchAngle(100, 1));
        Assert.True(editor.SwitchAngle(300, 0));
        var pieces = editor.Project.VideoTracks.First().OrderedClips.ToList();
        Assert.Equal([0, 1, 0], pieces.Select(p => p.ActiveAngle));
        Assert.False(editor.SwitchAngle(300, 0)); // schon diese Kamera

        var resolved = MulticamResolver.Resolve(editor.Project);
        var v1 = resolved.VideoTracks.First().OrderedClips.ToList();
        Assert.Equal("/c/MVI_0001.MOV", v1[1].PrimaryPath);
        Assert.Equal(4 + 12.5, v1[1].SourceIn, 6);  // Multicam-Zeit 4 s + Versatz
        Assert.False(v1[1].HasAudio);
        var sound = resolved.AudioTracks.Single(t => t.Name.StartsWith("Multicam-Ton")).OrderedClips.ToList();
        Assert.Equal(3, sound.Count);
        Assert.All(sound, s => Assert.Equal(r6.PrimaryPath, s.PrimaryPath)); // Ton durchgehend von der R6

        Assert.Equal(eos70.Id, PlaybackPlanner.VideoAt(editor.Project, 150)!.Media!.Id);
        var json = ProjectSerializer.Serialize(editor.Project);
        Assert.Equal(2, ProjectSerializer.Deserialize(json).VideoTracks.First().Clips.First().MulticamAngles!.Count);
    }

    [Fact]
    public async Task RealFfmpeg_CompositeMulticamAndStill()
    {
        if (FfmpegLocator.LocateDefault() is not { } paths)
        {
            return;
        }

        var client = new FfmpegClient(paths);
        async Task<MediaItem> Make(string name, string pattern)
        {
            var file = Path.Combine(_temp, name);
            await client.RunAsync(["-y", "-v", "error", "-f", "lavfi", "-i", $"{pattern}=s=640x360:r=25:d=6", "-f", "lavfi", "-i", "sine=d=6",
                "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "pcm_s16le", file], 0);
            return MediaItem.FromRecording(new RecordingGroup([await client.ProbeAsync(file)]));
        }

        var a = await Make("MVI_0001.MOV", "testsrc2");
        var b = await Make("MVI_0002.MOV", "smptebars");
        var editor = new TimelineEditor(MovieProject.CreateDefault(new ProjectSettings { Width = 640, Height = 360, FrameRate = FrameRate.Fps25 }));
        editor.AddMedia([a, b]);
        var multicam = MulticamResolver.Create([a, b], [0, 0.5], FrameRate.Fps25, "Mehrkamera");
        editor.Append(editor.Project.VideoTracks.First(), multicam);
        editor.SwitchAngle(40, 1);
        var v2 = editor.AddTrack(TrackKind.Video);
        var pip = editor.CreateClip(b, 1, 3);
        pip.Transform.Scale = 35;
        pip.Transform.PositionX = 25;
        pip.Transform.Rotation = 8;
        pip.SetValue(AnimatedProperty.Opacity, 0, 0, true);
        pip.SetValue(AnimatedProperty.Opacity, 100, 12, true);
        editor.Insert(v2, pip, 10);
        var v3 = editor.AddTrack(TrackKind.Video);
        var blend = editor.CreateClip(a, 2, 3);
        blend.Transform.Blend = BlendMode.Multiply;
        editor.Insert(v3, blend, 60);
        editor.AddTitle(TitleSpec.FromStyle(TitleStyle.LowerThird), 20, 60);

        var output = Path.Combine(_temp, "comp.mp4");
        var job = ExportBuilder.Build(editor.Project, ExportPreset.Default, output);
        await client.RunAsync(job.Arguments, job.DurationSeconds);
        var info = await client.ProbeAsync(output);
        Assert.Equal(640, info.Video!.Width);
        Assert.True(info.HasAudio);

        var png = await client.RunForBytesAsync(ExportBuilder.BuildStill(editor.Project, 30, 180).Arguments);
        Assert.Equal(0x89, png[0]);
        Assert.Equal((byte)'P', png[1]);
    }
}
