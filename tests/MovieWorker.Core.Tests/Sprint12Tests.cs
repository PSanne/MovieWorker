using MovieWorker.Core.Camera;
using MovieWorker.Core.Color;
using MovieWorker.Core.Export;
using MovieWorker.Core.Media;
using MovieWorker.Core.Projects;
using MovieWorker.Core.Time;
using MovieWorker.Core.Timeline;

namespace MovieWorker.Core.Tests;

public sealed class Sprint12Tests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "mw-s12-" + Guid.NewGuid().ToString("N"));

    public Sprint12Tests()
    {
        Directory.CreateDirectory(_temp);
        GradeLutCache.Folder = Path.Combine(Path.GetTempPath(), "mw-grades");
    }

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    [Fact]
    public void Curves_IdentityAndSCurve()
    {
        var curves = new ColorCurves();
        Assert.True(curves.IsIdentity);
        Assert.Equal(0.3, ColorCurves.Evaluate([], 0.3), 9);

        var s = new List<CurvePoint> { new() { X = 0.25, Y = 0.18 }, new() { X = 0.75, Y = 0.82 } };
        Assert.Equal(0.18, ColorCurves.Evaluate(s, 0.25), 6);
        Assert.Equal(0.5, ColorCurves.Evaluate(s, 0.5), 2);
        Assert.True(ColorCurves.Evaluate(s, 0.1) < 0.1);   // Schatten dunkler
        Assert.True(ColorCurves.Evaluate(s, 0.9) > 0.9);   // Lichter heller
        var previous = -1.0;
        for (var x = 0.0; x <= 1; x += 0.01)
        {
            var y = ColorCurves.Evaluate(s, x);
            Assert.True(y >= previous - 1e-9);               // monoton
            previous = y;
        }

        var grade = new ColorGrade();
        grade.Curves.Red.Add(new CurvePoint { X = 0.5, Y = 0.7 });
        Assert.False(grade.IsNeutral);
        Assert.True(grade.Apply(0.5, 0.5, 0.5).R > 0.6);
    }

    [Fact]
    public void Hsl_RoundTrip()
    {
        foreach (var (r, g, b) in new[] { (0.2, 0.4, 0.8), (0.9, 0.1, 0.1), (0.5, 0.5, 0.5), (0.3, 0.7, 0.2) })
        {
            var (h, s, l) = SecondaryCorrection.ToHsl(r, g, b);
            var back = SecondaryCorrection.FromHsl(h, s, l);
            Assert.Equal(r, back.R, 6);
            Assert.Equal(g, back.G, 6);
            Assert.Equal(b, back.B, 6);
        }
    }

    [Fact]
    public void Secondary_ChangesOnlySelectedColors()
    {
        var sky = new SecondaryCorrection { Enabled = true, HueCenter = 215, HueWidth = 50, Saturation = 150, Brightness = -0.3 };
        var blue = sky.Apply(0.35, 0.55, 0.85);
        var red = sky.Apply(0.85, 0.2, 0.15);
        Assert.NotEqual(0.85, blue.B, 3);           // Himmel verändert
        Assert.Equal(0.85, red.R, 6);                // Rot unverändert
        Assert.Equal(0.2, red.G, 6);
        sky.ShowSelection = true;
        Assert.True(sky.Apply(0.35, 0.55, 0.85).R > 0.8);
        Assert.True(sky.Apply(0.85, 0.2, 0.15).R < 0.05);
    }

    [Fact]
    public void Mask_CoverageAndExpression()
    {
        var mask = new ColorMask { Shape = MaskShape.Ellipse, CenterX = 0.5, CenterY = 0.5, Width = 0.4, Height = 0.4, Feather = 0.2 };
        Assert.Equal(1, mask.Coverage(0.5, 0.5, 0), 6);
        Assert.Equal(0, mask.Coverage(0.95, 0.95, 0), 6);
        Assert.InRange(mask.Coverage(0.7, 0.5, 0), 0.4, 0.6); // am Rand halb
        mask.Invert = true;
        Assert.Equal(0, mask.Coverage(0.5, 0.5, 0), 6);
        Assert.Contains("hypot(", mask.ToExpression(FrameRate.Fps25));

        var rect = new ColorMask { Shape = MaskShape.Rectangle, Feather = 0 };
        Assert.Equal(1, rect.Coverage(0.6, 0.6, 0));
        Assert.Contains("lte(max(", rect.ToExpression(FrameRate.Fps25));
    }

    [Fact]
    public void Tracker_FollowsMovingSquare()
    {
        const int w = 160, h = 90, count = 30;
        var frames = new byte[w * h * count];
        var random = new Random(3);
        for (var f = 0; f < count; f++)
        {
            for (var i = 0; i < w * h; i++)
            {
                frames[f * w * h + i] = (byte)(40 + random.Next(20));
            }

            var x0 = 30 + f * 3;
            var y0 = 30 + f;
            for (var y = y0; y < y0 + 16; y++)
            {
                for (var x = x0; x < x0 + 16; x++)
                {
                    frames[f * w * h + y * w + x] = (byte)(200 + (x + y) % 40);
                }
            }
        }

        var centers = ObjectTracker.Track(frames, w, h, count, (30 + 8) / (double)w, (30 + 8) / (double)h, 20.0 / w, 20.0 / h, searchRadius: 10);
        Assert.Equal(count, centers.Count);
        Assert.InRange(centers[^1].X * w, 38 + 29 * 3 - 2, 38 + 29 * 3 + 2);
        Assert.InRange(centers[^1].Y * h, 38 + 29 - 2, 38 + 29 + 2);

        var (tx, ty) = ObjectTracker.ToKeyframes(centers, 10, maxKeys: 8);
        Assert.True(tx.Keys.Count <= 9);
        Assert.Equal(10, tx.Keys[0].Frame);
        Assert.Equal(39, tx.Keys[^1].Frame);
    }

    private static (TimelineEditor Editor, TimelineClip Clip) Setup(MediaItem media)
    {
        var editor = new TimelineEditor(MovieProject.CreateDefault(new ProjectSettings { Width = 320, Height = 180, FrameRate = FrameRate.Fps25 }));
        editor.AddMedia([media]);
        editor.Append(editor.Project.VideoTracks.First(), editor.CreateClip(media, 0, 2));
        var clip = editor.Project.VideoTracks.First().Clips.Single();
        editor.UpdateClip(clip.Id, "Knoten", c =>
        {
            c.Grade.Curves.Luma.Add(new CurvePoint { X = 0.3, Y = 0.2 });
            c.Grade.Secondary.Enabled = true;
            c.Nodes.Add(new CorrectionNode { Name = "Gesicht aufhellen", Grade = new ColorGrade { Exposure = 0.5 }, Mask = new ColorMask() });
            c.Nodes.Add(new CorrectionNode { Name = "Vignette", Grade = new ColorGrade { Exposure = -1 }, Mask = new ColorMask { Invert = true, Width = 0.8, Height = 0.8 } });
            c.Nodes.Add(new CorrectionNode { Name = "Look", Grade = new ColorGrade { Saturation = 80 } });
            c.Nodes.Add(new CorrectionNode { Name = "Aus", Enabled = false, Grade = new ColorGrade { Exposure = 2 } });
            var (tx, ty) = ObjectTracker.ToKeyframes([(0.4, 0.5), (0.5, 0.5), (0.6, 0.4)], 0);
            c.Nodes[0].Mask!.TrackX = tx;
            c.Nodes[0].Mask!.TrackY = ty;
        });
        return (editor, editor.Project.VideoTracks.First().Clips.Single());
    }

    [Fact]
    public void Export_NodeGraphAndPersistence()
    {
        var (editor, _) = Setup(MediaItem.FromRecording(new RecordingGroup([TestMedia.Eos70D()])));
        var graph = string.Join(' ', ExportBuilder.Build(editor.Project, ExportPreset.Default, "o.mp4").Arguments);
        Assert.Equal(2, graph.Split("alphamerge").Length - 1);   // zwei maskierte Knoten
        Assert.Contains("geq=lum='255*(", graph);
        Assert.Contains("if(lt(T,", graph);                      // verfolgte Maske
        Assert.Equal(4, graph.Split("lut3d=file=").Length - 1);  // Hauptkorrektur + 3 aktive Knoten

        var loaded = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(editor.Project));
        var clip = loaded.VideoTracks.First().Clips.Single();
        Assert.Equal(4, clip.Nodes.Count);
        Assert.Equal(3, clip.Nodes[0].Mask!.TrackX!.Keys.Count);
        Assert.Single(clip.Grade.Curves.Luma);
    }

    [Fact]
    public async Task RealFfmpeg_MaskedNodesCurvesAndSecondary()
    {
        if (FfmpegLocator.LocateDefault() is not { } paths)
        {
            return;
        }

        var client = new FfmpegClient(paths);
        var file = Path.Combine(_temp, "MVI_0001.MOV");
        await client.RunAsync(["-y", "-v", "error", "-f", "lavfi", "-i", "testsrc2=s=320x180:r=25:d=2", "-f", "lavfi", "-i", "sine=d=2",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "pcm_s16le", file], 0);
        var media = MediaItem.FromRecording(new RecordingGroup([await client.ProbeAsync(file)]));
        var (editor, _) = Setup(media);
        var output = Path.Combine(_temp, "knoten.mp4");
        var job = ExportBuilder.Build(editor.Project, ExportPreset.Default, output);
        await client.RunAsync(job.Arguments, job.DurationSeconds);
        Assert.Equal(2.0, (await client.ProbeAsync(output)).Duration.TotalSeconds, 0);
        var png = await client.RunForBytesAsync(ExportBuilder.BuildStill(editor.Project, 10, 90).Arguments);
        Assert.Equal(0x89, png[0]);
    }
}
