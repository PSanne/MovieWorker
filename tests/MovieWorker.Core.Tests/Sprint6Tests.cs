using MovieWorker.Core.Camera;
using MovieWorker.Core.Media;
using MovieWorker.Core.Projects;
using MovieWorker.Core.Time;
using MovieWorker.Core.Timeline;

namespace MovieWorker.Core.Tests;

public class Sprint6Tests
{
    private static MediaItem Item(MediaInfo info) => MediaItem.FromRecording(new RecordingGroup([info]));

    [Fact]
    public void Fft_RoundTrip()
    {
        var data = Enumerable.Range(0, 16).Select(i => new System.Numerics.Complex(Math.Sin(i), 0)).ToArray();
        var copy = data.ToArray();
        Fft.Transform(data, inverse: false);
        Fft.Transform(data, inverse: true);
        for (var i = 0; i < 16; i++)
        {
            Assert.Equal(copy[i].Real, data[i].Real, 9);
        }

        Assert.Equal(1024, Fft.NextPowerOfTwo(1000));
    }

    [Theory]
    [InlineData(1.234)]
    [InlineData(-0.75)]
    [InlineData(0)]
    public void AudioSync_FindsOffsetOfRandomEvents(double offset)
    {
        var random = new Random(4711);
        var duration = 20.0;
        var events = Enumerable.Range(0, 40).Select(_ => random.NextDouble() * duration).ToList();
        float[] Envelope(double shift, double noise, int seed)
        {
            var r = new Random(seed);
            var env = new float[(int)(duration * AudioSync.EnvelopeRate)];
            for (var i = 0; i < env.Length; i++)
            {
                var t = i / AudioSync.EnvelopeRate - shift;
                var value = events.Sum(e => t >= e && t < e + 0.08 ? Math.Exp(-(t - e) * 30) : 0);
                env[i] = (float)(value + r.NextDouble() * noise);
            }

            return env;
        }

        var camera = Envelope(0, 0.05, 1);
        var external = Envelope(offset, 0.02, 2);
        var result = AudioSync.FindOffset(camera, external);
        Assert.InRange(result.OffsetSeconds, offset - 0.006, offset + 0.006); // Auflösung 5 ms
        Assert.True(result.IsReliable, $"Sicherheit {result.Confidence}");
    }

    [Fact]
    public void AudioSync_EnvelopeFromPcm()
    {
        var pcm = new byte[AudioSync.WindowSamples * 2 * 3];
        for (var i = AudioSync.WindowSamples; i < AudioSync.WindowSamples * 2; i++)
        {
            pcm[2 * i + 1] = 0x40; // 16384 → 0,5
        }

        var envelope = AudioSync.EnvelopeFromPcm16(pcm);
        Assert.Equal(3, envelope.Length);
        Assert.Equal(0, envelope[0], 3);
        Assert.Equal(0.5, envelope[1], 3);
    }

    [Fact]
    public void SyncedAudio_PlacementHandlesLateStartAndEarlyEnd()
    {
        var rate = FrameRate.Fps25;
        var clip = new TimelineClip { SourceIn = 2, LengthFrames = 250 }; // 10 s ab Sekunde 2
        var normal = SyncedAudioPlacement.For(clip, 1.5, 60, rate)!;
        Assert.Equal(0, normal.StartOffsetFrames);
        Assert.Equal(3.5, normal.SourceIn, 6);
        Assert.Equal(250, normal.LengthFrames);

        var lateRecorder = SyncedAudioPlacement.For(clip, -4, 60, rate)!; // Rekorder startet 2 s nach Clip-In
        Assert.Equal(50, lateRecorder.StartOffsetFrames);
        Assert.Equal(0, lateRecorder.SourceIn);
        Assert.Equal(200, lateRecorder.LengthFrames);

        var shortRecorder = SyncedAudioPlacement.For(clip, 0, 6, rate)!;   // Rekorder hat nur 6 s
        Assert.Equal(100, shortRecorder.LengthFrames);

        Assert.Null(SyncedAudioPlacement.For(clip, 100, 60, rate));
    }

    [Fact]
    public void InsertWithAudio_PlacesBothInOneUndoStep()
    {
        var editor = new TimelineEditor(MovieProject.CreateDefault(new ProjectSettings { FrameRate = FrameRate.Fps25 }));
        var video = Item(TestMedia.Eos70D());
        var song = Item(FfprobeParser.Parse("/c/ZOOM0001.WAV",
            """{ "streams": [ { "codec_type": "audio", "codec_name": "pcm_s24le", "sample_rate": "48000", "channels": 2 } ], "format": { "duration": "300" } }"""));
        editor.AddMedia([video, song]);
        var clip = editor.CreateClip(video, 0, 10);
        var audio = editor.CreateClip(song, 1, 11);
        Assert.True(editor.InsertWithAudio(editor.Project.VideoTracks.First(), clip, null, audio));
        Assert.Equal("A2 Ton", editor.Project.FindClip(audio.Id)!.Value.Track.Name);
        editor.Undo();
        Assert.Empty(editor.Project.AllClips);
    }

