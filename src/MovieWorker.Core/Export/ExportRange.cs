using MovieWorker.Core.Timeline;

namespace MovieWorker.Core.Export;

/// <summary>Schneidet ein Projekt auf einen Bereich (In/Out) zu – für „nur Bereich exportieren“.</summary>
public static class ExportRange
{
    public static MovieProject Extract(MovieProject project, long start, long end)
    {
        if (end <= start)
        {
            throw new ArgumentException("Das Ende des Bereichs muss hinter dem Anfang liegen.", nameof(end));
        }

        var rate = project.Settings.FrameRate;
        var copy = project.Clone();
        copy.MarkIn = null;
        copy.MarkOut = null;
        foreach (var track in copy.Tracks)
        {
            var kept = new List<TimelineClip>();
            foreach (var clip in track.Clips)
            {
                var clipStart = Math.Max(clip.StartFrame, start);
                var clipEnd = Math.Min(clip.EndFrame, end);
                if (clipEnd <= clipStart)
                {
                    continue;
                }

                var cutFront = clipStart - clip.StartFrame;
                var cutBack = clip.EndFrame - clipEnd;
                clip.SourceIn = clip.SourceTimeAt(clipStart, rate);
                clip.ShiftKeyframes(-cutFront);
                clip.StartFrame = clipStart - start;
                clip.LengthFrames = clipEnd - clipStart;
                clip.FadeInFrames = Math.Max(0, clip.FadeInFrames - cutFront);
                clip.FadeOutFrames = Math.Max(0, clip.FadeOutFrames - cutBack);
                kept.Add(clip);
            }

            track.Clips = kept;
        }

        return copy;
    }
}
