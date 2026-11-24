namespace MovieWorker.Core.Projects;

/// <summary>Ein Abschnitt des Quellbands.</summary>
public sealed record TapeSegment(MediaItem Media, double Start, double Duration)
{
    public double End => Start + Duration;
}

/// <summary>
/// Quellband: alle (gefilterten) Aufnahmen hintereinander wie ein einziges langes Band – zum schnellen
/// Durchsehen und Herausschneiden, ohne jede Aufnahme einzeln zu öffnen.
/// </summary>
public sealed class SourceTape
{
    private SourceTape(IReadOnlyList<TapeSegment> segments)
    {
        Segments = segments;
        Duration = segments.Count == 0 ? 0 : segments[^1].End;
    }

    public IReadOnlyList<TapeSegment> Segments { get; }

    public double Duration { get; }

    public static SourceTape Build(IEnumerable<MediaItem> items)
    {
        var segments = new List<TapeSegment>();
        double position = 0;
        foreach (var item in items.Where(i => i.Parts.Count > 0))
        {
            var duration = Math.Max(0.04, item.Duration.TotalSeconds);
            segments.Add(new TapeSegment(item, position, duration));
            position += duration;
        }

        return new SourceTape(segments);
    }

    /// <summary>Welcher Abschnitt an dieser Stelle des Bands liegt (und die Zeit innerhalb der Aufnahme).</summary>
    public (TapeSegment Segment, double LocalSeconds)? At(double tapeSeconds)
    {
        if (Segments.Count == 0)
        {
            return null;
        }

        var t = Math.Clamp(tapeSeconds, 0, Duration);
        foreach (var segment in Segments)
        {
            if (t < segment.End)
            {
                return (segment, t - segment.Start);
            }
        }

        var last = Segments[^1];
        return (last, last.Duration);
    }

    /// <summary>Stelle auf dem Band für eine Zeit in einer Aufnahme.</summary>
    public double? TapeTime(MediaItem media, double localSeconds) =>
        Segments.FirstOrDefault(s => s.Media.Id == media.Id) is { } segment ? segment.Start + Math.Clamp(localSeconds, 0, segment.Duration) : null;

    public TapeSegment? Next(TapeSegment segment) => Segments.SkipWhile(s => s != segment).Skip(1).FirstOrDefault();

    public TapeSegment? Previous(TapeSegment segment) => Segments.TakeWhile(s => s != segment).LastOrDefault();
}
