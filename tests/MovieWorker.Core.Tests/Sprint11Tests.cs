using MovieWorker.Core.Camera;
using MovieWorker.Core.Color;
using MovieWorker.Core.Export;
using MovieWorker.Core.Media;
using MovieWorker.Core.Projects;
using MovieWorker.Core.Time;
using MovieWorker.Core.Timeline;

namespace MovieWorker.Core.Tests;

public sealed class Sprint11Tests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "mw-s11-" + Guid.NewGuid().ToString("N"));

    public Sprint11Tests()
    {
        Directory.CreateDirectory(_temp);
        GradeLutCache.Folder = Path.Combine(Path.GetTempPath(), "mw-grades");
    }

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    [Fact]
    public void Grade_NeutralIsIdentity()
    {
        var grade = new ColorGrade();
        Assert.True(grade.IsNeutral);
        var (r, g, b) = grade.Apply(0.2, 0.5, 0.8);
        Assert.Equal(0.2, r, 6);
        Assert.Equal(0.5, g, 6);
        Assert.Equal(0.8, b, 6);
    }

    [Fact]
    public void Grade_ExposureSaturationTemperature()
    {
        Assert.Equal(0.4, new ColorGrade { Exposure = 1 }.Apply(0.2, 0.2, 0.2).R, 6);
        var (r, g, b) = new ColorGrade { Saturation = 0 }.Apply(1, 0, 0);
        Assert.Equal(r, g, 6);
        Assert.Equal(g, b, 6);
        var warm = new ColorGrade { Temperature = 50 }.Apply(0.5, 0.5, 0.5);
        Assert.True(warm.R > 0.5 && warm.B < 0.5);
        var contrast = new ColorGrade { Contrast = 50 }.Apply(0.75, 0.25, 0.5);
        Assert.Equal(0.875, contrast.R, 6);
        Assert.Equal(0.125, contrast.G, 6);
        Assert.Equal(0.5, contrast.B, 6);
        var lift = new ColorGrade { Lift = { Master = 0.4 } }.Apply(0, 0, 0);
        Assert.True(lift.R > 0.05); // Schatten aufgehellt
        var gain = new ColorGrade { Gain = { Master = -0.4 } }.Apply(1, 1, 1);
        Assert.True(gain.R < 0.9);  // Lichter abgedunkelt
    }

    [Theory]
    [InlineData(0, 0.5)]
    [InlineData(120, 0.3)]
    [InlineData(250, 0.8)]
    public void Wheel_PolarRoundTrip(double hue, double amount)
    {
        var wheel = new ColorWheel();
        wheel.SetFromPolar(hue, amount);
        Assert.Equal(0, wheel.R + wheel.G + wheel.B, 6); // Helligkeit bleibt
        var (h, a) = wheel.ToPolar();
        Assert.Equal(hue, h, 3);
        Assert.Equal(amount, a, 3);
    }

    [Fact]
    public void Cube_WriteParseApply()
    {
        var grade = new ColorGrade { Exposure = 0.5, Saturation = 80, Temperature = 20 };
        var lut = CubeLut.Parse(GradeLutCache.ToCube(grade, 17));
        Assert.Equal(17, lut.Size);
        var expected = grade.Apply(0.3, 0.6, 0.1);
        var actual = lut.Apply(0.3f, 0.6f, 0.1f);
        Assert.Equal(expected.R, actual.R, 2);
        Assert.Equal(expected.G, actual.G, 2);
        Assert.Equal(expected.B, actual.B, 2);
        Assert.Throws<InvalidDataException>(() => CubeLut.Parse("LUT_3D_SIZE 2\n0 0 0\n"));

        var path = GradeLutCache.Ensure(grade);
        Assert.Equal(path, GradeLutCache.Ensure(grade.Clone())); // gleiche Korrektur → gleiche Datei
        Assert.NotEqual(path, GradeLutCache.Ensure(new ColorGrade { Exposure = 0.6 }));
    }

    [Fact]
    public void Lut_LibraryRecognizesCanonLog3()
    {
        Assert.True(LutLibrary.LooksLikeCanonLog3("CinemaGamut_CanonLog3-to-BT709_WideDR_33_FF_Ver.2.0.cube"));
        Assert.False(LutLibrary.LooksLikeCanonLog3("Teal Orange.cube"));
        File.WriteAllText(Path.Combine(_temp, "a.cube"), GradeLutCache.ToCube(new ColorGrade(), 2));
        Assert.Single(LutLibrary.Scan(_temp));
        Assert.Empty(LutLibrary.Scan(Path.Combine(_temp, "fehlt")));
    }

    [Fact]
    public void Scopes_AndColorMatch()
    {
        byte[] Image(byte r, byte g, byte b, int noise)
        {
            var data = new byte[64 * 36 * 3];
            var random = new Random(1);
            for (var i = 0; i < data.Length; i += 3)
            {
                data[i] = (byte)Math.Clamp(r + random.Next(-noise, noise + 1), 0, 255);
                data[i + 1] = (byte)Math.Clamp(g + random.Next(-noise, noise + 1), 0, 255);
                data[i + 2] = (byte)Math.Clamp(b + random.Next(-noise, noise + 1), 0, 255);
            }

            return data;
        }

        var reference = ScopeData.FromRgb24(Image(150, 120, 100, 40), 64, 36);
        var target = ScopeData.FromRgb24(Image(110, 120, 140, 20), 64, 36);
        Assert.Equal(64 * 36, reference.Histogram[0].Sum());
        Assert.Equal(64 * 36, Enumerable.Range(0, 256).Sum(v => reference.Waveform[0, v]) * 64);
        Assert.True(reference.ChannelStats[0].Mean > reference.ChannelStats[2].Mean);

        var grade = new ColorGrade();
        ColorMatch.Apply(reference, target, grade);
        Assert.True(grade.MatchGainR > 1.5);  // Ziel hat weniger Streuung → Kontrast hoch
        var matched = grade.Apply(110 / 255.0, 120 / 255.0, 140 / 255.0);
        Assert.Equal(150 / 255.0, matched.R, 1);
        Assert.Equal(100 / 255.0, matched.B, 1);
    }

    [Fact]
    public void Export_GradeToneMapAndHdr10()
    {
        var editor = new TimelineEditor(MovieProject.CreateDefault(new ProjectSettings { Width = 1920, Height = 1080, FrameRate = FrameRate.Fps25 }));
        var pq = MediaItem.FromRecording(new RecordingGroup([FfprobeParser.Parse("/c/MVI_0100.MP4", TestMedia.R6PqJson)]));
        var sdr = MediaItem.FromRecording(new RecordingGroup([TestMedia.Eos70D()]));
        editor.AddMedia([pq, sdr]);
        var v1 = editor.Project.VideoTracks.First();
        editor.Append(v1, editor.CreateClip(pq));
        editor.Append(v1, editor.CreateClip(sdr, 0, 5));
        var clips = editor.Project.VideoTracks.First().OrderedClips.ToList();
        editor.UpdateClip(clips[0].Id, "Tone-Mapping", c => c.Grade.ToneMap = ToneMapping.Mobius);
        editor.UpdateClip(clips[1].Id, "Farbe", c => c.Grade.Exposure = 0.3);

        var sdrArgs = string.Join(' ', ExportBuilder.Build(editor.Project, ExportPreset.Default, "o.mp4").Arguments);
        Assert.Contains("tonemap=tonemap=mobius", sdrArgs);
        Assert.Contains("lut3d=file=", sdrArgs);
        Assert.Contains("grade_", sdrArgs);

        var hdr = ExportPreset.Find("hevc-hdr10")!;
        var hdrArgs = string.Join(' ', ExportBuilder.Build(editor.Project, hdr, "o.mp4").Arguments);
        Assert.DoesNotContain("tonemap=", hdrArgs);                    // HDR bleibt HDR
        Assert.Contains("t=smpte2084:m=bt2020nc", hdrArgs);           // SDR-Clip nach PQ
        Assert.Contains("-color_trc smpte2084", hdrArgs);
        Assert.Contains("master-display=", hdrArgs);
        Assert.Equal("0x949494", TitleRenderer.TextColor("white", hdr: true));
        Assert.DoesNotContain(ExportPreflight.Check(editor.Project, hdr, fileExists: _ => true), i => i.Message.Contains("kein HDR-Material"));
    }

    [Fact]
    public async Task RealFfmpeg_GradedHdr10AndRawFrame()
    {
        if (FfmpegLocator.LocateDefault() is not { } paths)
        {
            return;
        }

        var client = new FfmpegClient(paths);
        var sdrFile = Path.Combine(_temp, "MVI_0001.MOV");
        await client.RunAsync(["-y", "-v", "error", "-f", "lavfi", "-i", "testsrc2=s=320x180:r=25:d=2", "-f", "lavfi", "-i", "sine=d=2",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "pcm_s16le", sdrFile], 0);
        var pqFile = Path.Combine(_temp, "MVI_0100.MP4");
        await client.RunAsync(["-y", "-v", "error", "-f", "lavfi", "-i", "testsrc=s=320x180:r=25:d=2", "-c:v", "libx265", "-pix_fmt", "yuv420p10le",
            "-x265-params", "log-level=error", "-color_trc", "smpte2084", "-color_primaries", "bt2020", "-colorspace", "bt2020nc", pqFile], 0);
        var sdr = MediaItem.FromRecording(new RecordingGroup([await client.ProbeAsync(sdrFile)]));
        var pq = MediaItem.FromRecording(new RecordingGroup([await client.ProbeAsync(pqFile)]));
        var editor = new TimelineEditor(MovieProject.CreateDefault(new ProjectSettings { Width = 320, Height = 180, FrameRate = FrameRate.Fps25 }));
        editor.AddMedia([sdr, pq]);
        var v1 = editor.Project.VideoTracks.First();
        editor.Append(v1, editor.CreateClip(sdr));
        editor.Append(v1, editor.CreateClip(pq));
        var first = editor.Project.VideoTracks.First().OrderedClips.First();
        editor.UpdateClip(first.Id, "Farbe", c =>
        {
            c.Grade.Exposure = 0.5;
            c.Grade.Lift.SetFromPolar(200, 0.2);
            c.Grade.Saturation = 130;
        });

        foreach (var preset in new[] { ExportPreset.Default, ExportPreset.Find("hevc-hdr10")! })
        {
            var output = Path.Combine(_temp, preset.Id + ".mp4");
            var job = ExportBuilder.Build(editor.Project, preset, output);
            await client.RunAsync(job.Arguments, job.DurationSeconds);
            var info = await client.ProbeAsync(output);
            Assert.Equal(4.0, info.Duration.TotalSeconds, 0);
            if (preset.HdrOutput)
            {
                Assert.Equal(ColorTransfer.HdrPq, info.MetadataTransfer);
            }
        }

        var raw = await client.RunForBytesAsync(ExportBuilder.BuildRawFrame(editor.Project, 10, 90).Arguments);
        Assert.Equal(160 * 90 * 3, raw.Length);
        var scopes = ScopeData.FromRgb24(raw, 160, 90);
        Assert.Equal(160 * 90, scopes.Histogram[3].Sum());
    }
}
