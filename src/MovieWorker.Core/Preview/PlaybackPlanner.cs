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
        project.VideoTracks.Reverse()
            .Where(t => !t.Muted)
            .Select(t => (Track: t, Clip: t.ClipAt(frame)))
            .Where(x => x.Clip is { HasVideo: true })
            .Select(x => Source(project, x.Track, x.Clip!, frame))
            .FirstOrDefault();

    /// <summary>Alles, was an dieser Stelle zu hören ist (Kameraton von V1 und alle Tonspuren).</summary>
    public static IReadOnlyList<PlaybackSource> AudioAt(MovieProject project, long frame) =>
        project.Tracks
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

    /// <summary>Farbumrechnung für die Standbild-Vorschau (wie beim Export).</summary>
    public static string? PreviewColorFilter(TimelineClip clip) => FfmpegArgs.ColorFilter(clip);

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
