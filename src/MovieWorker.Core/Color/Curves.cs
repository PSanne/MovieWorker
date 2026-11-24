namespace MovieWorker.Core.Color;

public sealed class CurvePoint
{
    public double X { get; set; }

    public double Y { get; set; }
}

public enum CurveChannel
{
    Luma,
    Red,
    Green,
    Blue,
}

/// <summary>Gradationskurven: Luma (alle Kanäle) und je Kanal Rot/Grün/Blau, weich interpoliert.</summary>
public sealed class ColorCurves
{
    public List<CurvePoint> Luma { get; set; } = [];

    public List<CurvePoint> Red { get; set; } = [];

    public List<CurvePoint> Green { get; set; } = [];

    public List<CurvePoint> Blue { get; set; } = [];

    public ColorCurves Clone() => new()
    {
        Luma = Luma.Select(p => new CurvePoint { X = p.X, Y = p.Y }).ToList(),
        Red = Red.Select(p => new CurvePoint { X = p.X, Y = p.Y }).ToList(),
        Green = Green.Select(p => new CurvePoint { X = p.X, Y = p.Y }).ToList(),
        Blue = Blue.Select(p => new CurvePoint { X = p.X, Y = p.Y }).ToList(),
    };

    public List<CurvePoint> Points(CurveChannel channel) => channel switch
    {
        CurveChannel.Red => Red,
        CurveChannel.Green => Green,
        CurveChannel.Blue => Blue,
        _ => Luma,
    };

    public bool IsIdentity => IsLinear(Luma) && IsLinear(Red) && IsLinear(Green) && IsLinear(Blue);

    private static bool IsLinear(List<CurvePoint> points) => points.All(p => Math.Abs(p.X - p.Y) < 1e-6);

    public (double R, double G, double B) Apply(double r, double g, double b)
    {
        if (IsIdentity)
        {
            return (r, g, b);
        }

        r = Evaluate(Red, Evaluate(Luma, r));
        g = Evaluate(Green, Evaluate(Luma, g));
        b = Evaluate(Blue, Evaluate(Luma, b));
        return (r, g, b);
    }

    /// <summary>Wert der Kurve (Endpunkte 0/0 und 1/1 sind fest, monotone kubische Interpolation).</summary>
    public static double Evaluate(List<CurvePoint> userPoints, double x)
    {
        if (userPoints.Count == 0)
        {
            return x;
        }

        var points = new List<(double X, double Y)> { (0, 0) };
        points.AddRange(userPoints.Where(p => p.X > 0.001 && p.X < 0.999).OrderBy(p => p.X).Select(p => (p.X, Math.Clamp(p.Y, 0, 1))));
        var endpoint = userPoints.FirstOrDefault(p => p.X >= 0.999);
        points.Add((1, endpoint?.Y ?? 1));
        var start = userPoints.FirstOrDefault(p => p.X <= 0.001);
        if (start is not null)
        {
            points[0] = (0, Math.Clamp(start.Y, 0, 1));
        }

        x = Math.Clamp(x, 0, 1);
        var n = points.Count;
        var slopes = new double[n];
        var deltas = new double[n - 1];
        for (var i = 0; i < n - 1; i++)
        {
            deltas[i] = (points[i + 1].Y - points[i].Y) / Math.Max(1e-9, points[i + 1].X - points[i].X);
        }

        slopes[0] = deltas[0];
        slopes[n - 1] = deltas[n - 2];
        for (var i = 1; i < n - 1; i++)
        {
            slopes[i] = deltas[i - 1] * deltas[i] <= 0 ? 0 : (deltas[i - 1] + deltas[i]) / 2;
        }

        for (var i = 0; i < n - 1; i++)
        {
            if (x > points[i + 1].X && i < n - 2)
            {
                continue;
            }

            var h = points[i + 1].X - points[i].X;
            var t = h <= 0 ? 0 : (x - points[i].X) / h;
            var t2 = t * t;
            var t3 = t2 * t;
            var y = (2 * t3 - 3 * t2 + 1) * points[i].Y + (t3 - 2 * t2 + t) * h * slopes[i]
                    + (-2 * t3 + 3 * t2) * points[i + 1].Y + (t3 - t2) * h * slopes[i + 1];
            return Math.Clamp(y, 0, 1);
        }

        return x;
    }
}

