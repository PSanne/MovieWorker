using MovieWorker.Core.Camera;
using MovieWorker.Core.Color;
using MovieWorker.Core.Effects;
using MovieWorker.Core.Export;
using MovieWorker.Core.Media;
using MovieWorker.Core.Projects;
using MovieWorker.Core.Time;
using MovieWorker.Core.Timeline;

namespace MovieWorker.Core.Tests;

public sealed class Sprint15Tests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "mw-s15-" + Guid.NewGuid().ToString("N"));

    public Sprint15Tests() => Directory.CreateDirectory(_temp);

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    private static (TimelineEditor Editor, Guid V1Clip, Guid V2Clip) MakeProject()
    {
        var media = MediaItem.FromRecording(new RecordingGroup([TestMedia.Eos70D()]));
        var editor = new TimelineEditor(MovieProject.CreateDefault(new ProjectSettings { Width = 1920, Height = 1080, FrameRate = FrameRate.Fps25 }));
        editor.AddMedia([media]);
        editor.Append(editor.Project.VideoTracks.First(), editor.CreateClip(media, 0, 4));
        var v2 = editor.AddTrack(TrackKind.Video);
        editor.Insert(v2, editor.CreateClip(media, 10, 12), 25);
        return (editor, editor.Project.VideoTracks.First().Clips[0].Id, editor.Project.VideoTracks.Skip(1).First().Clips[0].Id);
    }

    [Fact]
    public void Effects_FiltersForEveryKind()
    {
        foreach (var kind in VideoEffect.All)
        {
            var filter = new VideoEffect { Kind = kind }.Filter(1920, 1080);
            Assert.False(string.IsNullOrWhiteSpace(filter));
            Assert.NotEqual("null", filter);
            Assert.False(string.IsNullOrWhiteSpace(VideoEffect.DisplayName(kind)));
        }

        var key = new VideoEffect { Kind = EffectKind.ChromaKey, Amount = 0.4, Softness = 0.2, Spill = 0.6 }.Filter(1920, 1080);
        Assert.StartsWith("format=yuva420p,chromakey=color=0x00B140:similarity=0.22:blend=0.06", key);
        Assert.Contains("despill=type=green:mix=0.6:expand=0.18", key);
        var blue = new VideoEffect { Kind = EffectKind.ChromaKey, KeyColor = VideoEffect.BlueScreen, Spill = 0.5 }.Filter(1920, 1080);
        Assert.Contains("chromakey=color=0x0047BB", blue);
        Assert.Contains("despill=type=blue", blue);
        Assert.DoesNotContain("despill", new VideoEffect { Kind = EffectKind.ChromaKey, Spill = 0 }.Filter(1920, 1080));

        Assert.Equal("pixelize=w=60:h=60", new VideoEffect { Kind = EffectKind.Pixelate, Amount = 0.5 }.Filter(1920, 1080));
        Assert.Equal("hue=s=0", new VideoEffect { Kind = EffectKind.BlackWhite, Amount = 1 }.Filter(1920, 1080));
        Assert.Equal("colorchannelmixer=1:0:0:0:0:1:0:0:0:0:1:0", new VideoEffect { Kind = EffectKind.Sepia, Amount = 0 }.Filter(1920, 1080));
        Assert.Contains("geq=", new VideoEffect { Kind = EffectKind.LumaKey }.Filter(1920, 1080));

        Assert.Equal("#00B140", VideoEffect.NormalizeColor("00b140"));
        Assert.Equal("#00B140", VideoEffect.NormalizeColor("Unsinn"));
        Assert.True(VideoEffect.IsBluish("#1030E0"));
        Assert.False(VideoEffect.IsBluish("#10E030"));
        Assert.Contains("Maske", new VideoEffect { Kind = EffectKind.Blur, Mask = new ColorMask() }.Summary);
    }

    [Fact]
    public void Editor_AddMoveUpdateRemoveEffects()
    {
        var (editor, clipId, _) = MakeProject();
        var blur = editor.AddEffect(clipId, EffectKind.Blur)!.Value;
        var grain = editor.AddEffect(clipId, EffectKind.FilmGrain)!.Value;
        var bw = editor.AddEffect(clipId, EffectKind.BlackWhite)!.Value;
        Assert.Equal([blur, grain, bw], editor.Project.FindClip(clipId)!.Value.Clip.Effects.Select(e => e.Id));

        Assert.True(editor.MoveEffect(clipId, bw, 0));
        Assert.Equal([bw, blur, grain], editor.Project.FindClip(clipId)!.Value.Clip.Effects.Select(e => e.Id));
        Assert.False(editor.MoveEffect(clipId, bw, 0));

        Assert.True(editor.UpdateEffect(clipId, blur, "Stärke", e => e.Amount = 0.2));
        Assert.True(editor.UpdateEffect(clipId, blur, "Stärke", e => e.Amount = 0.3));
        editor.Undo();   // beide Stärke-Schritte zusammengefasst
        Assert.Equal(0.5, editor.Project.FindClip(clipId)!.Value.Clip.Effects.First(e => e.Id == blur).Amount);

        Assert.True(editor.RemoveEffect(clipId, grain));
        Assert.Equal(2, editor.Project.FindClip(clipId)!.Value.Clip.Effects.Count);

        var copy = editor.Project.FindClip(clipId)!.Value.Clip.Clone();
        copy.Effects[0].Amount = 0;
        Assert.NotEqual(0, editor.Project.FindClip(clipId)!.Value.Clip.Effects[0].Amount);

        var back = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(editor.Project));
        Assert.Equal(2, back.FindClip(clipId)!.Value.Clip.Effects.Count);
        Assert.Equal(EffectKind.BlackWhite, back.FindClip(clipId)!.Value.Clip.Effects[0].Kind);
    }

    [Fact]
    public void Export_EffectChainsMasksAndKeys()
    {
        var (editor, v1Clip, v2Clip) = MakeProject();
        var blur = editor.AddEffect(v1Clip, EffectKind.Blur)!.Value;
        var pixel = editor.AddEffect(v1Clip, EffectKind.Pixelate)!.Value;
        editor.UpdateEffect(v1Clip, pixel, "Maske", e => e.Mask = new ColorMask { CenterX = 0.3, Width = 0.2, Height = 0.3 });
        var key = editor.AddEffect(v2Clip, EffectKind.ChromaKey)!.Value;
        editor.UpdateEffect(v2Clip, key, "Maske", e => e.Mask = new ColorMask { Shape = MaskShape.Rectangle, Width = 0.8, Height = 0.9 });

        var graph = string.Join(' ', ExportBuilder.Build(editor.Project, ExportPreset.Default, "out.mp4").Arguments);
        Assert.Contains("[fx0]gblur=sigma=15.5[e0f0]", graph);
        Assert.Contains("[e0f0]split=3[e0b0][e0g0][e0m0]", graph);
        Assert.Contains("[e0g0]pixelize=w=60:h=60,format=yuva444p[e0gl0]", graph);
        Assert.Contains("[e0b0][e0ga0]overlay=format=auto[e0o0]", graph);
        Assert.DoesNotContain("[kb0]", graph);   // V1 ohne Key: kein schwarzer Hintergrund nötig

        // Key auf V2: Bildgröße mit durchsichtigem Rand, Key mit Garbage Matte, Alpha bleibt erhalten
        Assert.Contains("pad=1920:1080:(ow-iw)/2:(oh-ih)/2:color=black@0", graph);
        Assert.Contains("chromakey=color=0x00B140", graph);
        Assert.Contains("alphaextract", graph);
        Assert.Contains("blend=all_mode=multiply", graph);
        Assert.Contains("overlay=x=", graph);

        // Key auf V1: über Schwarz legen
        editor.AddEffect(v1Clip, EffectKind.LumaKey);
        var keyed = string.Join(' ', ExportBuilder.Build(editor.Project, ExportPreset.Default, "out.mp4").Arguments);
        Assert.Contains("color=c=black:s=1920x1080:r=25", keyed);
        Assert.Contains("overlay=shortest=1:format=auto,format=yuv420p[kv0]", keyed);
        Assert.Contains(ExportPreflight.Check(editor.Project, ExportPreset.Default, fileExists: _ => true), i => i.Message.Contains("freigestellter"));
    }

    [Fact]
    public async Task RealFfmpeg_GreenscreenOverBackgroundAndMaskedPixelate()
    {
        if (FfmpegLocator.LocateDefault() is not { } paths)
        {
            return;
        }

        var client = new FfmpegClient(paths);
        var background = Path.Combine(_temp, "MVI_0001.MOV");
        await client.RunAsync(["-y", "-v", "error", "-f", "lavfi", "-i", "color=c=0x2040E0:s=320x180:r=25:d=2", "-f", "lavfi", "-i", "sine=d=2",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "pcm_s16le", background], 0);
        var green = Path.Combine(_temp, "MVI_0002.MOV");
        await client.RunAsync(["-y", "-v", "error", "-f", "lavfi", "-i", "color=c=0x00B140:s=320x180:r=25:d=2,drawbox=x=130:y=60:w=60:h=60:c=0xE02020:t=fill",
            "-f", "lavfi", "-i", "sine=d=2", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "pcm_s16le", green], 0);

        var bg = MediaItem.FromRecording(new RecordingGroup([await client.ProbeAsync(background)]));
        var fg = MediaItem.FromRecording(new RecordingGroup([await client.ProbeAsync(green)]));
        var editor = new TimelineEditor(MovieProject.CreateDefault(new ProjectSettings { Width = 320, Height = 180, FrameRate = FrameRate.Fps25 }));
        editor.AddMedia([bg, fg]);
        editor.Append(editor.Project.VideoTracks.First(), editor.CreateClip(bg, 0, 2));
        var v2 = editor.AddTrack(TrackKind.Video);
        editor.Insert(v2, editor.CreateClip(fg, 0, 2), 0);
        var keyClip = editor.Project.VideoTracks.Skip(1).First().Clips[0].Id;
        editor.AddEffect(keyClip, EffectKind.ChromaKey);

        async Task<(int R, int G, int B)> Pixel(int x, int y)
        {
            var raw = await client.RunForBytesAsync(ExportBuilder.BuildRawFrame(editor.Project, 10, 180).Arguments);
            var index = (y * 320 + x) * 3;
            return (raw[index], raw[index + 1], raw[index + 2]);
        }

        var corner = await Pixel(20, 20);
        Assert.True(corner.B > 150 && corner.G < 120, $"Ecke sollte den blauen Hintergrund zeigen, ist {corner}");
        var center = await Pixel(160, 90);
        Assert.True(center.R > 150 && center.G < 100, $"Mitte sollte rot bleiben, ist {center}");

        // Verpixeln nur in der Maske links oben; Mitte bleibt unverändert
        var v1Clip = editor.Project.VideoTracks.First().Clips[0].Id;
        var pixel = editor.AddEffect(v1Clip, EffectKind.Pixelate)!.Value;
        editor.UpdateEffect(v1Clip, pixel, "Maske", e => e.Mask = new ColorMask { CenterX = 0.2, CenterY = 0.2, Width = 0.3, Height = 0.3 });
        editor.AddEffect(v1Clip, EffectKind.FilmGrain);
        var output = Path.Combine(_temp, "fx.mp4");
        var job = ExportBuilder.Build(editor.Project, ExportPreset.Default, output);
        await client.RunAsync(job.Arguments, job.DurationSeconds);
        Assert.Equal(320, (await client.ProbeAsync(output)).Video!.Width);
    }
}