    [Fact]
    public void Filter_SearchRatingKindBinAndSort()
    {
        var a = Item(TestMedia.Eos70D("/c/MVI_0001.MOV"));
        a.Rating = 4;
        a.Keywords = MediaFilter.ParseKeywords("Hochzeit, Kirche;  Ringe, kirche");
        a.Scene = "12";
        var b = Item(TestMedia.R6Clog());
        b.Notes = "Drohne über dem See";
        var bin = new MediaBin { Name = "B-Roll" };
        b.BinId = bin.Id;

        Assert.Equal(["Hochzeit", "Kirche", "Ringe"], a.Keywords);
        Assert.True(new MediaFilter("kirche ringe").Matches(a));
        Assert.False(new MediaFilter("kirche see").Matches(a));
        Assert.True(new MediaFilter("see").Matches(b));
        Assert.True(new MediaFilter(MinRating: 3).Matches(a));
        Assert.False(new MediaFilter(MinRating: 3).Matches(b));
        Assert.True(new MediaFilter(BinId: bin.Id).Matches(b));
        Assert.True(new MediaFilter(OnlyUnsorted: true).Matches(a));
        Assert.False(new MediaFilter(Kind: MediaKindFilter.Audio).Matches(a));
        Assert.True(new MediaFilter(Camera: CanonCamera.EosR6MarkII).Matches(b));
        Assert.Equal(a, MediaFilter.Sort([b, a], MediaSort.Rating).First());
    }

    [Fact]
    public void MetadataAndBinsAreSavedButNotUndone()
    {
        var editor = new TimelineEditor();
        var item = Item(TestMedia.Eos70D());
        item.Rating = 5;
        item.SyncedAudio = new SyncedAudio { AudioMediaId = Guid.NewGuid(), OffsetSeconds = 0.75, Confidence = 0.8 };
        editor.AddMedia([item]);
        editor.Project.Bins.Add(new MediaBin { Name = "Interviews" });
        editor.Append(editor.Project.VideoTracks.First(), editor.CreateClip(item));
        editor.Undo();
        Assert.Single(editor.Project.Bins);

        var loaded = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(editor.Project));
        Assert.Equal("Interviews", loaded.Bins.Single().Name);
        Assert.Equal(5, loaded.Media.Single().Rating);
        Assert.Equal(0.75, loaded.Media.Single().SyncedAudio!.OffsetSeconds);
    }
}

public sealed class Sprint6FfmpegTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "mw-s6-" + Guid.NewGuid().ToString("N"));

    public Sprint6FfmpegTests() => Directory.CreateDirectory(_temp);

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    [Fact]
    public async Task RealRecorderAndCameraSoundAreSynced()
    {
        if (FfmpegLocator.LocateDefault() is not { } paths)
        {
            return;
        }

        var client = new FfmpegClient(paths);
        var zoom = Path.Combine(_temp, "ZOOM0001.WAV");
        await client.RunAsync(["-y", "-v", "error", "-f", "lavfi", "-i",
            "anoisesrc=d=12:c=pink:r=48000:seed=42,volume='0.15+0.85*abs(sin(2.3*t)*sin(0.9*t+1.3))':eval=frame", "-ac", "2", "-c:a", "pcm_s24le", zoom], 0);
        var camera = Path.Combine(_temp, "MVI_0002.MOV");
        await client.RunAsync(["-y", "-v", "error", "-f", "lavfi", "-i", "testsrc2=s=320x180:r=25:d=6", "-i", zoom,
            "-filter_complex", "[1:a]atrim=start=0.75:duration=6,asetpts=PTS-STARTPTS,volume=0.5[a]", "-map", "0:v", "-map", "[a]",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "pcm_s16le", camera], 0);

        var cameraEnvelope = AudioSync.EnvelopeFromPcm16(await client.RunForBytesAsync(AudioSync.FfmpegArguments(camera)));
        var zoomEnvelope = AudioSync.EnvelopeFromPcm16(await client.RunForBytesAsync(AudioSync.FfmpegArguments(zoom)));
        var result = AudioSync.FindOffset(cameraEnvelope, zoomEnvelope);
        Assert.InRange(result.OffsetSeconds, 0.74, 0.76);
        Assert.True(result.IsReliable, $"Sicherheit {result.Confidence}");
    }
}