/// <summary>
/// Sekundäre Farbkorrektur: wählt einen Farbbereich (Farbton, Sättigung, Helligkeit, mit weichem Übergang)
/// und verändert nur ihn – z. B. den Himmel blauer oder Hauttöne wärmer.
/// </summary>
public sealed class SecondaryCorrection
{
    public bool Enabled { get; set; }

    /// <summary>Mitte des Farbtons in Grad (0 = Rot, 120 = Grün, 240 = Blau).</summary>
    public double HueCenter { get; set; } = 210;

    /// <summary>Breite des Farbtonbereichs in Grad.</summary>
    public double HueWidth { get; set; } = 40;

    public double SaturationMin { get; set; } = 0.15;

    public double SaturationMax { get; set; } = 1;

    public double LumaMin { get; set; }

    public double LumaMax { get; set; } = 1;

    /// <summary>Weichheit der Auswahlgrenzen (0 … 1).</summary>
    public double Softness { get; set; } = 0.3;

    /// <summary>Farbton im Bereich verschieben (Grad).</summary>
    public double HueShift { get; set; }

    /// <summary>Sättigung im Bereich (100 = unverändert).</summary>
    public double Saturation { get; set; } = 100;

    /// <summary>Helligkeit im Bereich (−1 … +1).</summary>
    public double Brightness { get; set; }

    /// <summary>Nur die Auswahl zeigen (weiß = ausgewählt) – zum Einstellen.</summary>
    public bool ShowSelection { get; set; }

    public SecondaryCorrection Clone() => (SecondaryCorrection)MemberwiseClone();

    /// <summary>Wie stark eine Farbe zur Auswahl gehört (0 … 1).</summary>
    public double Weight(double h, double s, double l)
    {
        var soft = Math.Max(0.001, Softness);
        var hueDistance = Math.Abs(((h - HueCenter) % 360 + 540) % 360 - 180);
        var hueWeight = Smooth((HueWidth / 2 + HueWidth * soft - hueDistance) / Math.Max(1, HueWidth * soft * 2));
        var satWeight = Range(s, SaturationMin, SaturationMax, soft * 0.2);
        var lumWeight = Range(l, LumaMin, LumaMax, soft * 0.2);
        return hueWeight * satWeight * lumWeight;
    }

    public (double R, double G, double B) Apply(double r, double g, double b)
    {
        if (!Enabled)
        {
            return (r, g, b);
        }

        var (h, s, l) = ToHsl(r, g, b);
        var weight = Weight(h, s, l);
        if (ShowSelection)
        {
            return (weight, weight, weight);
        }

        if (weight <= 0)
        {
            return (r, g, b);
        }

        var (nr, ng, nb) = FromHsl((h + HueShift + 360) % 360, Math.Clamp(s * Saturation / 100, 0, 1), Math.Clamp(l + Brightness * 0.3, 0, 1));
        return (r + (nr - r) * weight, g + (ng - g) * weight, b + (nb - b) * weight);
    }

    private static double Smooth(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }

    private static double Range(double v, double min, double max, double soft)
    {
        soft = Math.Max(0.001, soft);
        return Smooth((v - (min - soft)) / (2 * soft)) * Smooth(((max + soft) - v) / (2 * soft));
    }

    public static (double H, double S, double L) ToHsl(double r, double g, double b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max - min < 1e-9)
        {
            return (0, 0, l);
        }

        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h;
        if (max == r)
        {
            h = (g - b) / d + (g < b ? 6 : 0);
        }
        else if (max == g)
        {
            h = (b - r) / d + 2;
        }
        else
        {
            h = (r - g) / d + 4;
        }

        return (h * 60, s, l);
    }

    public static (double R, double G, double B) FromHsl(double h, double s, double l)
    {
        if (s < 1e-9)
        {
            return (l, l, l);
        }

        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;
        double Hue(double t)
        {
            t = (t % 1 + 1) % 1;
            return t < 1.0 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p;
        }

        var hh = h / 360;
        return (Hue(hh + 1.0 / 3), Hue(hh), Hue(hh - 1.0 / 3));
    }
}
