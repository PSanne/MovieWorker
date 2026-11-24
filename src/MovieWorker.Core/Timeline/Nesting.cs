namespace MovieWorker.Core.Timeline;

public enum SpeedRampShape
{
    /// <summary>Normal → fließend in Zeitlupe (z. B. vor dem Höhepunkt).</summary>
    SlowDown,

    /// <summary>Zeitlupe → fließend auf normale Geschwindigkeit.</summary>
    SpeedUp,

    /// <summary>Normal → Zeitlupe → wieder normal.</summary>
    SlowMiddle,
}

/// <summary>
/// Löst verschachtelte Clips für Export und Vorschau auf: Ihr Inhalt wird an der richtigen Stelle in die
/// Timeline gelegt (erste Videospur des Inhalts ersetzt den Clip, weitere Spuren kommen auf freie Spuren).
/// </summary>
public static class NestFlattener
{
    public static MovieProject Flatten(MovieProject project)
    {
        if (!project.AllClips.Any(c => c.IsNested))
        {
            return project;
        }

        var copy = project.Clone();
        for (var depth = 0; depth < 8 && copy.AllClips.Any(c => c.IsNested); depth++)
        {
            foreach (var track in copy.Tracks.ToList())
            {
                foreach (var nested in track.Clips.Where(c => c.IsNested).ToList())
                {
                    Expand(copy, track, nested);
                }
            }
        }

        return copy;
    }

    /// <summary>Ersetzt einen verschachtelten Clip in <paramref name="project"/> durch seinen Inhalt.</summary>
    internal static void Expand(MovieProject project, TimelineTrack host, TimelineClip nested)
    {
        var rate = project.Settings.FrameRate;
        host.Clips.Remove(nested);
        var rangeStart = rate.SecondsToFrames(nested.SourceIn);
        var rangeEnd = rangeStart + nested.LengthFrames;
        var kindIndex = new Dictionary<TrackKind, int>();

        foreach (var inner in nested.NestedTracks ?? [])
        {
            var index = kindIndex.GetValueOrDefault(inner.Kind);
            kindIndex[inner.Kind] = index + 1;
            if (inner.Muted)
            {
                continue;
            }

            var pieces = new List<TimelineClip>();
            foreach (var clip in inner.Clips)
            {
                var start = Math.Max(clip.StartFrame, rangeStart);
                var end = Math.Min(clip.EndFrame, rangeEnd);
                if (end <= start)
                {
                    continue;
                }

                var piece = clip.Clone();
                piece.Id = Guid.NewGuid();
                var cutFront = start - clip.StartFrame;
                var cutBack = clip.EndFrame - end;
                piece.SourceIn = clip.SourceTimeAt(start, rate);
                piece.ShiftKeyframes(-cutFront);
                piece.StartFrame = nested.StartFrame + (start - rangeStart);
                piece.LengthFrames = end - start;
                piece.FadeInFrames = Math.Max(0, clip.FadeInFrames - cutFront);
                piece.FadeOutFrames = Math.Max(0, clip.FadeOutFrames - cutBack);
                piece.VolumeDb += nested.VolumeDb + inner.VolumeDb;
                piece.AudioMuted |= nested.AudioMuted;
                pieces.Add(piece);
            }

            if (pieces.Count == 0)
            {
                continue;
            }

            bool Fits(TimelineTrack t) => pieces.All(pc => t.IsFree(pc.StartFrame, pc.LengthFrames));
            var target = inner.Kind == host.Kind && index == 0 && Fits(host) ? host : null;
            target ??= project.Tracks.Where(t => t.Kind == inner.Kind).Skip(index).FirstOrDefault(Fits);
            if (target is null)
            {
                var count = project.Tracks.Count(t => t.Kind == inner.Kind);
                target = new TimelineTrack { Kind = inner.Kind, Name = $"{(inner.Kind == TrackKind.Video ? "V" : "A")}{count + 1}" };
                var last = project.Tracks.FindLastIndex(t => t.Kind == inner.Kind);
                project.Tracks.Insert(last + 1, target);
            }

            target.Clips.AddRange(pieces);
        }
    }
}
