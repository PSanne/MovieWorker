using MovieWorker.Core.Projects;
using MovieWorker.Core.Time;

namespace MovieWorker.Core.Timeline;

/// <summary>Eine Kamera (Perspektive) eines Multicam-Clips.</summary>
public sealed class MulticamAngle
{
    public Guid? MediaId { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<string> SourcePaths { get; set; } = [];

    public List<double> PartDurations { get; set; } = [];

    public double SourceDuration { get; set; }

    /// <summary>Zeit in dieser Kamera = Multicam-Zeit + Offset (Sekunden).</summary>
    public double OffsetSeconds { get; set; }

    public bool HasAudio { get; set; }

    public MulticamAngle Clone() => new()
    {
        MediaId = MediaId, Name = Name, SourcePaths = [.. SourcePaths], PartDurations = [.. PartDurations],
        SourceDuration = SourceDuration, OffsetSeconds = OffsetSeconds, HasAudio = HasAudio,
    };

    public static MulticamAngle FromMedia(MediaItem media, double offset) => new()
    {
        MediaId = media.Id,
        Name = media.Name,
        SourcePaths = media.Parts.Select(p => p.Path).ToList(),
        PartDurations = media.Parts.Select(p => p.Duration.TotalSeconds).ToList(),
        SourceDuration = media.Duration.TotalSeconds,
        OffsetSeconds = offset,
        HasAudio = media.First.HasAudio,
    };
}

/// <summary>Erstellen und Auflösen von Multicam-Clips (mehrere synchrone Kameras, eine davon ist sichtbar).</summary>
public static class MulticamResolver
{
    /// <summary>
    /// Erstellt einen Multicam-Clip. <paramref name="offsets"/>[i]: Zeit in Kamera i = Zeit in Kamera 0 + Offset
    /// (z. B. aus der Ton-Synchronisation). Der Clip umfasst die Zeit, in der alle Kameras laufen.
    /// </summary>
    public static TimelineClip Create(IReadOnlyList<MediaItem> cameras, IReadOnlyList<double> offsets, FrameRate rate, string name)
    {
        if (cameras.Count < 2 || offsets.Count != cameras.Count)
        {
            throw new ArgumentException("Ein Multicam-Clip braucht mindestens zwei Kameras mit je einem Versatz.");
        }

        var angles = cameras.Select((m, i) => MulticamAngle.FromMedia(m, offsets[i])).ToList();
        var start = angles.Max(a => -a.OffsetSeconds);
        var end = angles.Min(a => a.SourceDuration - a.OffsetSeconds);
        if (end - start < rate.FrameDuration)
        {
            (start, end) = (0, angles[0].SourceDuration); // keine gemeinsame Zeit – erste Kamera als Maß
        }

        start = Math.Max(0, start);
        return new TimelineClip
        {
            Name = name,
            MulticamAngles = angles,
            ActiveAngle = 0,
            AudioAngle = Math.Max(0, angles.FindIndex(a => a.HasAudio)),
            SourceDuration = angles[0].SourceDuration,
            SourceIn = start,
            LengthFrames = Math.Max(1, rate.SecondsToFrames(end - start)),
            HasVideo = true,
            HasAudio = angles.Any(a => a.HasAudio),
        };
    }

    /// <summary>Ersetzt Multicam-Clips durch normale Clips der gewählten Kamera; der Ton kommt von der Ton-Kamera.</summary>
    public static MovieProject Resolve(MovieProject project)
    {
        if (!project.AllClips.Any(c => c.IsMulticam))
        {
            return project;
        }

        var copy = project.Clone();
        var audioTracks = new Dictionary<Guid, TimelineTrack>();
        foreach (var track in copy.Tracks.Where(t => t.Kind == TrackKind.Video).ToList())
        {
            foreach (var clip in track.Clips.Where(c => c.IsMulticam).ToList())
            {
                var angles = clip.MulticamAngles!;
                var video = angles[Math.Clamp(clip.ActiveAngle, 0, angles.Count - 1)];
                var resolved = ApplyAngle(clip, video);
                resolved.HasAudio = false;
                track.Clips[track.Clips.IndexOf(clip)] = resolved;

                var audioAngle = angles[Math.Clamp(clip.AudioAngle, 0, angles.Count - 1)];
                if (audioAngle.HasAudio && !clip.AudioMuted && !track.Muted)
                {
                    if (!audioTracks.TryGetValue(track.Id, out var audioTrack))
                    {
                        audioTrack = new TimelineTrack { Kind = TrackKind.Audio, Name = $"Multicam-Ton {track.Name}", VolumeDb = track.VolumeDb };
                        copy.Tracks.Add(audioTrack);
                        audioTracks[track.Id] = audioTrack;
                    }

                    var sound = ApplyAngle(clip, audioAngle);
                    sound.Id = Guid.NewGuid();
                    sound.HasVideo = false;
                    sound.HasAudio = true;
                    sound.TransitionIn = null;
                    audioTrack.Clips.Add(sound);
                }
            }
        }

        return copy;
    }

    private static TimelineClip ApplyAngle(TimelineClip clip, MulticamAngle angle)
    {
        var result = clip.Clone();
        result.MulticamAngles = null;
        result.MediaId = angle.MediaId;
        result.SourcePaths = [.. angle.SourcePaths];
        result.PartDurations = [.. angle.PartDurations];
        result.SourceDuration = angle.SourceDuration;
        result.SourceIn = Math.Max(0, clip.SourceIn + angle.OffsetSeconds);
        result.Name = $"{clip.Name} ({angle.Name})";
        return result;
    }
}
