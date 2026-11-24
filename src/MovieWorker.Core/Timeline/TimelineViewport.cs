using MovieWorker.Core.Time;

namespace MovieWorker.Core.Timeline;

/// <summary>
/// Umrechnung zwischen Timeline-Bildern und Bildschirm-Pixeln (Zoom und waagerechtes Scrollen).
/// </summary>
public sealed class TimelineViewport
{
    public const double MinPixelsPerFrame = 0.0005;
    public const double MaxPixelsPerFrame = 40;

    private double _pixelsPerFrame = 2;
    private double _scrollFrame;

    /// <summary>Breite des Zeichenbereichs (ohne Spurköpfe) in Pixeln.</summary>
    public double Width { get; set; } = 1000;

    public double PixelsPerFrame
    {
        get => _pixelsPerFrame;
        set => _pixelsPerFrame = Math.Clamp(value, MinPixelsPerFrame, MaxPixelsPerFrame);
    }

    /// <summary>Erstes sichtbares Bild (darf gebrochen sein).</summary>
    public double ScrollFrame
    {
        get => _scrollFrame;
        set => _scrollFrame = Math.Max(0, value);
    }

    public double VisibleFrames => Width / PixelsPerFrame;

    public double FrameToX(double frame) => (frame - ScrollFrame) * PixelsPerFrame;

    public double XToFrame(double x) => ScrollFrame + x / PixelsPerFrame;

    /// <summary>Zoomt so, dass das Bild unter <paramref name="x"/> an derselben Stelle bleibt.</summary>
    public void ZoomAround(double x, double factor)
    {
        var anchor = XToFrame(x);
        PixelsPerFrame *= factor;
        ScrollFrame = anchor - x / PixelsPerFrame;
    }

    /// <summary>Passt den Zoom so an, dass <paramref name="durationFrames"/> ganz sichtbar ist (mit etwas Rand).</summary>
    public void Fit(long durationFrames)
    {
        var frames = Math.Max(durationFrames, 1) * 1.05;
        PixelsPerFrame = Width / frames;
        ScrollFrame = 0;
    }

    /// <summary>Scrollt so, dass <paramref name="frame"/> sichtbar ist (z. B. beim Abspielen).</summary>
    public bool EnsureVisible(double frame, double marginRatio = 0.1)
    {
        var margin = VisibleFrames * marginRatio;
        if (frame < ScrollFrame + margin * 0.2)
        {
            ScrollFrame = frame - margin;
            return true;
        }

        if (frame > ScrollFrame + VisibleFrames - margin * 0.2)
        {
            ScrollFrame = frame - VisibleFrames + margin;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Wählt den Abstand der Lineal-Beschriftung so, dass zwischen zwei Marken mindestens
    /// <paramref name="minPixels"/> Platz ist – in „runden“ Werten (Bilder, Sekunden, Minuten).
    /// </summary>
    public long RulerStep(FrameRate rate, double minPixels = 90)
    {
        var fps = rate.NominalFps;
        long[] candidates =
        [
            1, 2, 5, 10,
            fps, 2L * fps, 5L * fps, 10L * fps, 15L * fps, 30L * fps,
            60L * fps, 120L * fps, 300L * fps, 600L * fps, 900L * fps, 1800L * fps, 3600L * fps,
        ];
        foreach (var step in candidates.Where(s => s > 0).Distinct().Order())
        {
            if (step * PixelsPerFrame >= minPixels)
            {
                return step;
            }
        }

        return 7200L * fps;
    }
}
