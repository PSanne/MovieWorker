namespace MovieWorker.Core.Timeline;

/// <summary>Übergang zwischen zwei Clips (wird am Anfang des zweiten Clips gespeichert).</summary>
public enum TransitionType
{
    Crossfade,
    FadeBlack,
    FadeWhite,
    Dissolve,
    WipeLeft,
    WipeRight,
    WipeUp,
    WipeDown,
    SlideLeft,
    SlideRight,
    CircleOpen,
    CircleClose,
    Radial,
    Pixelize,
}

public sealed class Transition
{
    public TransitionType Type { get; set; } = TransitionType.Crossfade;

    public long DurationFrames { get; set; } = 25;

    public Transition Clone() => (Transition)MemberwiseClone();

    /// <summary>Name des Übergangs im ffmpeg-Filter <c>xfade</c>.</summary>
    public static string FfmpegName(TransitionType type) => type switch
    {
        TransitionType.FadeBlack => "fadeblack",
        TransitionType.FadeWhite => "fadewhite",
        TransitionType.Dissolve => "dissolve",
        TransitionType.WipeLeft => "wipeleft",
        TransitionType.WipeRight => "wiperight",
        TransitionType.WipeUp => "wipeup",
        TransitionType.WipeDown => "wipedown",
        TransitionType.SlideLeft => "slideleft",
        TransitionType.SlideRight => "slideright",
        TransitionType.CircleOpen => "circleopen",
        TransitionType.CircleClose => "circleclose",
        TransitionType.Radial => "radial",
        TransitionType.Pixelize => "pixelize",
        _ => "fade",
    };

    public static string Label(TransitionType type) => type switch
    {
        TransitionType.FadeBlack => "Abblende über Schwarz",
        TransitionType.FadeWhite => "Abblende über Weiß",
        TransitionType.Dissolve => "Auflösen (körnig)",
        TransitionType.WipeLeft => "Wischen nach links",
        TransitionType.WipeRight => "Wischen nach rechts",
        TransitionType.WipeUp => "Wischen nach oben",
        TransitionType.WipeDown => "Wischen nach unten",
        TransitionType.SlideLeft => "Schieben nach links",
        TransitionType.SlideRight => "Schieben nach rechts",
        TransitionType.CircleOpen => "Kreis öffnen",
        TransitionType.CircleClose => "Kreis schließen",
        TransitionType.Radial => "Uhrzeiger",
        TransitionType.Pixelize => "Verpixeln",
        _ => "Kreuzblende",
    };
}
