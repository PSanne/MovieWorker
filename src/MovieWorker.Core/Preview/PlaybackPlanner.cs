using MovieWorker.Core.Export;
using MovieWorker.Core.Projects;
using MovieWorker.Core.Timeline;

namespace MovieWorker.Core.Preview;

/// <summary>Was an einer Stelle der Timeline zu sehen bzw. zu hören ist.</summary>
public sealed record PlaybackSource(
    TimelineTrack Track,
    TimelineClip Clip,
    MediaItem? Media,
    double SourceSeconds,
    string PreviewPath,
    string OriginalPath,
    double OriginalSeconds)
{
    /// <summary>Lautstärke als Faktor (0…1+) aus Clip- und Spurlautstärke.</summary>
    public double Volume => Track.Muted || Clip.AudioMuted || !Clip.HasAudio
        ? 0
        : Math.Pow(10, (Clip.VolumeDb + Track.VolumeDb) / 20);
}

/// <summary>
/// Plant die Wiedergabe der Timeline in der Vorschau: welche Datei an welcher Stelle läuft,
/// und wann sich das ändert (dann muss der Player umschalten).
/// </summary>
public static class PlaybackPlanner
{
    /// <summary>Das sichtbare Bild: der Clip der obersten Videospur an dieser Stelle.</summary>
    public static PlaybackSource? VideoAt(MovieProject project, long frame) =>
        MulticamResolver.Resolve(NestFlattener.Flatten(project)).VideoTracks.Reverse()
            .Where(t => !t.Muted)
            .Select(t => (Track: t, Clip: t.ClipAt(frame)))
            .Where(x => x.Clip is { HasVideo: true })
            .Select(x => Source(project, x.Track, x.Clip!, frame))
            .FirstOrDefault();

    /// <summary>Alles, was an dieser Stelle zu hören ist (Kameraton von V1 und alle Tonspuren).</summary>
    public static IReadOnlyList<PlaybackSource> AudioAt(MovieProject project, long frame) =>
        MulticamResolver.Resolve(NestFlattener.Flatten(project)).Tracks
            .Select(t => (Track: t, Clip: t.ClipAt(frame)))
            .Where(x => x.Clip is { HasAudio: true })
            .Select(x => Source(project, x.Track, x.Clip!, frame))
            .ToList();

    /// <summary>Nächste Stelle nach <paramref name="frame"/>, an der ein Clip beginnt oder endet.</summary>
    public static long NextChange(MovieProject project, long frame) =>
        project.AllClips.SelectMany(c => new[] { c.StartFrame, c.EndFrame })
            .Where(f => f > frame)
            .DefaultIfEmpty(Math.Max(frame + 1, project.DurationFrames))
            .Min();

    /// <summary>Datei und Zeit darin für ein Timeline-Bild eines Clips (berücksichtigt geteilte Aufnahmen).</summary>
    public static (string Path, double Seconds) SourceFileAt(TimelineClip clip, long frame, Time.FrameRate rate)
    {
        var seconds = clip.SourceTimeAt(frame, rate);
        if (clip.SourcePaths.Count > 1 && clip.PartDurations.Count == clip.SourcePaths.Count)
        {
            double start = 0;
            for (var i = 0; i < clip.SourcePaths.Count; i++)
            {
                if (seconds < start + clip.PartDurations[i] || i == clip.SourcePaths.Count - 1)
                {
                    return (clip.SourcePaths[i], seconds - start);
                }

                start += clip.PartDurations[i];
            }
        }

        return (clip.PrimaryPath, seconds);
    }

    /// <summary>Farbumrechnung für die Standbild-Vorschau (wie beim Export).</summary>
    public static string? PreviewColorFilter(TimelineClip clip) => FfmpegArgs.ColorFilter(clip);

    /// <summary>
    /// Kompletter Vorschau-Filter für ein Standbild: Farbumrechnung, auf Projektformat bringen und die
    /// Bildlage (Zoom, Position, Drehung, Deckkraft) an dieser Stelle – wie im Export.
    /// </summary>
    public static string? PreviewFilter(MovieProject project, PlaybackSource source, long frame, int height)
    {
        height = Math.Max(2, height / 2 * 2); // gerade Höhe – 4:2:0-Material verträgt keine ungeraden Maße
        var settings = project.Settings;
        var width = Math.Max(2, (int)Math.Round(settings.Width * (double)height / settings.Height / 2) * 2);
        var filters = new List<string>();
        if (FfmpegArgs.ColorFilter(source.Clip) is { } color)
        {
            filters.Add(color);
        }

        var titles = TitleRenderer.StaticAt(project, frame, width, height);
        if (!source.Clip.HasTransform && titles.Count == 0)
        {
            return filters.Count == 0 ? null : string.Join(',', filters);
        }

        filters.Add($"scale={width}:{height}:force_original_aspect_ratio=decrease");
        filters.Add($"pad={width}:{height}:(ow-iw)/2:(oh-ih)/2");
        filters.AddRange(TransformFilters.StaticAt(source.Clip, frame - source.Clip.StartFrame, width, height));
        filters.AddRange(titles);
        return string.Join(',', filters);
    }

    /// <summary>Titel über schwarzem Bild (für Stellen ohne Video) – null, wenn dort auch kein Titel ist.</summary>
    public static string? TitleOnlyFilter(MovieProject project, long frame, int height)
    {
        height = Math.Max(2, height / 2 * 2);
        var settings = project.Settings;
        var width = Math.Max(2, (int)Math.Round(settings.Width * (double)height / settings.Height / 2) * 2);
        var titles = TitleRenderer.StaticAt(project, frame, width, height);
        return titles.Count == 0 ? null : $"scale={width}:{height}," + string.Join(',', titles);
    }

    private static PlaybackSource Source(MovieProject project, TimelineTrack track, TimelineClip clip, long frame)
    {
        var media = project.FindMedia(clip.MediaId);
        var seconds = clip.SourceTimeAt(frame, project.Settings.FrameRate);
        var previewPath = media?.PreviewPath ?? clip.PrimaryPath;

        // Für Standbilder aus dem Original: bei geteilten Aufnahmen die richtige Teildatei und Zeit darin finden.
        var originalPath = clip.PrimaryPath;
        var originalSeconds = seconds;
        if (clip.SourcePaths.Count > 1 && clip.PartDurations.Count == clip.SourcePaths.Count)
        {
            double start = 0;
            for (var i = 0; i < clip.SourcePaths.Count; i++)
            {
                if (seconds < start + clip.PartDurations[i] || i == clip.SourcePaths.Count - 1)
                {
                    originalPath = clip.SourcePaths[i];
                    originalSeconds = seconds - start;
                    break;
                }

                start += clip.PartDurations[i];
            }
        }

        return new PlaybackSource(track, clip, media, seconds, previewPath, originalPath, originalSeconds);
    }
}
