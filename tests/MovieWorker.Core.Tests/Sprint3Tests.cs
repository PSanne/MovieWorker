using MovieWorker.Core.Camera;
using MovieWorker.Core.Media;
using MovieWorker.Core.Projects;
using MovieWorker.Core.Time;
using MovieWorker.Core.Timeline;

namespace MovieWorker.Core.Tests;

public class Sprint3Tests
{
    [Fact]
    public void Viewport_ConvertsAndZoomsAroundAnchor()
    {
        var viewport = new TimelineViewport { Width = 1000, PixelsPerFrame = 2, ScrollFrame = 100 };
        Assert.Equal(200, viewport.FrameToX(200));
        Assert.Equal(150, viewport.XToFrame(100));

        viewport.ZoomAround(500, 2);
        Assert.Equal(4, viewport.PixelsPerFrame);
        Assert.Equal(350, viewport.XToFrame(500), 6); // Bild unter der Maus bleibt stehen
    }

    [Fact]
    public void Viewport_FitAndEnsureVisible()
    {
        var viewport = new TimelineViewport { Width = 1050 };
        viewport.Fit(1000);
        Assert.Equal(1.0, viewport.PixelsPerFrame, 6);
        Assert.Equal(0, viewport.ScrollFrame);

        Assert.True(viewport.EnsureVisible(5000));
        Assert.InRange(5000, viewport.ScrollFrame, viewport.ScrollFrame + viewport.VisibleFrames);
        Assert.False(viewport.EnsureVisible(viewport.ScrollFrame + viewport.VisibleFrames / 2));
    }

    [Fact]
    public void Viewport_RulerStepIsRoundAndWideEnough()
    {
        var viewport = new TimelineViewport { PixelsPerFrame = 20 };
        Assert.Equal(5, viewport.RulerStep(FrameRate.Fps25)); // 5 Bilder = 100 px
        viewport.PixelsPerFrame = 1;
        Assert.Equal(125, viewport.RulerStep(FrameRate.Fps25)); // 5 s
        viewport.PixelsPerFrame = 0.01;
        Assert.Equal(15000, viewport.RulerStep(FrameRate.Fps25)); // 10 min
    }

    [Fact]
    public void Navigation_EditPointsAndSnapping()
    {
        var editor = new TimelineEditor(MovieProject.CreateDefault(new ProjectSettings { FrameRate = FrameRate.Fps25 }));
        var v1 = editor.Project.VideoTracks.First();
        editor.Append(v1, editor.CreateClip(new RecordingGroup([TestMedia.Eos70D()]), 0, 4));   // 0–100
        editor.Insert(v1, editor.CreateClip(new RecordingGroup([TestMedia.Eos70D()]), 0, 2), 150); // 150–200

        Assert.Equal([0, 100, 150, 200], TimelineNavigation.EditPoints(editor.Project));
        Assert.Equal(150, TimelineNavigation.NextEditPoint(editor.Project, 100));
        Assert.Equal(100, TimelineNavigation.PreviousEditPoint(editor.Project, 150));
        Assert.Equal(200, TimelineNavigation.NextEditPoint(editor.Project, 200));

        Assert.Equal(100, TimelineNavigation.Snap(104, [0, 100, 150], 5, out var snapped));
        Assert.True(snapped);
        Assert.Equal(110, TimelineNavigation.Snap(110, [0, 100, 150], 5, out snapped));
        Assert.False(snapped);

        // Clip (Länge 30) rastet mit seinem Ende an 150 ein
        Assert.Equal(120, TimelineNavigation.SnapClipStart(118, 30, [0, 100, 150], 5));
        Assert.Equal(100, TimelineNavigation.SnapClipStart(103, 30, [0, 100, 150], 5));
    }

    [Fact]
    public void Waveform_FromPcm16()
    {
        var pcm = new byte[4000 * 2]; // 1 s bei 4 kHz
        for (var i = 0; i < 4000; i++)
        {
            short value = i < 2000 ? (short)16384 : (short)-32768;
            pcm[2 * i] = (byte)(value & 0xFF);
            pcm[2 * i + 1] = (byte)((value >> 8) & 0xFF);
        }

        var wave = WaveformData.FromPcm16(pcm);
        Assert.Equal(100, wave.Peaks.Length);
        Assert.Equal(1.0, wave.DurationSeconds, 6);
        Assert.Equal(0.5f, wave.PeakBetween(0, 0.4), 3);
        Assert.Equal(1.0f, wave.PeakBetween(0.4, 0.6), 3);
        Assert.Equal(0f, wave.PeakBetween(5, 6));
    }

    [Fact]
    public void Marks_TracksAndMuteAreUndoable()
    {
        var editor = new TimelineEditor();
        editor.SetMarks(50, 20); // Out vor In wird verworfen
        Assert.Equal(50, editor.Project.MarkIn);
        Assert.Null(editor.Project.MarkOut);
        editor.SetMarks(50, 120);
        Assert.True(editor.Project.HasRange);

        var v2 = editor.AddTrack(TrackKind.Video);
        Assert.Equal("V2", v2.Name);
        Assert.Equal(TrackKind.Video, editor.Project.Tracks[1].Kind); // direkt hinter V1
        editor.SetTrackMuted(v2.Id, true);
        Assert.True(editor.Project.Tracks[1].Muted);

        editor.Undo();
        editor.Undo();
        Assert.Equal(3, editor.Project.Tracks.Count);
        editor.Undo();
        Assert.Null(editor.Project.MarkOut);

        var json = ProjectSerializer.Serialize(editor.Project);
        Assert.Equal(50, ProjectSerializer.Deserialize(json).MarkIn);
    }

    [Fact]
    public void History_MergesRapidSliderChanges()
    {
        var editor = new TimelineEditor();
        editor.Append(editor.Project.VideoTracks.First(), editor.CreateClip(new RecordingGroup([TestMedia.Eos70D()]), 0, 4));
        var id = editor.Project.AllClips.Single().Id;
        for (var db = -1; db >= -10; db--)
        {
            var value = db;
            editor.UpdateClip(id, "Lautstärke", c => c.VolumeDb = value, mergeSimilar: true);
        }

        Assert.Equal(-10, editor.Project.AllClips.Single().VolumeDb);
        editor.Undo();
        Assert.Equal(0, editor.Project.AllClips.Single().VolumeDb); // ein einziger Schritt
        Assert.Equal("Clip einfügen", editor.History.UndoDescription);
    }
}
