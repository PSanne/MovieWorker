namespace MovieWorker.Core.Timeline;

/// <summary>Wie ein Clip aus dem Quellmonitor in die Timeline kommt (wie in Profi-Schnittprogrammen).</summary>
public enum EditMode
{
    /// <summary>Ans Ende der Spur anhängen.</summary>
    Append,

    /// <summary>Am Abspielkopf einfügen – alles dahinter rückt auf allen Spuren nach (Ripple).</summary>
    Insert,

    /// <summary>Am Abspielkopf überschreiben – was dort liegt, wird ersetzt; nichts verschiebt sich.</summary>
    Overwrite,

    /// <summary>Den ausgewählten Clip ersetzen (gleiche Position und Länge).</summary>
    Replace,

    /// <summary>Am Abspielkopf auf die nächste freie Videospur darüber legen (z. B. B-Roll).</summary>
    PlaceOnTop,
}

public static class EditModeInfo
{
    public static string Label(EditMode mode) => mode switch
    {
        EditMode.Append => "Anhängen",
        EditMode.Insert => "Einfügen",
        EditMode.Overwrite => "Überschreiben",
        EditMode.Replace => "Ersetzen",
        _ => "Oben platzieren",
    };

    public static string Shortcut(EditMode mode) => mode switch
    {
        EditMode.Insert => "F9",
        EditMode.Overwrite => "F10",
        EditMode.Replace => "F11",
        EditMode.PlaceOnTop => "F12",
        _ => "Umschalt+F12",
    };
}
