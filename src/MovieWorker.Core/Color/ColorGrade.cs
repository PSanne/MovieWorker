using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MovieWorker.Core.Color;

/// <summary>Ein Farbrad: Farbverschiebung (R, G, B um 0) plus Helligkeit (Master).</summary>
public sealed class ColorWheel
{
    public double R { get; set; }

    public double G { get; set; }

    public double B { get; set; }

    public double Master { get; set; }

    public ColorWheel Clone() => (ColorWheel)MemberwiseClone();

    public bool IsNeutral => Math.Abs(R) < 1e-6 && Math.Abs(G) < 1e-6 && Math.Abs(B) < 1e-6 && Math.Abs(Master) < 1e-6;

    /// <summary>Setzt die Farbverschiebung aus Winkel (Grad, 0 = Rot) und Stärke (0…1).</summary>
    public void SetFromPolar(double hueDegrees, double amount)
    {
        var h = hueDegrees * Math.PI / 180;
        // Richtung im Farbkreis → RGB-Abweichung vom Mittel (Summe 0, damit die Helligkeit gleich bleibt).
        R = amount * Math.Cos(h);
        G = amount * Math.Cos(h - 2 * Math.PI / 3);
        B = amount * Math.Cos(h + 2 * Math.PI / 3);
    }

    public (double Hue, double Amount) ToPolar()
    {
        // Umkehrung von SetFromPolar (R, G, B sind Projektionen auf drei 120°-Achsen).
        var x = R - (G + B) / 2;
        var y = (G - B) * Math.Sqrt(3) / 2;
        var amount = Math.Sqrt(x * x + y * y) / 1.5;
        var hue = Math.Atan2(y, x) * 180 / Math.PI;
        return ((hue + 360) % 360, amount);
    }
}

/// <summary>Verfahren für die Umrechnung HDR → SDR (Tone-Mapping).</summary>
public enum ToneMapping
{
    /// <summary>Filmisch, weiche Lichter (Standard).</summary>
    Hable,

    /// <summary>Hält Mitteltöne nahe am Original.</summary>
    Mobius,

    /// <summary>Einfach, eher flach.</summary>
    Reinhard,

    /// <summary>Hartes Abschneiden (hell, aber Lichter fressen aus).</summary>
    Clip,
}

/// <summary>
/// Farbkorrektur eines Clips. Wird als 3D-LUT berechnet und über <c>lut3d</c> angewendet – dadurch sehen
/// Vorschau und Export gleich aus, und 10-Bit-Material behält seine Abstufungen.
/// Reihenfolge: Farbabgleich → Belichtung → Weißabgleich → Lift/Gamma/Gain → Kontrast → Sättigung.
/// </summary>
public sealed class ColorGrade
{
    /// <summary>Belichtung in Blendenstufen (−3 … +3).</summary>
    public double Exposure { get; set; }

    /// <summary>Kontrast in % (−100 … +100).</summary>
    public double Contrast { get; set; }

    /// <summary>Sättigung in % (0 = schwarzweiß, 100 = unverändert, 200 = doppelt).</summary>
    public double Saturation { get; set; } = 100;

    /// <summary>Farbtemperatur −100 (kühler/blauer) … +100 (wärmer/gelber).</summary>
    public double Temperature { get; set; }

    /// <summary>Tönung −100 (grüner) … +100 (magenta).</summary>
    public double Tint { get; set; }

    /// <summary>Schatten.</summary>
    public ColorWheel Lift { get; set; } = new();

    /// <summary>Mitteltöne.</summary>
    public ColorWheel Gamma { get; set; } = new();

    /// <summary>Lichter.</summary>
    public ColorWheel Gain { get; set; } = new();

    /// <summary>Farbabgleich (z. B. 70D an R6 angeglichen): Verstärkung je Kanal.</summary>
    public double MatchGainR { get; set; } = 1;

    public double MatchGainG { get; set; } = 1;

    public double MatchGainB { get; set; } = 1;

    /// <summary>Farbabgleich: Verschiebung je Kanal.</summary>
    public double MatchOffsetR { get; set; }

    public double MatchOffsetG { get; set; }

    public double MatchOffsetB { get; set; }

    /// <summary>Tone-Mapping für HDR-Clips (PQ/HLG) beim SDR-Export.</summary>
    public ToneMapping ToneMap { get; set; } = ToneMapping.Hable;

    public ColorGrade Clone()
    {
        var copy = (ColorGrade)MemberwiseClone();
        copy.Lift = Lift.Clone();
        copy.Gamma = Gamma.Clone();
        copy.Gain = Gain.Clone();
        return copy;
    }

