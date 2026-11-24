using MovieWorker.Core.Camera;
using MovieWorker.Core.Media;
using MovieWorker.Core.Projects;
using MovieWorker.Core.Time;
using MovieWorker.Core.Timeline;

namespace MovieWorker.Core.Tests;

public class Sprint7Tests
{
    private static (TimelineEditor Editor, MediaItem Media) Setup()
    {
        var editor = new TimelineEditor(MovieProject.CreateDefault(new ProjectSettings { FrameRate = FrameRate.Fps25 }));
        var media = MediaItem.FromRecording(new RecordingGroup([TestMedia.Eos70D()])); // 120 s
        editor.AddMedia([media]);
        var v1 = editor.Project.VideoTracks.First();
        editor.Append(v1, editor.CreateClip(media, 0, 4));   // A: 0–100
        editor.Append(v1, editor.CreateClip(media, 10, 14)); // B: 100–200
        var music = MediaItem.FromRecording(new RecordingGroup([TestMedia.Eos70D("/c/song.mov")]));
        editor.AddMedia([music]);
        editor.Insert(editor.Project.AudioTracks.First(), editor.CreateClip(music, 0, 8), 0); // Musik 0–200
        return (editor, media);
    }

    private static List<TimelineClip> V1(TimelineEditor e) => e.Project.VideoTracks.First().OrderedClips.ToList();

    [Fact]
    public void Insert_RipplesAllTracks()
    {
        var (editor, media) = Setup();
        var clip = editor.CreateClip(media, 50, 52); // 50 Bilder
        Assert.True(editor.Place(EditMode.Insert, editor.Project.VideoTracks.First().Id, clip, 50));
        var v1 = V1(editor);
        Assert.Equal([0, 50, 100, 150], v1.Select(c => c.StartFrame));
        Assert.Equal(2.0, v1[2].SourceIn, 6); // rechte Hälfte von A
        var music = editor.Project.AudioTracks.First().OrderedClips.ToList();
        Assert.Equal(2, music.Count);           // Musik wurde ebenfalls geteilt …
        Assert.Equal(100, music[1].StartFrame); // … und rückt nach
        Assert.Equal(250, editor.Duration);
    }

    [Fact]
    public void Overwrite_ReplacesRangeWithoutShifting()
    {
        var (editor, media) = Setup();
        var clip = editor.CreateClip(media, 50, 52);
        Assert.True(editor.Place(EditMode.Overwrite, editor.Project.VideoTracks.First().Id, clip, 75));
        var v1 = V1(editor);
        Assert.Equal([0, 75, 125], v1.Select(c => c.StartFrame));
        Assert.Equal(75, v1[0].LengthFrames);
        Assert.Equal(75, v1[2].LengthFrames);
        Assert.Equal(11.0, v1[2].SourceIn, 6);
        Assert.Equal(200, editor.Duration);
    }

    [Fact]
    public void Overwrite_InsideOneClipSplitsIt()
    {
        var (editor, media) = Setup();
        Assert.True(editor.Place(EditMode.Overwrite, editor.Project.VideoTracks.First().Id, editor.CreateClip(media, 60, 61), 20));
        var v1 = V1(editor);
        Assert.Equal([0, 20, 45, 100], v1.Select(c => c.StartFrame));
        Assert.Equal(1.8, v1[2].SourceIn, 6);
    }

    [Fact]
    public void Replace_KeepsPositionAndLength()
    {
        var (editor, media) = Setup();
        var target = V1(editor)[1];
        Assert.True(editor.Place(EditMode.Replace, editor.Project.VideoTracks.First().Id, editor.CreateClip(media, 30, 60), 0, replaceClipId: target.Id));
        var v1 = V1(editor);
        Assert.Equal(2, v1.Count);
        Assert.Equal(100, v1[1].StartFrame);
        Assert.Equal(100, v1[1].LengthFrames);
        Assert.Equal(30.0, v1[1].SourceIn, 6);
    }

    [Fact]
    public void PlaceOnTop_UsesOrCreatesTrackAbove()
    {
        var (editor, media) = Setup();
        var v1Id = editor.Project.VideoTracks.First().Id;
        Assert.True(editor.Place(EditMode.PlaceOnTop, v1Id, editor.CreateClip(media, 70, 71), 30));
        Assert.Equal(2, editor.Project.VideoTracks.Count());
        Assert.True(editor.Place(EditMode.PlaceOnTop, v1Id, editor.CreateClip(media, 80, 81), 40)); // V2 belegt → V3
        Assert.Equal(3, editor.Project.VideoTracks.Count());
        Assert.True(editor.Place(EditMode.PlaceOnTop, v1Id, editor.CreateClip(media, 90, 91), 150)); // V2 dort frei
        Assert.Equal(2, editor.Project.VideoTracks.ElementAt(1).Clips.Count);
        Assert.Equal(200, editor.Duration);
    }

    [Fact]
    public void Append_And_OverwriteWithLinkedAudio()
    {
        var (editor, media) = Setup();
        var v1Id = editor.Project.VideoTracks.First().Id;
        Assert.True(editor.Place(EditMode.Append, v1Id, editor.CreateClip(media, 0, 1), 0));
        Assert.Equal(225, editor.Duration);

        var audio = editor.CreateClip(media, 0, 2);
        Assert.True(editor.Place(EditMode.Overwrite, v1Id, editor.CreateClip(media, 0, 2), 10, audio));
        Assert.Equal("A2 Ton", editor.Project.FindClip(audio.Id)!.Value.Track.Name);
        editor.Undo();
        Assert.Null(editor.Project.FindClip(audio.Id));
    }

    [Fact]
    public void SourceTape_MapsPositions()
    {
        var a = MediaItem.FromRecording(new RecordingGroup([TestMedia.Eos70D("/c/MVI_0001.MOV")]));  // 120 s
        var b = MediaItem.FromRecording(new RecordingGroup([TestMedia.R6Clog()]));                  // 30,03 s
        var tape = SourceTape.Build([a, b]);
        Assert.Equal(150.03, tape.Duration, 6);
        var at = tape.At(125)!.Value;
        Assert.Equal(b.Id, at.Segment.Media.Id);
        Assert.Equal(5, at.LocalSeconds, 6);
        Assert.Equal(a.Id, tape.At(0)!.Value.Segment.Media.Id);
        Assert.Equal(b.Id, tape.At(9999)!.Value.Segment.Media.Id);
        Assert.Equal(130, tape.TapeTime(b, 10)!.Value, 6);
        Assert.Equal(b, tape.Next(tape.Segments[0])!.Media);
        Assert.Null(tape.Previous(tape.Segments[0]));
        Assert.Null(SourceTape.Build([]).At(0));
    }
}
