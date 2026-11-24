namespace MovieWorker.Core.Timeline;

public enum TitleStyle
{
    /// <summary>Großer Titel in der Bildmitte.</summary>
    Headline,

    /// <summary>Untertitel unten mittig, mit halbtransparentem Kasten.</summary>
    Subtitle,

    /// <summary>Bauchbinde unten links: Name und darunter kleiner die Funktion.</summary>
    LowerThird,

    /// <summary>Abspann: Text rollt von unten nach oben durchs Bild.</summary>
    Credits,

    /// <summary>Kleine Einblendung oben links (Ort, Datum …).</summary>
    Caption,
}

public enum TitleAnimation
{
    None,
    Fade,
    SlideUp,
    SlideLeft,
    Typewriter,
}

public enum TitleAlign
{
    Left,
    Center,
    Right,
}

/// <summary>Inhalt und Gestaltung eines Titels (Titelwerkzeug).</summary>
public sealed class TitleSpec
{
    public string Text { get; set; } = "Titel";

    public TitleStyle Style { get; set; } = TitleStyle.Headline;

    public string FontFamily { get; set; } = "Segoe UI";

    public bool Bold { get; set; } = true;

    /// <summary>Schriftgröße der ersten Zeile in % der Bildhöhe.</summary>
    public double SizePercent { get; set; } = 9;

    /// <summary>Größe der weiteren Zeilen relativ zur ersten (z. B. 0,6 bei Bauchbinden).</summary>
    public double SecondaryScale { get; set; } = 1;

    public string Color { get; set; } = "white";

    public bool Box { get; set; }

    public string BoxColor { get; set; } = "black";

    public double BoxOpacity { get; set; } = 0.55;

    /// <summary>Mittelpunkt des Textblocks in % (X von links, Y von oben).</summary>
    public double PositionX { get; set; } = 50;

    public double PositionY { get; set; } = 50;

    public TitleAlign Align { get; set; } = TitleAlign.Center;

    public TitleAnimation AnimationIn { get; set; } = TitleAnimation.Fade;

    public TitleAnimation AnimationOut { get; set; } = TitleAnimation.Fade;

    public double AnimationSeconds { get; set; } = 0.6;

    public TitleSpec Clone() => (TitleSpec)MemberwiseClone();

    public IReadOnlyList<string> Lines =>
        Text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToList();

    /// <summary>Vorlagen für typische Titel.</summary>
    public static TitleSpec FromStyle(TitleStyle style) => style switch
    {
        TitleStyle.Subtitle => new TitleSpec
        {
            Style = style, Text = "Untertitel", SizePercent = 5, Bold = false, Box = true, PositionY = 88,
            AnimationIn = TitleAnimation.Fade, AnimationOut = TitleAnimation.Fade, AnimationSeconds = 0.2,
        },
        TitleStyle.LowerThird => new TitleSpec
        {
            Style = style, Text = "Name Nachname\nFunktion", SizePercent = 6, SecondaryScale = 0.62, Box = true, BoxOpacity = 0.6,
            Align = TitleAlign.Left, PositionX = 7, PositionY = 80, AnimationIn = TitleAnimation.SlideLeft, AnimationOut = TitleAnimation.Fade,
        },
        TitleStyle.Credits => new TitleSpec
        {
            Style = style, Text = "Ein Film von\nName\n\nKamera\nCanon EOS R6 Mark II\nCanon EOS 70D\n\nSchnitt\nMovieWorker",
            SizePercent = 5, SecondaryScale = 0.8, Bold = false, AnimationIn = TitleAnimation.None, AnimationOut = TitleAnimation.None,
        },
        TitleStyle.Caption => new TitleSpec
        {
            Style = style, Text = "Ort, Datum", SizePercent = 4, Bold = false, Align = TitleAlign.Left, PositionX = 5, PositionY = 8,
            AnimationIn = TitleAnimation.Typewriter, AnimationOut = TitleAnimation.Fade,
        },
        _ => new TitleSpec { Style = TitleStyle.Headline, Text = "Titel", SizePercent = 10, AnimationIn = TitleAnimation.SlideUp },
    };
}
