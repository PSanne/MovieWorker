using MovieWorker.Core.Camera;
using MovieWorker.Core.Media;
using MovieWorker.Core.Projects;
using MovieWorker.Core.Settings;
using MovieWorker.Core.Timeline;

namespace MovieWorker.Core.Tests;

public sealed class Sprint2Tests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "mw-tests-" + Guid.NewGuid().ToString("N"));

    public Sprint2Tests() => Directory.CreateDirectory(_temp);

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    [Fact]
    public void Settings_RoundTripAndRecentList()
    {
        var store = new SettingsStore(Path.Combine(_temp, "sub", "settings.json"));
        var settings = store.Load();
        for (var i = 0; i < 12; i++)
        {
            settings.AddRecentProject($"C:/p{i}.mvproj");
        }

        settings.AddRecentProject("C:/p5.mvproj");
        settings.FfmpegPath = "C:/ffmpeg";
        store.Save(settings);

        var loaded = store.Load();
        Assert.Equal(AppSettings.MaxRecentProjects, loaded.RecentProjects.Count);
        Assert.Equal("C:/p5.mvproj", loaded.RecentProjects[0]);
        Assert.Single(loaded.RecentProjects, p => p == "C:/p5.mvproj");
        Assert.Equal("C:/ffmpeg", loaded.FfmpegPath);
    }

    [Fact]
    public void Settings_CorruptFileGivesDefaults()
    {
        var path = Path.Combine(_temp, "settings.json");
        File.WriteAllText(path, "{ kaputt");
        Assert.True(new SettingsStore(path).Load().ImportCopyFiles);
    }

    [Fact]
    public void Locator_PrefersConfiguredThenBundledThenPath()
    {
        var exe = FfmpegLocator.ExecutableName("ffmpeg");
        var probe = FfmpegLocator.ExecutableName("ffprobe");
        var existing = new HashSet<string>
        {
            Path.Combine("/app", "ffmpeg", exe), Path.Combine("/app", "ffmpeg", probe),
            Path.Combine("/usr/bin", exe), Path.Combine("/usr/bin", probe),
            Path.Combine("/custom", exe), Path.Combine("/custom", probe),
        };
        bool Exists(string p) => existing.Contains(p);

        Assert.Equal(Path.Combine("/custom", exe), FfmpegLocator.Locate("/custom", "/app", "/usr/bin", Exists)!.Ffmpeg);
        Assert.Equal(Path.Combine("/app", "ffmpeg", exe), FfmpegLocator.Locate(null, "/app", "/usr/bin", Exists)!.Ffmpeg);
        Assert.Equal(Path.Combine("/usr/bin", probe), FfmpegLocator.Locate(null, "/nowhere", "/x" + Path.PathSeparator + "/usr/bin", Exists)!.Ffprobe);
        Assert.Null(FfmpegLocator.Locate(null, "/nowhere", null, Exists));
    }

    [Fact]
    public async Task CardImporter_CopiesVerifiesAndSkipsExisting()
    {
        var card = Path.Combine(_temp, "card", "DCIM", "100CANON");
        Directory.CreateDirectory(card);
        File.WriteAllBytes(Path.Combine(card, "MVI_0001.MOV"), new byte[100_000]);
        File.WriteAllBytes(Path.Combine(card, "MVI_0001.THM"), new byte[10]);
        File.WriteAllBytes(Path.Combine(card, "MVI_0002.MOV"), Enumerable.Range(0, 5000).Select(i => (byte)i).ToArray());
        var other = Path.Combine(_temp, "card", "DCIM", "101CANON");
        Directory.CreateDirectory(other);
        File.WriteAllBytes(Path.Combine(other, "MVI_0001.MOV"), new byte[7]);

        var dest = Path.Combine(_temp, "dest");
        var plan = CardImporter.Plan(CardScanner.Scan(Path.Combine(_temp, "card")), dest, new DateTime(2026, 5, 1), "EOS 70D");
        Assert.Equal(3, plan.Count);
        Assert.All(plan, p => Assert.StartsWith(Path.Combine(dest, "2026-05-01", "EOS 70D"), p.Destination));
        Assert.Contains(plan, p => Path.GetFileName(p.Destination) == "101CANON_MVI_0001.MOV");

        var reports = new List<CopyProgress>();
        var results = await CardImporter.ExecuteAsync(plan, verify: true, new SyncProgress<CopyProgress>(reports.Add));
        Assert.All(results, r => Assert.True(r.Verified));
        Assert.Contains(reports, r => r.Stage == "Prüfen");
        Assert.Equal(File.ReadAllBytes(plan[1].Source), File.ReadAllBytes(plan[1].Destination));

        var again = CardImporter.Plan(CardScanner.Scan(Path.Combine(_temp, "card")), dest, new DateTime(2026, 5, 1), "EOS 70D");
        Assert.All(again, p => Assert.True(p.AlreadyPresent));
    }

    [Fact]
    public void CardImporter_SanitizesFolderNames() =>
        Assert.Equal("EOS_R6_", CardImporter.SanitizeFolderName("EOS:R6?"));

    [Fact]
    public async Task MediaImport_GroupsAndReportsFailures()
    {
        var paths = new[] { "/c/MVI_0010.MOV", "/c/MVI_0011.MOV", "/c/broken.MOV", "/c/notes.txt", "/c/._MVI_0010.MOV", "/c/song.wav" };
        Task<MediaInfo> Probe(string path, CancellationToken _) => path switch
        {
            "/c/broken.MOV" => throw new FfmpegException("Invalid data", 1),
            "/c/song.wav" => Task.FromResult(FfprobeParser.Parse(path,
                """{ "streams": [ { "codec_type": "audio", "codec_name": "pcm_s16le", "sample_rate": "48000", "channels": 2 } ], "format": { "duration": "60" } }""")),
            "/c/MVI_0010.MOV" => Task.FromResult(TestMedia.Eos70D(path, 4_294_000_000)),
            _ => Task.FromResult(TestMedia.Eos70D(path, 1000)),
        };

        var result = await MediaImport.ProbeAsync(paths, Probe);
        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, i => i.Parts.Count == 2 && i.Name.StartsWith("MVI_0010.MOV"));
        Assert.Contains(result.Items, i => i.IsAudioOnly);
        Assert.Single(result.Failed);
    }

    [Fact]
    public void MediaPool_SurvivesUndoAndIsSaved()
    {
        var editor = new TimelineEditor();
        var media = MediaItem.FromRecording(new RecordingGroup([TestMedia.R6Clog()]));
        media.CameraOverride = CanonCamera.EosR6MarkII;
        editor.AddMedia([media]);
        Assert.Empty(editor.AddMedia([MediaItem.FromRecording(new RecordingGroup([TestMedia.R6Clog()]))])); // doppelt

        editor.Append(editor.Project.VideoTracks.First(), editor.CreateClip(media));
        var other = MediaItem.FromRecording(new RecordingGroup([TestMedia.Eos70D()]));
        editor.AddMedia([other]);
        editor.Undo();
        Assert.Equal(2, editor.Project.Media.Count);
        Assert.Empty(editor.Project.AllClips);

        editor.Redo();
        Assert.False(editor.RemoveMedia(media.Id)); // wird verwendet
        Assert.True(editor.RemoveMedia(other.Id));

        var json = ProjectSerializer.Serialize(editor.Project);
        var loaded = ProjectSerializer.Deserialize(json);
        var item = Assert.Single(loaded.Media);
        Assert.Equal(CanonCamera.EosR6MarkII, item.CameraOverride);
        Assert.Equal(media.First.Video!.FrameRate, item.First.Video!.FrameRate);
        Assert.Equal(media.First.Video.CodecLabel, item.First.Video.CodecLabel);
        Assert.Equal(ColorTransfer.CanonLog3, item.Detection.Transfer);
        Assert.Equal(media.Id, loaded.AllClips.Single().MediaId);
    }

    [Fact]
    public void ManualCameraOverridesDetection()
    {
        var detection = CanonCameraDetector.Detect(TestMedia.Eos70D(), CanonCamera.EosR6MarkII);
        Assert.Equal(CanonCamera.EosR6MarkII, detection.Camera);
        Assert.Equal(DetectionConfidence.Certain, detection.Confidence);
    }

    [Fact]
    public async Task FfmpegClient_ProbesRealFileWhenAvailable()
    {
        var paths = FfmpegLocator.LocateDefault();
        if (paths is null)
        {
            return; // ffmpeg nicht installiert – Test entfällt.
        }

        var file = Path.Combine(_temp, "clip.mov");
        var client = new FfmpegClient(paths);
        var progress = new List<double>();
        await client.RunAsync(["-y", "-v", "error", "-f", "lavfi", "-i", "testsrc2=s=320x180:r=25:d=2", "-f", "lavfi", "-i", "sine=d=2",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "pcm_s16le", "-progress", "pipe:1", file], 2, new SyncProgress<double>(progress.Add));
        Assert.Equal(1, progress[^1]);

        var info = await client.ProbeAsync(file);
        Assert.Equal(320, info.Video!.Width);
        Assert.True(info.Audio!.IsLinearPcm);

        var png = await client.RunForBytesAsync(Export.FfmpegArgs.Thumbnail(file, 1, 90));
        Assert.Equal(0x89, png[0]);

        await Assert.ThrowsAsync<FfmpegException>(() => client.ProbeAsync(Path.Combine(_temp, "missing.mov")));
    }

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
