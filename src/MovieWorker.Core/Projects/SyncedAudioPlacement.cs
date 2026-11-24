using MovieWorker.Core.Time;
using MovieWorker.Core.Timeline;

namespace MovieWorker.Core.Projects;

/// <summary>Wo der synchronisierte externe Ton zu einem Videoclip in der Timeline liegt.</summary>
public sealed record SyncedAudioPlacement(long StartOffsetFrames, double SourceIn, long LengthFrames)
{
    /// <summary>
    /// Berechnet den passenden Ausschnitt des externen Tons für einen Videoclip. Beginnt der Rekorder später
    /// als die Kamera, fängt der Ton entsprechend später an; endet er früher, ist er kürzer.
    /// Liefert null, wenn sich Bild und Ton nicht überschneiden.
    /// </summary>
    public static SyncedAudioPlacement? For(TimelineClip videoClip, double offsetSeconds, double audioDuration, FrameRate rate)
    {
        var sourceStart = videoClip.SourceIn + offsetSeconds;
        var sourceLength = videoClip.SourceLength(rate);
        var skip = Math.Max(0, -sourceStart);                 // Rekorder lief noch nicht
        var available = audioDuration - Math.Max(0, sourceStart); // was der Rekorder ab dort noch hat
        var length = Math.Min(sourceLength - skip, available);
        if (length <= rate.FrameDuration)
        {
            return null;
        }

        return new SyncedAudioPlacement(
            rate.SecondsToFrames(skip / videoClip.Speed),
            Math.Max(0, sourceStart),
            Math.Max(1, rate.SecondsToFrames(length / videoClip.Speed)));
    }
}