    public bool IsNeutral =>
        Math.Abs(Exposure) < 1e-6 && Math.Abs(Contrast) < 1e-6 && Math.Abs(Saturation - 100) < 1e-6
        && Math.Abs(Temperature) < 1e-6 && Math.Abs(Tint) < 1e-6 && Lift.IsNeutral && Gamma.IsNeutral && Gain.IsNeutral
        && Math.Abs(MatchGainR - 1) < 1e-6 && Math.Abs(MatchGainG - 1) < 1e-6 && Math.Abs(MatchGainB - 1) < 1e-6
        && Math.Abs(MatchOffsetR) < 1e-6 && Math.Abs(MatchOffsetG) < 1e-6 && Math.Abs(MatchOffsetB) < 1e-6;

    /// <summary>Wendet die Korrektur auf eine Farbe (0…1) an.</summary>
    public (double R, double G, double B) Apply(double r, double g, double b)
    {
        // 1. Farbabgleich
        r = r * MatchGainR + MatchOffsetR;
        g = g * MatchGainG + MatchOffsetG;
        b = b * MatchGainB + MatchOffsetB;

        // 2. Belichtung
        var exposure = Math.Pow(2, Exposure);
        r *= exposure;
        g *= exposure;
        b *= exposure;

        // 3. Weißabgleich
        var t = Temperature / 100;
        var tint = Tint / 100;
        r *= 1 + 0.25 * t;
        b *= 1 - 0.25 * t;
        g *= 1 - 0.15 * tint;

        // 4. Lift / Gamma / Gain (je Kanal: Farbrad + Master)
        r = Lgg(r, Lift.R, Gamma.R, Gain.R);
        g = Lgg(g, Lift.G, Gamma.G, Gain.G);
        b = Lgg(b, Lift.B, Gamma.B, Gain.B);

        // 5. Kontrast um die Mitte
        var c = 1 + Contrast / 100;
        r = (r - 0.5) * c + 0.5;
        g = (g - 0.5) * c + 0.5;
        b = (b - 0.5) * c + 0.5;

        // 6. Sättigung (Rec.709-Luma)
        var s = Saturation / 100;
        var y = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        r = y + (r - y) * s;
        g = y + (g - y) * s;
        b = y + (b - y) * s;
        return (Math.Clamp(r, 0, 1), Math.Clamp(g, 0, 1), Math.Clamp(b, 0, 1));
    }

    private double Lgg(double x, double liftColor, double gammaColor, double gainColor)
    {
        var lift = (Lift.Master + liftColor) * 0.25;
        var gain = Math.Max(0, 1 + (Gain.Master + gainColor) * 0.5);
        var gamma = Math.Max(0.1, 1 + (Gamma.Master + gammaColor) * 0.5);
        var v = gain * (x + lift * (1 - x));
        return Math.Pow(Math.Max(0, v), 1 / gamma);
    }

    /// <summary>Kurzer, stabiler Schlüssel für den LUT-Zwischenspeicher.</summary>
    public string Hash()
    {
        string N(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
        var text = string.Join('|', new[]
        {
            Exposure, Contrast, Saturation, Temperature, Tint,
            Lift.R, Lift.G, Lift.B, Lift.Master, Gamma.R, Gamma.G, Gamma.B, Gamma.Master, Gain.R, Gain.G, Gain.B, Gain.Master,
            MatchGainR, MatchGainG, MatchGainB, MatchOffsetR, MatchOffsetG, MatchOffsetB,
        }.Select(N));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }
}

/// <summary>Schreibt Farbkorrekturen als .cube-Dateien in einen Zwischenspeicher (gleiche Korrektur = gleiche Datei).</summary>
public static class GradeLutCache
{
    public const int Size = 33;

    /// <summary>Ordner für berechnete LUTs (änderbar, z. B. für Tests).</summary>
    public static string Folder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MovieWorker", "Farbkorrekturen");

    public static string Ensure(ColorGrade grade)
    {
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, $"grade_{grade.Hash()}.cube");
        if (!File.Exists(path))
        {
            var temp = path + "." + Guid.NewGuid().ToString("N")[..6] + ".tmp";
            File.WriteAllText(temp, ToCube(grade));
            File.Move(temp, path, overwrite: true);
        }

        return path;
    }

    public static string ToCube(ColorGrade grade, int size = Size)
    {
        var text = new StringBuilder();
        text.AppendLine("TITLE \"MovieWorker Farbkorrektur\"");
        text.AppendLine(CultureInfo.InvariantCulture, $"LUT_3D_SIZE {size}");
        for (var b = 0; b < size; b++)
        {
            for (var g = 0; g < size; g++)
            {
                for (var r = 0; r < size; r++)
                {
                    var (ro, go, bo) = grade.Apply(r / (double)(size - 1), g / (double)(size - 1), b / (double)(size - 1));
                    text.AppendLine(CultureInfo.InvariantCulture, $"{ro:0.000000} {go:0.000000} {bo:0.000000}");
                }
            }
        }

        return text.ToString();
    }
}
