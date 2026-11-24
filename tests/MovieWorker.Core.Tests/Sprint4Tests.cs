using MovieWorker.Core.Camera;
using MovieWorker.Core.Media;
using MovieWorker.Core.Preview;
using MovieWorker.Core.Projects;
using MovieWorker.Core.Time;
using MovieWorker.Core.Timeline;

namespace MovieWorker.Core.Tests;

public sealed class Sprint4Tests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "mw-s4-" + Guid.NewGuid().ToString("N"));

    public Sprint4Tests() => Directory.CreateDirectory(_temp);

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    private static MediaItem Item(params MediaInfo[] parts) => MediaItem.FromRecording(new RecordingGroup(parts));

    [Fact]
    public void Proxy_DecisionPerCamera()
    {
        Assert.Equal(ProxyKind.Remux, ProxyPlanner.Decide(Item(TestMedia.Eos70D())));        // MOV + LPCM
        Assert.Equal(ProxyKind.Transcode, ProxyPlanner.Decide(Item(TestMedia.R6Clog())));    // HEVC 10 Bit 4:2:2
        Assert.Equal(ProxyKind.Transcode, ProxyPlanner.Decide(Item(FfprobeParser.Parse("MVI_1.MP4", TestMedia.R6PqJson))));
        var simpleMp4 = FfprobeParser.Parse("MVI_2.MP4", """
            { "streams": [ { "codec_type": "video", "codec_name": "h264", "width": 3840, "height": 2160, "pix_fmt": "yuv420p", "avg_frame_rate": "25/1" },
                           { "codec_type": "audio", "codec_name": "aac", "sample_rate": "48000", "channels": 2 } ], "format": { "duration": "5" } }
            """);
        Assert.Equal(ProxyKind.None, ProxyPlanner.Decide(Item(simpleMp4)));
    }

    [Fact]
    public void Proxy_PathIsStableAndUnique()
    {
        var a = Item(TestMedia.Eos70D("/card/MVI_0001.MOV", 100));
        var b = Item(TestMedia.Eos70D("/other/MVI_0001.MOV", 100));
        Assert.Equal(ProxyPlanner.ProxyPath(a, "/proxys"), ProxyPlanner.ProxyPath(a, "/proxys"));
        Assert.NotEqual(ProxyPlanner.ProxyPath(a, "/proxys"), ProxyPlanner.ProxyPath(b, "/proxys"));
        Assert.StartsWith(Path.Combine("/proxys", "MVI_0001_"), ProxyPlanner.ProxyPath(a, "/proxys"));
    }

    [Fact]
    public void Proxy_ArgumentsConcatSplitRecordingsAndToneMapHdr()
    {
        var split = Item(TestMedia.Eos70D("/c/MVI_0010.MOV", 4_294_000_000), TestMedia.Eos70D("/c/MVI_0011.MOV", 1000));
        var args = string.Join(' ', ProxyPlanner.Arguments(split, ProxyKind.Remux, "out.mp4"));
        Assert.Contains("concat=n=2:v=1:a=1[cv][ca]", args);
        Assert.Contains("-map [cv]", args);

        var hdr = Item(FfprobeParser.Parse("MVI_1.MP4", TestMedia.R6PqJson));
        var hdrArgs = string.Join(' ', ProxyPlanner.Arguments(hdr, ProxyKind.Transcode, "out.mp4"));
        Assert.Contains("tonemap=tonemap=hable", hdrArgs);
        Assert.Contains("scale=-2:540", hdrArgs);
        Assert.DoesNotContain("-map 0:a:0", hdrArgs); // PQ-Testclip hat keinen Ton

        var log = Item(TestMedia.R6Clog());
        Assert.Contains("lut3d=file='C\\:/LUTs/clog3.cube'", string.Join(' ', ProxyPlanner.Arguments(log, ProxyKind.Transcode, "o.mp4", "C:\\LUTs\\clog3.cube")));
    }

    [Fact]
    public void Playback_FindsTopVideoAudioAndChanges()
    {
        var editor = new TimelineEditor(MovieProject.CreateDefault(new ProjectSettings { FrameRate = FrameRate.Fps25 }));
        var a = Item(TestMedia.Eos70D("/c/MVI_0001.MOV"));
        var b = Item(TestMedia.R6Clog());
        editor.AddMedia([a, b]);
        var v1 = editor.Project.VideoTracks.First();
        editor.Append(v1, editor.CreateClip(a, 10, 20)); // 0–250
        var v2 = editor.AddTrack(TrackKind.Video);
        editor.Insert(v2, editor.CreateClip(b, 0, 2), 100); // 100–150 oben drüber

        var at50 = PlaybackPlanner.VideoAt(editor.Project, 50)!;
        Assert.Equal(a.Id, at50.Media!.Id);
        Assert.Equal(12.0, at50.SourceSeconds, 6);
        Assert.Equal(b.Id, PlaybackPlanner.VideoAt(editor.Project, 120)!.Media!.Id);
        Assert.Null(PlaybackPlanner.VideoAt(editor.Project, 300));

        editor.SetTrackMuted(editor.Project.Tracks.First(t => t.Name == "V2").Id, true);
        Assert.Equal(a.Id, PlaybackPlanner.VideoAt(editor.Project, 120)!.Media!.Id); // stumme Spur unsichtbar

        Assert.Equal(100, PlaybackPlanner.NextChange(editor.Project, 50));
        Assert.Equal(150, PlaybackPlanner.NextChange(editor.Project, 100));
        Assert.Equal(2, PlaybackPlanner.AudioAt(editor.Project, 120).Count);
    }

    [Fact]
    public void Playback_VolumeAndSplitPartLookup()
    {
        var editor = new TimelineEditor(MovieProject.CreateDefault(new ProjectSettings { FrameRate = FrameRate.Fps25 }));
        var split = Item(TestMedia.Eos70D("/c/MVI_0010.MOV", 4_294_000_000), TestMedia.Eos70D("/c/MVI_0011.MOV", 1000));
        editor.AddMedia([split]);
        editor.Append(editor.Project.VideoTracks.First(), editor.CreateClip(split, 110, 130));
        editor.UpdateClip(editor.Project.AllClips.Single().Id, "Lautstärke", c => c.VolumeDb = -20);

        var source = PlaybackPlanner.VideoAt(editor.Project, 250)!; // 10 s nach 110 s = 120 s → Teil 2, 0 s
        Assert.Equal("/c/MVI_0011.MOV", source.OriginalPath);
        Assert.Equal(0, source.OriginalSeconds, 6);
        Assert.Equal(0.1, source.Volume, 6);
    }

    [Fact]
    public async Task Proxy_RealFfmpegRunsForAllKinds()
    {
        if (FfmpegLocator.LocateDefault() is not { } paths)
        {
            return;
        }

        var client = new FfmpegClient(paths);
        async Task<MediaInfo> Make(string name, params string[] encode)
        {
            var file = Path.Combine(_temp, name);
            await client.RunAsync([.. encode, file], 0);
            return await client.ProbeAsync(file);
        }

        var mov1 = await Make("MVI_0001.MOV", "-y", "-v", "error", "-f", "lavfi", "-i", "testsrc2=s=320x180:r=25:d=1", "-f", "lavfi", "-i", "sine=d=1",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "pcm_s16le");
        var mov2 = await Make("MVI_0002.MOV", "-y", "-v", "error", "-f", "lavfi", "-i", "testsrc2=s=320x180:r=25:d=1", "-f", "lavfi", "-i", "sine=d=1",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "pcm_s16le");
        var hevc = await Make("MVI_0042.MP4", "-y", "-v", "error", "-f", "lavfi", "-i", "testsrc=s=640x360:r=25:d=1", "-c:v", "libx265",
            "-pix_fmt", "yuv422p10le", "-x265-params", "log-level=error", "-color_trc", "smpte2084", "-color_primaries", "bt2020", "-colorspace", "bt2020nc");

        foreach (var (item, kind) in new[] { (Item(mov1), ProxyKind.Remux), (Item(mov1, mov2), ProxyKind.Remux), (Item(hevc), ProxyKind.Transcode) })
        {
            var output = ProxyPlanner.ProxyPath(item, _temp);
            await client.RunAsync(ProxyPlanner.Arguments(item, kind, output), item.Duration.TotalSeconds);
            var info = await client.ProbeAsync(output);
            Assert.Equal("h264", info.Video!.Codec);
            Assert.Equal(item.Duration.TotalSeconds, info.Duration.TotalSeconds, 0);
        }
    }
}
