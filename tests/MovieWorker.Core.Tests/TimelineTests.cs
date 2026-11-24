using MovieWorker.Core.Camera;
using MovieWorker.Core.Projects;
using MovieWorker.Core.Time;
using MovieWorker.Core.Timeline;

namespace MovieWorker.Core.Tests;

public class TimelineTests
{
    private static (TimelineEditor Editor, TimelineTrack V1) NewEditor()
    {
        var editor = new TimelineEditor(MovieProject.CreateDefault(new ProjectSettings { Width = 1920, Height = 1080, FrameRate = FrameRate.Fps25 }));
        return (editor, editor.Project.VideoTracks.First());
    }

    private static TimelineClip Clip(TimelineEditor editor, double seconds = 120) =>
        editor.CreateClip(new RecordingGroup([TestMedia.Eos70D()]), 0, seconds);

    [Fact]
    public void Append_PlacesClipsBackToBack()
    {
        var (editor, v1) = NewEditor();
        editor.Append(v1, Clip(editor, 10));
        editor.Append(v1, Clip(editor, 4));
        var clips = editor.Project.VideoTracks.First().OrderedClips.ToList();
        Assert.Equal(0, clips[0].StartFrame);
        Assert.Equal(250, clips[0].LengthFrames);
        Assert.Equal(250, clips[1].StartFrame);
        Assert.Equal(350, editor.Duration);
    }

    [Fact]
    public void Split_CreatesTwoClipsWithCorrectSourceTimes()
    {
        var (editor, v1) = NewEditor();
        editor.Append(v1, Clip(editor, 10));
        Assert.True(editor.SplitAt(100));
        var clips = editor.Project.VideoTracks.First().OrderedClips.ToList();
        Assert.Equal(2, clips.Count);
        Assert.Equal(100, clips[0].LengthFrames);
        Assert.Equal(150, clips[1].LengthFrames);
        Assert.Equal(4.0, clips[1].SourceIn, 9);
        Assert.NotEqual(clips[0].Id, clips[1].Id);
    }

    [Fact]
    public void Split_AtClipBoundary_DoesNothing()
    {
        var (editor, v1) = NewEditor();
        editor.Append(v1, Clip(editor, 10));
        Assert.False(editor.SplitAt(0));
        Assert.False(editor.SplitAt(250));
        Assert.Equal("Clip einfügen", editor.History.UndoDescription);
    }

    [Fact]
    public void RippleDelete_ClosesGap()
    {
        var (editor, v1) = NewEditor();
        editor.Append(v1, Clip(editor, 2));
        editor.Append(v1, Clip(editor, 3));
        editor.Append(v1, Clip(editor, 4));
        var middle = editor.Project.VideoTracks.First().OrderedClips.ElementAt(1);
        Assert.True(editor.Delete(middle.Id, ripple: true));
        var clips = editor.Project.VideoTracks.First().OrderedClips.ToList();
        Assert.Equal(2, clips.Count);
        Assert.Equal(50, clips[1].StartFrame);
        Assert.Equal(150, editor.Duration);
    }

    [Fact]
    public void Move_RejectsOverlap()
    {
        var (editor, v1) = NewEditor();
        editor.Append(v1, Clip(editor, 2));
        editor.Append(v1, Clip(editor, 2));
        var second = editor.Project.VideoTracks.First().OrderedClips.Last();
        Assert.False(editor.Move(second.Id, 25));
        Assert.True(editor.Move(second.Id, 500));
        Assert.Equal(550, editor.Duration);
    }

    [Fact]
    public void Move_OnlyToTrackOfSameKind()
    {
        var (editor, v1) = NewEditor();
        editor.Append(v1, Clip(editor, 2));
        var clip = editor.Project.AllClips.Single();
        var audio = editor.Project.AudioTracks.First();
        Assert.False(editor.Move(clip.Id, 0, audio.Id));
    }

