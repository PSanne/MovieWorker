using MovieWorker.Core.Camera;
using MovieWorker.Core.Export;
using MovieWorker.Core.Media;
using MovieWorker.Core.Projects;
using MovieWorker.Core.Time;
using MovieWorker.Core.Timeline;

namespace MovieWorker.Core.Tests;

public sealed class Sprint16Tests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "mw-s16-" + Guid.NewGuid().ToString("N"));

    public Sprint16Tests() => Directory.CreateDirectory(_temp);

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    private const string Encoders = """
         V....D libx264              libx264 H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10 (codec h264)
         V....D h264_nvenc           NVIDIA NVENC H.264 encoder (codec h264)
         V..... h264_qsv             H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10 (Intel Quick Sync Video acceleration) (codec h264)
         V....D hevc_nvenc           NVIDIA NVENC hevc encoder (codec hevc)
         V....D hevc_amf             AMD AMF HEVC encoder (codec hevc)
        """;

    [Fact]
    public void Hardware_ParseListedEncoders()
    {
        var listed = HardwareEncoders.ParseListed(Encoders);
        Assert.Equal(["h264_nvenc", "hevc_nvenc", "h264_qsv", "hevc_amf"], listed.Select(e => e.Codec));
        Assert.Equal("NVIDIA NVENC (H.264)", listed[0].ToString());
        Assert.Empty(HardwareEncoders.ParseListed("V....D libx264 libx264"));
        Assert.Contains("h264_qsv", HardwareEncoders.ProbeArguments(listed[2]));
    }

    [Fact]
    public void Hardware_PresetsSwitchToGpuAndBack()
    {
        HardwareEncoder[] nvidia = [new(HardwareVendor.Nvidia, false), new(HardwareVendor.Nvidia, true)];
        var web = ExportPreset.Find("web-h264-1080")!;
        var gpu = HardwareEncoders.Apply(web, nvidia);
        Assert.True(gpu.UsesHardware);
        Assert.Equal("web-h264-1080-gpu", gpu.Id);
        Assert.Contains("h264_nvenc", gpu.VideoArguments);
        Assert.DoesNotContain("libx264", gpu.VideoArguments);
        Assert.Contains("+faststart", gpu.VideoArguments);
        Assert.Equal(web.MaxHeight, gpu.MaxHeight);
        Assert.Equal(web, gpu.Software);
        Assert.Same(gpu, HardwareEncoders.Apply(gpu, nvidia));   // nicht doppelt umstellen

        var social = HardwareEncoders.Apply(ExportPreset.Find("social-vertical")!.WithReframe(ReframeMode.Fill), nvidia);
        Assert.Contains("-maxrate", social.VideoArguments);
        Assert.Equal(ReframeMode.Fill, social.Software.Reframe);

        var archive = HardwareEncoders.Apply(ExportPreset.Find("hevc-10bit")!, nvidia);
        Assert.Contains("hevc_nvenc", archive.VideoArguments);
        Assert.Contains("main10", archive.VideoArguments);
        Assert.Equal("p010le", archive.PixelFormat);
        Assert.Contains("hvc1", archive.VideoArguments);

        Assert.False(HardwareEncoders.Apply(ExportPreset.Find("prores-hq")!, nvidia).UsesHardware);
        Assert.False(HardwareEncoders.Apply(ExportPreset.Find("hevc-hdr10")!, nvidia).UsesHardware);
        Assert.False(HardwareEncoders.Apply(ExportPreset.Find("audio-wav")!, nvidia).UsesHardware);
        Assert.False(HardwareEncoders.Apply(ExportPreset.Find("web-h265-4k")!, [new HardwareEncoder(HardwareVendor.Intel, false)]).UsesHardware);

        var qsv = HardwareEncoders.Apply(web, [new HardwareEncoder(HardwareVendor.Intel, false)]);
        Assert.Contains("-global_quality", qsv.VideoArguments);
        var amf = HardwareEncoders.Apply(web, [new HardwareEncoder(HardwareVendor.Amd, false)]);
        Assert.Contains("h264_amf", amf.VideoArguments);

        var media = MediaItem.FromRecording(new RecordingGroup([TestMedia.Eos70D()]));
        var editor = new TimelineEditor(MovieProject.CreateDefault(new ProjectSettings { FrameRate = FrameRate.Fps25 }));
        editor.Append(editor.Project.VideoTracks.First(), editor.CreateClip(media, 0, 2));
        var args = ExportBuilder.Build(editor.Project, gpu, "out.mp4").Arguments;
        Assert.Contains("h264_nvenc", args);
        Assert.Equal("yuv420p", args[args.ToList().IndexOf("-pix_fmt") + 1]);
    }

    [Fact]
    public async Task RealFfmpeg_DetectionNeverThrows()
    {
        if (FfmpegLocator.LocateDefault() is not { } paths)
        {
            return;
        }

        var found = await HardwareEncoders.DetectAsync(new FfmpegClient(paths));
        Assert.All(found, e => Assert.Contains(e.Vendor, Enum.GetValues<HardwareVendor>()));
    }

    [Fact]
    public async Task Autosave_SaveFindRecoverDiscard()
    {
        var folder = Path.Combine(_temp, "Autosave");
        var crashed = new AutosaveStore(folder);
        var project = MovieProject.CreateDefault();
        project.Master.LoudnessTarget = -14;
        await crashed.SaveAsync(project, @"C:\Filme\Hochzeit.mvproj", "Hochzeit");
        Assert.True(crashed.HasOwn);
        Assert.Empty(crashed.FindRecoverable(_ => false));   // eigene Sitzung wird nicht angeboten

        var next = new AutosaveStore(folder);
        var entries = next.FindRecoverable(_ => false);
        var entry = Assert.Single(entries);
        Assert.Equal("Hochzeit", entry.Title);
        Assert.Equal(@"C:\Filme\Hochzeit.mvproj", entry.OriginalPath);
        Assert.Equal(-14, (await next.LoadAsync(entry)).Master.LoudnessTarget);

        Assert.DoesNotContain(next.FindRecoverable(_ => true), e => e.ProcessId != Environment.ProcessId);
        next.Discard(entry);
        Assert.Empty(next.FindRecoverable(_ => false));
        Assert.False(crashed.HasOwn);

        await next.SaveAsync(project, null, "Neu");
        next.ClearOwn();
        Assert.False(next.HasOwn);
        Assert.Empty(Directory.GetFiles(folder));

        File.WriteAllText(Path.Combine(folder, "kaputt.json"), "{ nicht json");
        Assert.Empty(new AutosaveStore(folder).FindRecoverable(_ => false));
    }

    [Fact]
    public async Task Save_KeepsBackupOfPreviousVersion()
    {
        var path = Path.Combine(_temp, "Film.mvproj");
        var project = MovieProject.CreateDefault();
        await ProjectSerializer.SaveAsync(project, path, keepBackup: true);
        Assert.False(File.Exists(path + ".bak"));
        project.Master.VolumeDb = -3;
        await ProjectSerializer.SaveAsync(project, path, keepBackup: true);
        Assert.Equal(0, (await ProjectSerializer.LoadAsync(path + ".bak")).Master.VolumeDb);
        Assert.Equal(-3, (await ProjectSerializer.LoadAsync(path)).Master.VolumeDb);
    }
}
