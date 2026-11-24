using System.Globalization;
using MovieWorker.Core.Time;
using MovieWorker.Core.Timeline;

namespace MovieWorker.Core.Color;

public enum MaskShape
{
    Rectangle,
    Ellipse,
}

/// <summary>Maske eines Korrekturknotens: Rechteck oder Ellipse mit weicher Kante; Mitte auch per Tracking animiert.</summary>
public sealed class ColorMask
{
    public MaskShape Shape { get; set; } = MaskShape.Ellipse;

    /// <summary>Mitte in Bildanteilen (0 … 1).</summary>
    public double CenterX { get; set; } = 0.5;

    public double CenterY { get; set; } = 0.5;

    /// <summary>Größe in Bildanteilen (0 … 1).</summary>
    public double Width { get; set; } = 0.4;

    public double Height { get; set; } = 0.4;

    /// <summary>Weiche Kante (0 = hart … 1 = sehr weich).</summary>
    public double Feather { get; set; } = 0.3;

    /// <summary>Außen statt innen korrigieren (z. B. Vignette).</summary>
    public bool Invert { get; set; }

    /// <summary>Verfolgte Mitte (Keyframes je Bild, relativ zum Clipanfang).</summary>
    public KeyframeTrack? TrackX { get; set; }

    public KeyframeTrack? TrackY { get; set; }

    public ColorMask Clone() => new()
    {
        Shape = Shape, CenterX = CenterX, CenterY = CenterY, Width = Width, Height = Height, Feather = Feather, Invert = Invert,
        TrackX = TrackX?.Clone(), TrackY = TrackY?.Clone(),
    };

    public (double X, double Y) CenterAt(double frameInClip) =>
        (TrackX is { Keys.Count: > 0 } tx ? tx.ValueAt(frameInClip) : CenterX, TrackY is { Keys.Count: > 0 } ty ? ty.ValueAt(frameInClip) : CenterY);

    /// <summary>Deckung (0 … 1) an einer Stelle (u, v in Bildanteilen).</summary>
    public double Coverage(double u, double v, double frameInClip)
    {
        var (cx, cy) = CenterAt(frameInClip);
        var dx = Math.Abs(u - cx) / Math.Max(1e-6, Width / 2);
        var dy = Math.Abs(v - cy) / Math.Max(1e-6, Height / 2);
        var d = Shape == MaskShape.Ellipse ? Math.Sqrt(dx * dx + dy * dy) : Math.Max(dx, dy);
        var alpha = Feather <= 0.001 ? (d <= 1 ? 1 : 0) : Math.Clamp((1 - d) / Feather + 0.5, 0, 1);
        return Invert ? 1 - alpha : alpha;
    }

    /// <summary>geq-Ausdruck für die Deckung (0 … 1) mit X, Y, W, H, T.</summary>
    public string ToExpression(FrameRate rate)
    {
        string N(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
        var cx = TrackX is { Keys.Count: > 0 } tx ? tx.ToExpression("T", f => rate.FramesToSeconds(f)) : N(CenterX);
        var cy = TrackY is { Keys.Count: > 0 } ty ? ty.ToExpression("T", f => rate.FramesToSeconds(f)) : N(CenterY);
        var dx = $"(abs(X/W-({cx}))/{N(Math.Max(1e-6, Width / 2))})";
        var dy = $"(abs(Y/H-({cy}))/{N(Math.Max(1e-6, Height / 2))})";
        var d = Shape == MaskShape.Ellipse ? $"hypot({dx},{dy})" : $"max({dx},{dy})";
        var alpha = Feather <= 0.001 ? $"lte({d},1)" : $"clip((1-{d})/{N(Feather)}+0.5,0,1)";
        return Invert ? $"(1-{alpha})" : alpha;
    }
}

/// <summary>Ein Korrekturknoten: eigene Farbkorrektur, optional nur innerhalb einer Maske. Knoten wirken nacheinander.</summary>
public sealed class CorrectionNode
{
    public string Name { get; set; } = "Knoten";

    public bool Enabled { get; set; } = true;

    public ColorGrade Grade { get; set; } = new();

    public ColorMask? Mask { get; set; }

    public CorrectionNode Clone() => new() { Name = Name, Enabled = Enabled, Grade = Grade.Clone(), Mask = Mask?.Clone() };
}
