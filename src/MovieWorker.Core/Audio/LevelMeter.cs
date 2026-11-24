using MovieWorker.Core.Media;
using MovieWorker.Core.Timeline;

namespace MovieWorker.Core.Audio;

/// <summary>Pegelanzeige im Mischpult: Spitzenpegel einer Spur an einer Stelle, aus den Wellenformen ihrer Clips.</summary>
public static class LevelMeter
{
    /// <summary>Untergrenze der Anzeige (dBFS).</summary>
    public const double Floor = -60;

    public static double TrackLevelDb(MovieProject project, TimelineTrack track, long frame, Func<TimelineClip, WaveformData?> waveform)
    {
        if (!project.IsAudible(track) || track.ClipAt(frame) is not { HasAudio: true, AudioMuted: false } clip || waveform(clip) is not { } wave)
        {
            return Floor;
        }

        var seconds = clip.SourceTimeAt(frame, project.Settings.FrameRate);
        var peak = wave.PeakBetween(Math.Max(0, seconds - 0.05), seconds + 0.05);
        if (peak <= 0)
        {
            return Floor;
        }

        var db = 20 * Math.Log10(peak) + clip.VolumeDb + track.VolumeDb + project.Master.VolumeDb;
        return Math.Clamp(db, Floor, 6);
    }

    /// <summary>Anteil 0…1 für einen Balken (−60 dB … 0 dB).</summary>
    public static double ToBar(double db) => Math.Clamp((db - Floor) / -Floor, 0, 1);
}
