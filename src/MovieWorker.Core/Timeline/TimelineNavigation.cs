namespace MovieWorker.Core.Timeline;

/// <summary>Einrasten und Springen zwischen Schnittpunkten.</summary>
public static class TimelineNavigation
{
    /// <summary>Alle Stellen, an denen Clips beginnen oder enden (plus Anfang), sortiert.</summary>
    public static IReadOnlyList<long> EditPoints(MovieProject project, Guid? excludeClip = null) =>
        project.AllClips.Where(c => c.Id != excludeClip)
            .SelectMany(c => new[] { c.StartFrame, c.EndFrame })
            .Append(0)
            .Distinct().Order().ToList();

    public static long NextEditPoint(MovieProject project, long frame) =>
        EditPoints(project).FirstOrDefault(p => p > frame, Math.Max(frame, project.DurationFrames));

    public static long PreviousEditPoint(MovieProject project, long frame) =>
        EditPoints(project).LastOrDefault(p => p < frame, 0);

    /// <summary>
    /// Rastet <paramref name="frame"/> auf den nächsten Punkt ein, wenn er höchstens
    /// <paramref name="thresholdFrames"/> entfernt ist. Liefert die (ggf. unveränderte) Position.
    /// </summary>
    public static long Snap(long frame, IEnumerable<long> points, double thresholdFrames, out bool snapped)
    {
        snapped = false;
        var best = frame;
        var bestDistance = thresholdFrames;
        foreach (var point in points)
        {
            var distance = Math.Abs(point - frame);
            if (distance <= bestDistance)
            {
                best = point;
                bestDistance = distance;
                snapped = true;
            }
        }

        return best;
    }

    /// <summary>
    /// Rastet einen verschobenen Clip mit Anfang oder Ende ein (je nachdem, was näher liegt).
    /// </summary>
    public static long SnapClipStart(long start, long length, IReadOnlyList<long> points, double thresholdFrames)
    {
        var snappedStart = Snap(start, points, thresholdFrames, out var startSnapped);
        var snappedEnd = Snap(start + length, points, thresholdFrames, out var endSnapped);
        if (startSnapped && (!endSnapped || Math.Abs(snappedStart - start) <= Math.Abs(snappedEnd - (start + length))))
        {
            return snappedStart;
        }

        return endSnapped ? snappedEnd - length : start;
    }
}
