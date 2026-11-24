namespace MovieWorker.Core.Timeline;

/// <summary>Wie Ziehen in der Timeline wirkt.</summary>
public enum TrimMode
{
    /// <summary>Normal: Rand ziehen trimmt nur diesen Clip, Ziehen verschiebt ihn.</summary>
    Normal,

    /// <summary>Ripple: Rand trimmen, alles dahinter rückt mit (keine Lücken).</summary>
    Ripple,

    /// <summary>Roll: Schnittpunkt zwischen zwei Clips verschieben (einer wird länger, der andere kürzer).</summary>
    Roll,

    /// <summary>Slip: anderer Ausschnitt der Quelle bei gleicher Position und Länge.</summary>
    Slip,

    /// <summary>Slide: Clip zwischen seinen Nachbarn verschieben (Nachbarn passen sich an).</summary>
    Slide,
}

/// <summary>Grenzen eines Clips in Bildern (wie weit lässt er sich verlängern?).</summary>
internal static class ClipHandles
{
    /// <summary>Bilder, die vor dem Clipanfang in der Quelle noch vorhanden sind.</summary>
    public static long Head(TimelineClip clip, Time.FrameRate rate) =>
        clip.NestedTracks is not null
            ? (long)Math.Floor(clip.SourceIn / rate.FrameDuration + 1e-6)
            : (long)Math.Floor(clip.SourceIn / clip.Speed / rate.FrameDuration + 1e-6);

    /// <summary>Bilder, die nach dem Clipende in der Quelle noch vorhanden sind.</summary>
    public static long Tail(TimelineClip clip, Time.FrameRate rate) =>
        (long)Math.Floor(Math.Max(0, clip.SourceDuration - clip.SourceOut(rate)) / clip.Speed / rate.FrameDuration + 1e-6);
}

/// <summary>Öffentlicher Zugang zu den Clip-Reserven (für den Export).</summary>
public static class ClipHandlesAccess
{
    public static long Head(TimelineClip clip, Time.FrameRate rate) => ClipHandles.Head(clip, rate);

    public static long Tail(TimelineClip clip, Time.FrameRate rate) => ClipHandles.Tail(clip, rate);
}
