using MovieWorker.Core.Audio;
using MovieWorker.Core.Camera;
using MovieWorker.Core.Export;
using MovieWorker.Core.Media;
using MovieWorker.Core.Projects;
using MovieWorker.Core.Time;
using MovieWorker.Core.Timeline;

namespace MovieWorker.Core.Tests;

public sealed class Sprint14Tests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "mw-s14-" + Guid.NewGuid().ToString("N"));

    public Sprint14Tests() => Directory.CreateDirectory(_temp);

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    private static TimelineEditor MakeProject()
    {
        var media = MediaItem.FromRecording(new RecordingGroup([TestMedia.Eos70D()]));
        var editor = new TimelineEditor(MovieProject.CreateDefault(new ProjectSettings { FrameRate = FrameRate.Fps25 }));
        editor.AddMedia([media]);
        editor.Append(editor.Project.VideoTracks.First(), editor.CreateClip(media, 0, 8));
        editor.Append(editor.Project.AudioTracks.First(), editor.CreateClip(media, 10, 18));
        return editor;
    }

    [Fact]
    public void Panner_StereoNeutralNeedsNoFilter()
    {
        Assert.Null(SurroundPanner.Filter(new TrackAudio(), surround: false));
        Assert.Equal("pan=stereo|c0=1*c0|c1=0.5*c1", SurroundPanner.Filter(new TrackAudio { Pan = -0.5 }, surround: false));
    }

    [Fact]
    public void Panner_SurroundPositions()
    {
        Assert.Equal("pan=5.1|FL=1*c0|FR=1*c1|FC=0*c0|LFE=0*c0|BL=0*c0|BR=0*c0", SurroundPanner.Filter(new TrackAudio(), surround: true));
        Assert.Equal("pan=5.1|FL=0*c0|FR=0*c0|FC=0.5*c0+0.5*c1|LFE=0*c0|BL=0*c0|BR=0*c0",
            SurroundPanner.Filter(new TrackAudio { Center = 1 }, surround: true));
        Assert.Equal("pan=5.1|FL=0*c0|FR=0*c0|FC=0*c0|LFE=0.25*c0+0.25*c1|BL=1*c0|BR=1*c1",
            SurroundPanner.Filter(new TrackAudio { Depth = 1, Lfe = 0.5 }, surround: true));

        // Stereo-Downmix nach ITU: Center und hinten mit −3 dB, Subwoofer entfällt
        Assert.Equal("pan=stereo|c0=0.3536*c0+0.3536*c1|c1=0.3536*c0+0.3536*c1", SurroundPanner.Filter(new TrackAudio { Center = 1 }, surround: false));
        Assert.Equal("pan=stereo|c0=0.7071*c0|c1=0.7071*c1", SurroundPanner.Filter(new TrackAudio { Depth = 1, Lfe = 1 }, surround: false));

        var half = SurroundPanner.Gains51(new TrackAudio { Depth = 0.5 });
        Assert.Equal(half[0, 0], half[4, 0], 6);   // halb vorne, halb hinten (gleiche Leistung)
        Assert.Equal(1, half[0, 0] * half[0, 0] + half[4, 0] * half[4, 0], 6);
        Assert.Contains("hinten", SurroundPanner.Describe(new TrackAudio { Depth = 1 }));
        Assert.Contains("Center 100", SurroundPanner.Describe(new TrackAudio { Center = 1 }));
    }

    [Fact]
    public void Export_SurroundGraphAndDownmix()
    {
        var editor = MakeProject();
        var project = editor.Project;
        project.VideoTracks.First().Audio.Center = 1;
        project.AudioTracks.First().Audio.Depth = 0.7;

        var surround = string.Join(' ', ExportBuilder.Build(project, ExportPreset.Find("master-51")!, "out.mp4").Arguments);
        Assert.Contains("[acam]pan=5.1|FL=0*c0|FR=0*c0|FC=0.5*c0+0.5*c1", surround);
        Assert.Equal(2, surround.Split("pan=5.1").Length - 1);
        Assert.Contains("channelsplit=channel_layout=5.1", surround);
        Assert.Contains("[sLFE]lowpass=f=120", surround);
        Assert.Contains("join=inputs=6:channel_layout=5.1[aout]", surround);
        Assert.Contains("640k", surround);

        var stereo = string.Join(' ', ExportBuilder.Build(project, ExportPreset.Default, "out.mp4").Arguments);
        Assert.DoesNotContain("5.1", stereo);
        Assert.Contains("[acam]pan=stereo|c0=0.3536*c0+0.3536*c1", stereo);

        Assert.Contains(ExportPreflight.Check(project, ExportPreset.Default, fileExists: _ => true), i => i.Message.Contains("heruntergemischt"));
        project.VideoTracks.First().Audio.Center = 0;
        project.AudioTracks.First().Audio.Depth = 0;
        Assert.Contains(ExportPreflight.Check(project, ExportPreset.Find("master-51")!, fileExists: _ => true), i => i.Message.Contains("Center"));
    }

    [Fact]
    public void Devices_ParseOldAndNewFfmpegOutput()
    {
        const string modern = """
            [dshow @ 000001] "HD Webcam" (video)
            [dshow @ 000001]   Alternative name "@device_pnp_\\?\usb#vid"
            [dshow @ 000001] "Mikrofon (Rode NT-USB)" (audio)
            [dshow @ 000001]   Alternative name "@device_cm_{33D9A762}\wave_{1}"
            [dshow @ 000001] "Line 1 (Virtual Audio Cable)" (audio)
            dummy: Immediate exit requested
            """;
        var devices = RecordingDevice.ParseDirectShowList(modern);
        Assert.Equal(["Mikrofon (Rode NT-USB)", "Line 1 (Virtual Audio Cable)"], devices.Select(d => d.Name));
        Assert.Equal("audio=Mikrofon (Rode NT-USB)", devices[0].Input);

        const string legacy = """
            [dshow @ 0x1] DirectShow video devices (some may be both video and audio devices)
            [dshow @ 0x1]  "Integrated Camera"
            [dshow @ 0x1]     Alternative name "@device_pnp_x"
            [dshow @ 0x1] DirectShow audio devices
            [dshow @ 0x1]  "Mikrofonarray (Realtek(R) Audio)"
            [dshow @ 0x1]     Alternative name "@device_cm_y"
            """;
        Assert.Equal(["Mikrofonarray (Realtek(R) Audio)"], RecordingDevice.ParseDirectShowList(legacy).Select(d => d.Name));
        Assert.Empty(RecordingDevice.ParseDirectShowList("Unknown input format: 'dshow'"));

        var args = devices[0].Arguments("C:/x/take.wav");
        Assert.Equal(["-f", "dshow", "-audio_buffer_size", "50", "-i", "audio=Mikrofon (Rode NT-USB)"], args.Skip(4).Take(6));
        Assert.Equal("C:/x/take.wav", args[^1]);
        Assert.Contains("pcm_s24le", args);
    }

    [Fact]
    public void Adr_CueTakesAndOriginalAudio()
    {
        var editor = MakeProject();
        var v1 = editor.Project.VideoTracks.First();
        var adrTrack = editor.AddTrack(TrackKind.Audio);
        var cue = editor.AddAdrCue(50, 100, "Szene 3");
        Assert.Same(cue, editor.AddAdrCue(50, 100));
        Assert.Equal(50, cue.LengthFrames);
        editor.UpdateAdrCue(cue.Id, "Dialog", c => c.Dialog = "Wo warst du gestern?");

        var take1 = new AdrTake { Number = 1, Path = "take1.wav", OffsetSeconds = 1.5 };
        var take2 = new AdrTake { Number = 2, Path = "take2.wav", OffsetSeconds = 1.4 };
        Assert.True(editor.AddAdrTake(cue.Id, take1));
        Assert.True(editor.AddAdrTake(cue.Id, take2));
        Assert.Equal(3, editor.Project.AdrCues[0].NextTakeNumber);

        var media = editor.Project.Media[0];
        var clip = editor.CreateClip(media, take2.OffsetSeconds, take2.OffsetSeconds + 2);
        Assert.True(editor.UseAdrTake(cue.Id, take2.Id, adrTrack.Id, clip));
        var placed = editor.Project.Tracks.First(t => t.Id == adrTrack.Id).Clips.Single();
        Assert.Equal(50, placed.StartFrame);
        Assert.Equal(50, placed.LengthFrames);
        Assert.Equal(take2.Id, editor.Project.AdrCues[0].UsedTakeId);

        Assert.True(editor.MuteOriginalAudio(50, 100));
        var parts = editor.Project.VideoTracks.First().OrderedClips.ToList();
        Assert.Equal(3, parts.Count);
        Assert.Equal([false, true, false], parts.Select(p => p.AudioMuted));
        Assert.Equal(50, parts[1].StartFrame);
        Assert.Equal(100, parts[1].EndFrame);
        Assert.Equal(2.0, parts[1].SourceIn, 6);
        Assert.False(editor.MuteOriginalAudio(60, 90));   // schon stumm

        var back = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(editor.Project));
        Assert.Equal("Wo warst du gestern?", back.AdrCues[0].Dialog);
        Assert.Equal(2, back.AdrCues[0].Takes.Count);

        editor.Undo();
        Assert.Single(editor.Project.VideoTracks.First().Clips);
        Assert.True(editor.RemoveAdrCue(cue.Id));
        Assert.Empty(editor.Project.AdrCues);
        Assert.Equal(v1.Id, editor.Project.VideoTracks.First().Id);
    }

    [Fact]
    public async Task RealFfmpeg_RecordTestToneAndSurroundExport()
    {
        if (FfmpegLocator.LocateDefault() is not { } paths)
        {
            return;
        }

        var client = new FfmpegClient(paths);
        var takePath = Path.Combine(_temp, "take.wav");
        await using (var recording = client.StartRecording(RecordingDevice.TestTone, takePath))
        {
            await recording.WaitUntilStartedAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(1500);
            Assert.True(recording.Seconds > 0.5, $"Aufgenommen: {recording.Seconds}");
            await recording.StopAsync();
        }

        var take = await client.ProbeAsync(takePath);
        Assert.True(take.HasAudio);
        Assert.InRange(take.Duration.TotalSeconds, 1, 20);   // langsame Rechner brauchen fürs Beenden etwas länger

        var video = Path.Combine(_temp, "MVI_0001.MOV");
        await client.RunAsync(["-y", "-v", "error", "-f", "lavfi", "-i", "testsrc2=s=320x180:r=25:d=4", "-f", "lavfi", "-i", "sine=f=440:d=4",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "pcm_s16le", video], 0);
        var editor = new TimelineEditor(MovieProject.CreateDefault(new ProjectSettings { Width = 320, Height = 180, FrameRate = FrameRate.Fps25 }));
        var videoMedia = MediaItem.FromRecording(new RecordingGroup([await client.ProbeAsync(video)]));
        var takeMedia = MediaItem.FromRecording(new RecordingGroup([take]));
        editor.AddMedia([videoMedia, takeMedia]);
        editor.Append(editor.Project.VideoTracks.First(), editor.CreateClip(videoMedia, 0, 4));
        var cue = editor.AddAdrCue(25, 50);
        var adrTake = new AdrTake { Number = 1, Path = takePath, OffsetSeconds = 0.2, MediaId = takeMedia.Id };
        editor.AddAdrTake(cue.Id, adrTake);
        var voice = editor.AddTrack(TrackKind.Audio);
        editor.UseAdrTake(cue.Id, adrTake.Id, voice.Id, editor.CreateClip(takeMedia, 0.2, 1.2));
        editor.MuteOriginalAudio(25, 50);
        editor.SetTrackAudio(editor.Project.VideoTracks.First().Id, "Raum", t => t.Audio.Center = 1);
        editor.SetTrackAudio(voice.Id, "Raum", t => { t.Audio.Depth = 1; t.Audio.Lfe = 0.3; });
        editor.SetMaster("Lautheit", m => m.LoudnessTarget = -16);

        var measure = ExportBuilder.BuildLoudnessMeasurement(editor.Project, surround: true);
        var loudness = LoudnessMeasurement.Parse(await client.RunForStderrAsync(measure.Arguments));
        Assert.NotNull(loudness);

        var wav = Path.Combine(_temp, "mix51.wav");
        var job = ExportBuilder.Build(editor.Project, ExportPreset.Find("audio-wav-51")!, wav, loudness: loudness);
        await client.RunAsync(job.Arguments, job.DurationSeconds);
        var info = await client.ProbeAsync(wav);
        Assert.Equal(6, info.Audio!.Channels);

        var mp4 = Path.Combine(_temp, "master51.mp4");
        job = ExportBuilder.Build(editor.Project, ExportPreset.Find("master-51")!, mp4);
        await client.RunAsync(job.Arguments, job.DurationSeconds);
        info = await client.ProbeAsync(mp4);
        Assert.Equal(6, info.Audio!.Channels);
        Assert.NotNull(info.Video);

        var stereo = Path.Combine(_temp, "downmix.wav");
        job = ExportBuilder.Build(editor.Project, ExportPreset.Find("audio-wav")!, stereo);
        await client.RunAsync(job.Arguments, job.DurationSeconds);
        Assert.Equal(2, (await client.ProbeAsync(stereo)).Audio!.Channels);
    }
}
