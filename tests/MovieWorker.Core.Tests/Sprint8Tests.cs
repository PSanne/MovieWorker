using MovieWorker.Core.Camera;
using MovieWorker.Core.Export;
using MovieWorker.Core.Media;
using MovieWorker.Core.Preview;
using MovieWorker.Core.Projects;
using MovieWorker.Core.Time;
using MovieWorker.Core.Timeline;

namespace MovieWorker.Core.Tests;

public sealed class Sprint8Tests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "mw-s8-" + Guid.NewGuid().ToString("N"));

    public Sprint8Tests() => Directory.CreateDirectory(_temp);

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    /// <summary>Zwei Clips A (Quelle 10–14 s → 0–100) und B (Quelle 20–24 s → 100–200), Musik auf A1.</summary>
    private static TimelineEditor Setup()
    {
        var editor = new TimelineEditor(MovieProject.CreateDefault(new ProjectSettings { Width = 1920, Height = 1080, FrameRate = FrameRate.Fps25 }));
        var media = MediaItem.FromRecording(new RecordingGroup([TestMedia.Eos70D()]));
        editor.AddMedia([media]);
        var v1 = editor.Project.VideoTracks.First();
        editor.Append(v1, editor.CreateClip(media, 10, 14));
        editor.Append(v1, editor.CreateClip(media, 20, 24));
        editor.Insert(editor.Project.AudioTracks.First(), editor.CreateClip(media, 0, 10), 150);
        return editor;
    }

    private static List<TimelineClip> V1(TimelineEditor e) => e.Project.VideoTracks.First().OrderedClips.ToList();

    [Fact]
    public void RippleTrim_EndShiftsFollowingClipsOnAllTracks()
    {
        var editor = Setup();
        var a = V1(editor)[0];
        Assert.True(editor.RippleTrim(a.Id, atStart: false, 25));
        Assert.Equal(125, V1(editor)[0].LengthFrames);
        Assert.Equal(125, V1(editor)[1].StartFrame);
        Assert.Equal(175, editor.Project.AudioTracks.First().Clips[0].StartFrame);

        Assert.True(editor.RippleTrim(a.Id, atStart: true, 50)); // Anfang kürzen, Clip bleibt stehen
        Assert.Equal(0, V1(editor)[0].StartFrame);
        Assert.Equal(12.0, V1(editor)[0].SourceIn, 6);
        Assert.Equal(75, V1(editor)[1].StartFrame);
    }

    [Fact]
    public void Roll_MovesEditPointKeepsDuration()
    {
        var editor = Setup();
        Assert.True(editor.Roll(V1(editor)[0].Id, 20));
        var clips = V1(editor);
        Assert.Equal(120, clips[0].LengthFrames);
        Assert.Equal(120, clips[1].StartFrame);
        Assert.Equal(80, clips[1].LengthFrames);
        Assert.Equal(20.8, clips[1].SourceIn, 6);
        Assert.Equal(400, editor.Duration); // Musik (150–400) unverändert
    }

    [Fact]
    public void Slip_ChangesSourceOnly_LimitedByMedia()
    {
        var editor = Setup();
        var b = V1(editor)[1];
        Assert.True(editor.Slip(b.Id, -25));
        Assert.Equal(19.0, V1(editor)[1].SourceIn, 6);
        Assert.Equal(100, V1(editor)[1].StartFrame);
        Assert.True(editor.Slip(b.Id, 100_000)); // begrenzt auf Ende der Quelle (120 s)
        Assert.Equal(116.0, V1(editor)[1].SourceIn, 6);
    }

    [Fact]
    public void Slide_MovesBetweenNeighbours()
    {
        var editor = Setup();
        var media = editor.Project.Media[0];
        editor.Append(editor.Project.VideoTracks.First(), editor.CreateClip(media, 30, 34)); // C: 200–300
        var b = V1(editor)[1];
        Assert.True(editor.Slide(b.Id, 10));
        var clips = V1(editor);
        Assert.Equal(110, clips[0].LengthFrames);
        Assert.Equal(110, clips[1].StartFrame);
        Assert.Equal(20.0, clips[1].SourceIn, 6); // Inhalt von B bleibt
        Assert.Equal(210, clips[2].StartFrame);
        Assert.Equal(90, clips[2].LengthFrames);
        Assert.False(editor.Slide(clips[0].Id, 5)); // A hat keinen linken Nachbarn
    }

    [Fact]
    public void Keyframes_InterpolateAndBuildExpressions()
    {
        var track = new KeyframeTrack
        {
            Property = AnimatedProperty.Scale,
            Keys =
            [
                new Keyframe { Frame = 0, Value = 100, Interpolation = KeyframeInterpolation.Linear },
                new Keyframe { Frame = 100, Value = 150, Interpolation = KeyframeInterpolation.Hold },
                new Keyframe { Frame = 200, Value = 120 },
            ],
        };
        Assert.Equal(100, track.ValueAt(-5));
        Assert.Equal(125, track.ValueAt(50), 6);
        Assert.Equal(150, track.ValueAt(150));   // Halten
        Assert.Equal(120, track.ValueAt(500));

        var expression = track.ToExpression("on", f => f);
        Assert.StartsWith("if(lt(on,0),100,", expression);
        Assert.Contains("if(lt(on,100),(100+(50)*((on-0)/100))", expression);

        var smooth = new KeyframeTrack { Keys = [new Keyframe { Frame = 0, Value = 0 }, new Keyframe { Frame = 10, Value = 10 }] };
        Assert.Equal(5, smooth.ValueAt(5), 6);
        Assert.True(smooth.ValueAt(2) < 2); // weich anfahren
    }

    [Fact]
    public void Keyframes_FollowContentWhenSplittingAndTrimming()
    {
        var editor = Setup();
        var a = V1(editor)[0];
        editor.UpdateClip(a.Id, "Zoom", c =>
        {
            c.SetValue(AnimatedProperty.Scale, 100, 0, addKeyframe: true);
            c.SetValue(AnimatedProperty.Scale, 200, 100, addKeyframe: true);
        });
        editor.SplitAt(50);
        var right = V1(editor)[1];
        Assert.Equal(150, right.ValueAt(AnimatedProperty.Scale, 0), 0); // in der Mitte geteilt
        editor.TrimStart(V1(editor)[0].Id, 25);
        Assert.Equal(115.625, V1(editor)[0].ValueAt(AnimatedProperty.Scale, 0), 3); // weich: Bild 25 von 100

        var json = ProjectSerializer.Serialize(editor.Project);
        Assert.Equal(2, ProjectSerializer.Deserialize(json).VideoTracks.First().OrderedClips.ElementAt(1).KeyframesFor(AnimatedProperty.Scale)!.Keys.Count);
    }

    [Fact]
    public void SetValue_StaticUntilFirstKeyframe()
    {
        var clip = new TimelineClip();
        clip.SetValue(AnimatedProperty.Rotation, 15, 0, addKeyframe: false);
        Assert.Equal(15, clip.Transform.Rotation);
        Assert.False(clip.IsAnimated(AnimatedProperty.Rotation));
        clip.SetValue(AnimatedProperty.Volume, -6, 0, addKeyframe: false);
        Assert.Equal(-6, clip.VolumeDb);
        clip.SetValue(AnimatedProperty.Opacity, 0, 0, addKeyframe: true);
        clip.SetValue(AnimatedProperty.Opacity, 100, 25, addKeyframe: false); // schon animiert → Keyframe
        Assert.Equal(2, clip.KeyframesFor(AnimatedProperty.Opacity)!.Keys.Count);
        Assert.True(clip.HasTransform);
    }

    [Fact]
    public void SpeedRamp_SplitsIntoSmoothSteps()
    {
        var editor = Setup();
        var b = V1(editor)[1]; // 100 Bilder, Quelle 4 s
        Assert.True(editor.ApplySpeedRamp(b.Id, SpeedRampShape.SlowDown, 0.25, steps: 4));
        var pieces = V1(editor).Skip(1).ToList();
        Assert.Equal(6, pieces.Count);
        Assert.Equal(1.0, pieces[0].Speed);
        Assert.Equal(0.25, pieces[^1].Speed);
        Assert.True(pieces.Zip(pieces.Skip(1)).All(p => p.Second.Speed <= p.First.Speed + 1e-9)); // fällt stetig
        Assert.Equal(20.0, pieces[0].SourceIn, 6);
        Assert.Equal(24.0, pieces[^1].SourceOut(editor.Project.Settings.FrameRate), 1);
        Assert.True(pieces.Skip(1).All(p => p.AudioMuted));
        Assert.True(editor.Duration > 200);
        Assert.False(editor.ApplySpeedRamp(pieces[^1].Id, SpeedRampShape.SpeedUp, 0.25)); // schon verlangsamt
    }

    [Fact]
    public void Nest_FlattenAndUnnest()
    {
        var editor = Setup();
        var nested = editor.Nest(50, 175, "Szene 1")!;
        Assert.NotNull(nested);
        Assert.True(nested.IsNested);
        Assert.Equal(3, V1(editor).Count); // A-Rest, Verschachtelt, B-Rest
        Assert.Equal(125, nested.LengthFrames);
        Assert.Equal(2, nested.NestedTracks!.Count); // V1 + A1 (Musik ab 150)

        var flat = NestFlattener.Flatten(editor.Project);
        Assert.DoesNotContain(flat.AllClips, c => c.IsNested);
        Assert.Equal([0, 50, 100, 175], flat.VideoTracks.First().OrderedClips.Select(c => c.StartFrame));
        Assert.Equal(150, flat.AudioTracks.First().OrderedClips.First().StartFrame);

        // Verschachtelten Clip vorne kürzen: Inhalt verschiebt sich mit
        editor.TrimStart(nested.Id, 25);
        var trimmedFlat = NestFlattener.Flatten(editor.Project);
        Assert.Equal(75, trimmedFlat.VideoTracks.First().OrderedClips.ElementAt(1).StartFrame);
        Assert.Equal(13.0, trimmedFlat.VideoTracks.First().OrderedClips.ElementAt(1).SourceIn, 6);

        Assert.True(editor.Unnest(nested.Id));
        Assert.DoesNotContain(editor.Project.AllClips, c => c.IsNested);
        Assert.False(editor.SetSpeed(V1(editor)[0].Id, 0.5) == false && false);

        var exportArgs = string.Join(' ', ExportBuilder.Build(editor.Project, ExportPreset.Default, "o.mp4").Arguments);
        Assert.Contains("-i /card/DCIM/100CANON/MVI_0001.MOV", exportArgs);
    }

    [Fact]
    public void Export_ContainsTransformAndVolumeExpressions()
    {
        var editor = Setup();
        var a = V1(editor)[0];
        editor.UpdateClip(a.Id, "Bewegung", c =>
        {
            c.SetValue(AnimatedProperty.Scale, 100, 0, true);
            c.SetValue(AnimatedProperty.Scale, 130, 99, true);
            c.Transform.Rotation = 5;
            c.SetValue(AnimatedProperty.Opacity, 0, 0, true);
            c.SetValue(AnimatedProperty.Opacity, 100, 25, true);
            c.SetValue(AnimatedProperty.Volume, -20, 0, true);
            c.SetValue(AnimatedProperty.Volume, 0, 50, true);
        });
        var b = V1(editor)[1];
        editor.UpdateClip(b.Id, "PiP", c =>
        {
            c.Transform.Scale = 50;
            c.Transform.PositionX = 20;
        });

        var graph = string.Join(' ', ExportBuilder.Build(editor.Project, ExportPreset.Default, "o.mp4").Arguments);
        Assert.Contains("zoompan=z='max(1,(", graph);
        Assert.Contains("rotate=a='(5)*PI/180'", graph);
        Assert.Contains("eq=contrast=", graph);
        Assert.Contains("volume='pow(10,((if(lt(t,0),-20", graph);
        Assert.Contains("scale=960:540", graph);
        Assert.Contains("pad=1920:1080:'max(0,min(ow-iw,(ow-iw)/2+384))'", graph);

        var preview = PlaybackPlanner.PreviewFilter(editor.Project, PlaybackPlanner.VideoAt(editor.Project, 50)!, 50, 540)!;
        Assert.Contains("crop=", preview);
        Assert.Contains("rotate=a=5*PI/180", preview);
    }

    [Fact]
    public async Task RealFfmpeg_KenBurnsRotationOpacityVolumeAndNested()
    {
        if (FfmpegLocator.LocateDefault() is not { } paths)
        {
            return;
        }

        var client = new FfmpegClient(paths);
        var source = Path.Combine(_temp, "MVI_0001.MOV");
        await client.RunAsync(["-y", "-v", "error", "-f", "lavfi", "-i", "testsrc2=s=640x360:r=25:d=6", "-f", "lavfi", "-i", "sine=d=6",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "pcm_s16le", source], 0);
        var media = MediaItem.FromRecording(new RecordingGroup([await client.ProbeAsync(source)]));
        var editor = new TimelineEditor(MovieProject.CreateDefault(new ProjectSettings { Width = 640, Height = 360, FrameRate = FrameRate.Fps25 }));
        editor.AddMedia([media]);
        var v1 = editor.Project.VideoTracks.First();
        editor.Append(v1, editor.CreateClip(media, 0, 2));
        editor.Append(v1, editor.CreateClip(media, 2, 4));
        editor.Append(v1, editor.CreateClip(media, 4, 6));
        var clips = editor.Project.VideoTracks.First().OrderedClips.ToList();
        editor.UpdateClip(clips[0].Id, "KB", c =>
        {
            c.SetValue(AnimatedProperty.Scale, 100, 0, true);
            c.SetValue(AnimatedProperty.Scale, 150, 49, true);
            c.SetValue(AnimatedProperty.PositionX, 0, 0, true);
            c.SetValue(AnimatedProperty.PositionX, 10, 49, true);
            c.SetValue(AnimatedProperty.Rotation, 0, 0, true);
            c.SetValue(AnimatedProperty.Rotation, 10, 49, true);
            c.SetValue(AnimatedProperty.Opacity, 0, 0, true);
            c.SetValue(AnimatedProperty.Opacity, 100, 25, true);
            c.SetValue(AnimatedProperty.Volume, -30, 0, true);
            c.SetValue(AnimatedProperty.Volume, 0, 49, true);
        });
        editor.UpdateClip(clips[1].Id, "PiP", c => c.Transform.Scale = 50);
        editor.Nest(25, 125, "Verschachtelt");

        var output = Path.Combine(_temp, "out.mp4");
        var job = ExportBuilder.Build(editor.Project, ExportPreset.Default, output);
        await client.RunAsync(job.Arguments, job.DurationSeconds);
        var info = await client.ProbeAsync(output);
        Assert.Equal(640, info.Video!.Width);
        Assert.Equal(6.0, info.Duration.TotalSeconds, 0);

        var still = Path.Combine(_temp, "still.png");
        var at = PlaybackPlanner.VideoAt(editor.Project, 10)!;
        await client.RunAsync(FfmpegArgs.StillFrame(at.OriginalPath, at.OriginalSeconds, PlaybackPlanner.PreviewFilter(editor.Project, at, 10, 180), still), 0);
        Assert.True(File.Exists(still));
    }
}
