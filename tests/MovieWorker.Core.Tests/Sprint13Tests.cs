using MovieWorker.Core.Audio;
using MovieWorker.Core.Camera;
using MovieWorker.Core.Export;
using MovieWorker.Core.Media;
using MovieWorker.Core.Projects;
using MovieWorker.Core.Time;
using MovieWorker.Core.Timeline;

namespace MovieWorker.Core.Tests;

public sealed class Sprint13Tests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "mw-s13-" + Guid.NewGuid().ToString("N"));

    public Sprint13Tests() => Directory.CreateDirectory(_temp);

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    private static (TimelineEditor Editor, TimelineTrack Music) MakeProject()
    {
        var media = MediaItem.FromRecording(new RecordingGroup([TestMedia.Eos70D()]));
        var editor = new TimelineEditor(MovieProject.CreateDefault(new ProjectSettings { FrameRate = FrameRate.Fps25 }));
        editor.AddMedia([media]);
        editor.Append(editor.Project.VideoTracks.First(), editor.CreateClip(media, 0, 4));
        editor.Append(editor.Project.AudioTracks.First(), editor.CreateClip(media, 10, 14));
        return (editor, editor.Project.AudioTracks.First());
    }

    [Fact]
    public void ChannelStrip_NeutralHasNoFilters()
    {
        Assert.Empty(new TrackAudio().Filters());
    }

    [Fact]
    public void ChannelStrip_BuildsEffectChainInOrder()
    {
        var strip = new TrackAudio
        {
            HighPassHz = 80, NoiseReduction = 0.5, EqLow = -3, EqMid = 2, EqHigh = 4,
            Compressor = true, CompressorThresholdDb = -24, CompressorRatio = 4, Reverb = 0.5, Pan = -0.5,
        };
        var filters = strip.Filters();
        Assert.Equal(8, filters.Count);
        Assert.Equal("highpass=f=80", filters[0]);
        Assert.Equal("afftdn=nr=18:nf=-40", filters[1]);
        Assert.Equal("bass=g=-3:f=120", filters[2]);
        Assert.Equal("equalizer=f=1000:t=q:w=1:g=2", filters[3]);
        Assert.Equal("treble=g=4:f=8000", filters[4]);
        Assert.Equal("acompressor=threshold=-24dB:ratio=4:attack=10:release=200:makeup=4", filters[5]);
        Assert.StartsWith("aecho=0.9:0.9:", filters[6]);
        Assert.Equal("pan=stereo|c0=1*c0|c1=0.5*c1", filters[7]);

        var copy = strip.Clone();
        copy.Pan = 1;
        Assert.Equal(-0.5, strip.Pan);
    }

    [Fact]
    public void Solo_SilencesOtherTracks()
    {
        var (editor, music) = MakeProject();
        var project = editor.Project;
        var v1 = project.VideoTracks.First();
        Assert.True(project.IsAudible(v1));
        Assert.True(project.IsAudible(music));

        music.Audio.Solo = true;
        Assert.False(project.IsAudible(v1));
        Assert.True(project.IsAudible(music));

        music.Muted = true;
        Assert.False(project.IsAudible(music));
    }

    [Fact]
    public void Loudness_ParsesFfmpegJson()
    {
        const string stderr = """
            [Parsed_loudnorm_3 @ 0x55d] 
            {
            	"input_i" : "-27.61",
            	"input_tp" : "-9.02",
            	"input_lra" : "4.30",
            	"input_thresh" : "-38.01",
            	"output_i" : "-14.10",
            	"output_tp" : "-1.00",
            	"output_lra" : "3.40",
            	"output_thresh" : "-24.40",
            	"normalization_type" : "dynamic",
            	"target_offset" : "0.10"
            }
            """;
        var m = LoudnessMeasurement.Parse(stderr);
        Assert.NotNull(m);
        Assert.Equal(-27.61, m.IntegratedLufs, 3);
        Assert.Equal(-9.02, m.TruePeakDb, 3);
        Assert.Equal(4.3, m.Range, 3);
        Assert.Equal(-38.01, m.Threshold, 3);
        Assert.Equal(0.1, m.Offset, 3);
        Assert.Null(LoudnessMeasurement.Parse("keine Messung"));
        Assert.Equal(-99, LoudnessMeasurement.Parse("""{ "input_i" : "-inf", "input_tp" : "-inf", "input_lra" : "0", "input_thresh" : "-70", "target_offset" : "0" }""")!.IntegratedLufs);
    }

    [Fact]
    public void Export_UsesChannelStripsDuckingAndMaster()
    {
        var (editor, music) = MakeProject();
        var project = editor.Project;
        var v1 = project.VideoTracks.First();
        v1.Audio.HighPassHz = 100;
        v1.VolumeDb = 3;
        music.VolumeDb = -6;
        music.Audio.DuckBy = v1.Id;
        music.Audio.DuckAmountDb = 16;
        project.Master.VolumeDb = -1;
        project.Master.LoudnessTarget = -14;

        var graph = string.Join(' ', ExportBuilder.Build(project, ExportPreset.Default, "out.mp4").Arguments);
        Assert.Contains("[acam]highpass=f=100,volume=3dB[bus0]", graph);
        Assert.Contains("volume=-6dB[bus1]", graph);
        Assert.Contains("[bus0]asplit=2[dm0][sc0_0]", graph);
        Assert.Contains("[bus1][sc0_0]sidechaincompress=threshold=0.03:ratio=8", graph);
        Assert.Contains("[dm0][dk1]amix=inputs=2:duration=first:normalize=0,volume=-1dB,loudnorm=I=-14:TP=-1:LRA=11,aresample=48000,alimiter", graph);

        var measured = new LoudnessMeasurement(-20, -3, 5, -30, 0.2);
        var twoPass = string.Join(' ', ExportBuilder.Build(project, ExportPreset.Default, "out.mp4", loudness: measured).Arguments);
        Assert.Contains("measured_I=-20:measured_TP=-3:measured_LRA=5:measured_thresh=-30:offset=0.2:linear=true", twoPass);

        var measure = ExportBuilder.BuildLoudnessMeasurement(project).Arguments;
        Assert.Contains("print_format=json", string.Join(' ', measure));
        Assert.Equal("-", measure[^1]);
        Assert.DoesNotContain("alimiter", string.Join(' ', measure));

        // Stummgeschaltet bzw. Solo: Musikspur fällt aus der Mischung
        music.Muted = true;
        var muted = string.Join(' ', ExportBuilder.Build(project, ExportPreset.Default, "out.mp4").Arguments);
        Assert.DoesNotContain("sidechaincompress", muted);
        Assert.Contains("[bus0]anull,volume=-1dB", muted);

        music.Muted = false;
        music.Audio.Solo = true;
        var solo = string.Join(' ', ExportBuilder.Build(project, ExportPreset.Default, "out.mp4").Arguments);
        Assert.Contains("anullsrc=r=48000:cl=stereo,atrim=duration=4[a0]", solo);   // Kameraton stumm, weil Musik solo
        Assert.Contains("sidechaincompress", solo);
    }

    [Fact]
    public void Editor_MixerChangesAreUndoable()
    {
        var (editor, music) = MakeProject();
        var v1 = editor.Project.VideoTracks.First();
        Assert.True(editor.SetTrackAudio(music.Id, "Panorama", t => t.Audio.Pan = 0.4));
        Assert.True(editor.SetTrackAudio(music.Id, "Panorama", t => t.Audio.Pan = 0.6));
        Assert.False(editor.SetTrackAudio(music.Id, "Panorama", t => t.Audio.Pan = 0.6));
        Assert.True(editor.SetTrackAudio(music.Id, "Ducking", t => t.Audio.DuckBy = v1.Id));
        Assert.True(editor.SetTrackAudio(v1.Id, "Ducking", t => t.Audio.DuckBy = v1.Id) is false);
        Assert.True(editor.SetMaster("Lautheit", m => m.LoudnessTarget = -14));
        Assert.Equal(0.6, editor.Project.AudioTracks.First().Audio.Pan);
        editor.Undo();
        Assert.Null(editor.Project.Master.LoudnessTarget);
        editor.Undo();
        Assert.Null(editor.Project.AudioTracks.First().Audio.DuckBy);
        editor.Undo();   // beide Panorama-Schritte wurden zusammengefasst
        Assert.Equal(0, editor.Project.AudioTracks.First().Audio.Pan);
    }

    [Fact]
    public void Project_SavesMixerSettings()
    {
        var (editor, music) = MakeProject();
        music.Audio.Pan = 0.25;
        music.Audio.DuckBy = editor.Project.VideoTracks.First().Id;
        editor.Project.Master.LoudnessTarget = -16;
        var back = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(editor.Project));
        Assert.Equal(0.25, back.AudioTracks.First().Audio.Pan);
        Assert.Equal(editor.Project.VideoTracks.First().Id, back.AudioTracks.First().Audio.DuckBy);
        Assert.Equal(-16, back.Master.LoudnessTarget);

        var clone = editor.Project.Clone();
        clone.Master.LoudnessTarget = null;
        clone.AudioTracks.First().Audio.Pan = 0;
        Assert.Equal(-16, editor.Project.Master.LoudnessTarget);
        Assert.Equal(0.25, music.Audio.Pan);
    }

    [Fact]
    public async Task RealFfmpeg_TwoPassLoudnessHitsTarget()
    {
        if (FfmpegLocator.LocateDefault() is not { } paths)
        {
            return;
        }

        var client = new FfmpegClient(paths);
        var speech = Path.Combine(_temp, "MVI_0001.MOV");
        await client.RunAsync(["-y", "-v", "error", "-f", "lavfi", "-i", "testsrc2=s=320x180:r=25:d=8", "-f", "lavfi",
            "-i", "sine=f=440:d=8,volume=-6dB", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "pcm_s16le", speech], 0);
        var musicFile = Path.Combine(_temp, "musik.wav");
        await client.RunAsync(["-y", "-v", "error", "-f", "lavfi", "-i", "sine=f=220:d=8,volume=-3dB", musicFile], 0);

        var video = MediaItem.FromRecording(new RecordingGroup([await client.ProbeAsync(speech)]));
        var song = MediaItem.FromRecording(new RecordingGroup([await client.ProbeAsync(musicFile)]));
        var editor = new TimelineEditor(MovieProject.CreateDefault(new ProjectSettings { Width = 320, Height = 180, FrameRate = FrameRate.Fps25 }));
        editor.AddMedia([video, song]);
        var v1 = editor.Project.VideoTracks.First();
        editor.Append(v1, editor.CreateClip(video, 0, 8));
        editor.Append(editor.Project.AudioTracks.First(), editor.CreateClip(song, 0, 8));
        v1 = editor.Project.VideoTracks.First();
        var musicTrack = editor.Project.AudioTracks.First();
        v1.Audio.HighPassHz = 80;
        v1.Audio.Compressor = true;
        v1.Audio.NoiseReduction = 0.3;
        musicTrack.Audio.DuckBy = v1.Id;
        musicTrack.Audio.EqLow = 2;
        musicTrack.Audio.Pan = 0.3;
        editor.Project.Master.LoudnessTarget = -16;

        var measureJob = ExportBuilder.BuildLoudnessMeasurement(editor.Project);
        var measured = LoudnessMeasurement.Parse(await client.RunForStderrAsync(measureJob.Arguments));
        Assert.NotNull(measured);
        Assert.InRange(measured.IntegratedLufs, -40, -10);

        var output = Path.Combine(_temp, "laut.wav");
        var job = ExportBuilder.Build(editor.Project, ExportPreset.Find("audio-wav")!, output, loudness: measured);
        await client.RunAsync(job.Arguments, job.DurationSeconds);

        var verify = LoudnessMeasurement.Parse(await client.RunForStderrAsync(
            ["-v", "info", "-nostdin", "-hide_banner", "-i", output, "-af", "loudnorm=I=-16:TP=-1:LRA=11:print_format=json", "-f", "null", "-"]));
        Assert.NotNull(verify);
        Assert.InRange(verify.IntegratedLufs, -17.5, -14.5);
        Assert.True(verify.TruePeakDb <= -0.3, $"Spitze {verify.TruePeakDb}");
    }
}