    [Fact]
    public void Trim_IsLimitedBySourceAndNeighbours()
    {
        var (editor, v1) = NewEditor();
        editor.Append(v1, editor.CreateClip(new RecordingGroup([TestMedia.Eos70D()]), 10, 20));
        var clip = editor.Project.AllClips.Single();

        Assert.True(editor.TrimStart(clip.Id, 50));
        clip = editor.Project.AllClips.Single();
        Assert.Equal(50, clip.StartFrame);
        Assert.Equal(12.0, clip.SourceIn, 9);
        Assert.Equal(200, clip.LengthFrames);

        // Nach vorne verlängern: höchstens bis Bild 0 (die Quelle hätte noch 12 s davor).
        Assert.True(editor.TrimStart(clip.Id, -1000));
        clip = editor.Project.AllClips.Single();
        Assert.Equal(0, clip.StartFrame);
        Assert.Equal(10.0, clip.SourceIn, 9);

        // Ende: höchstens bis zum Ende der Quelle (120 s).
        Assert.True(editor.TrimEnd(clip.Id, 100_000));
        clip = editor.Project.AllClips.Single();
        Assert.Equal(120.0, clip.SourceOut(editor.Project.Settings.FrameRate), 6);
    }

    [Fact]
    public void ConformHighFrameRate_MakesSlowMotion()
    {
        var (editor, v1) = NewEditor();
        editor.Append(v1, Clip(editor, 2)); // 50 Bilder
        var clip = editor.Project.AllClips.Single();
        Assert.True(editor.ConformToProjectRate(clip.Id, 150));
        clip = editor.Project.AllClips.Single();
        Assert.Equal(1.0 / 6, clip.Speed, 9);
        Assert.Equal(300, clip.LengthFrames);
        Assert.True(clip.AudioMuted);
    }

    [Fact]
    public void UndoRedo_RestoresStates()
    {
        var (editor, v1) = NewEditor();
        editor.Append(v1, Clip(editor, 10));
        editor.SplitAt(100);
        Assert.Equal("Teilen", editor.History.UndoDescription);
        Assert.True(editor.Undo());
        Assert.Single(editor.Project.AllClips);
        Assert.True(editor.Redo());
        Assert.Equal(2, editor.Project.AllClips.Count());
        Assert.True(editor.Undo());
        Assert.True(editor.Undo());
        Assert.Empty(editor.Project.AllClips);
        Assert.False(editor.Undo());
    }

    [Fact]
    public void ChangeFrameRate_KeepsTimeInSeconds()
    {
        var (editor, v1) = NewEditor();
        editor.Append(v1, Clip(editor, 10));
        var settings = editor.Project.Settings.Clone();
        settings.FrameRate = FrameRate.Fps50;
        editor.ChangeSettings(settings);
        Assert.Equal(500, editor.Project.AllClips.Single().LengthFrames);
    }

    [Fact]
    public void CreateClip_SelectsToneMappingForHdr()
    {
        var (editor, _) = NewEditor();
        var media = Media.FfprobeParser.Parse("MVI_0001.MP4", TestMedia.R6PqJson);
        Assert.Equal(ColorTransform.HdrToSdr, editor.CreateClip(new RecordingGroup([media])).ColorTransform);
    }

    [Fact]
    public void Project_RoundTripsThroughJson()
    {
        var (editor, v1) = NewEditor();
        editor.Append(v1, Clip(editor, 10));
        editor.UpdateClip(editor.Project.AllClips.Single().Id, "LUT", c =>
        {
            c.ColorTransform = ColorTransform.Lut;
            c.LutPath = "C:/LUTs/CanonLog3_to_BT709.cube";
            c.FadeInFrames = 12;
        });

        var json = ProjectSerializer.Serialize(editor.Project);
        var loaded = ProjectSerializer.Deserialize(json);
        var clip = loaded.AllClips.Single();
        Assert.Equal(ColorTransform.Lut, clip.ColorTransform);
        Assert.Equal(12, clip.FadeInFrames);
        Assert.Equal(FrameRate.Fps25, loaded.Settings.FrameRate);
        Assert.Contains("\"Lut\"", json);
    }

    [Fact]
    public void FromMedia_UsesNormalRateForHighFrameRate()
    {
        var media = Media.FfprobeParser.Parse("MVI_0200.MP4", TestMedia.R6HfrJson);
        Assert.Equal(FrameRate.Fps29_97, ProjectSettings.FromMedia(media).FrameRate);
        Assert.Equal(FrameRate.Fps23_976, ProjectSettings.FromMedia(TestMedia.R6Clog()).FrameRate);
    }
}
