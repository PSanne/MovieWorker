using System.Globalization;
using System.Text.Json.Serialization;

namespace MovieWorker.Core.Time;

/// <summary>
/// Bildrate als exakter Bruch (z. B. 30000/1001 = 29,97 fps). Mit Brüchen statt Kommazahlen
/// bleiben Schnittpunkte auch nach Stunden Material bildgenau.
/// </summary>
public readonly record struct FrameRate
{
    [JsonConstructor]
    public FrameRate(int numerator, int denominator = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(numerator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);
        var gcd = Gcd(numerator, denominator);
        Numerator = numerator / gcd;
        Denominator = denominator / gcd;
    }

    public int Numerator { get; }

    public int Denominator { get; }

    public static FrameRate Fps23_976 => new(24000, 1001);
    public static FrameRate Fps24 => new(24);
    public static FrameRate Fps25 => new(25);
    public static FrameRate Fps29_97 => new(30000, 1001);
    public static FrameRate Fps30 => new(30);
    public static FrameRate Fps50 => new(50);
    public static FrameRate Fps59_94 => new(60000, 1001);
    public static FrameRate Fps60 => new(60);

    /// <summary>Bildrate als Kommazahl, z. B. 29,97002997.</summary>
    [JsonIgnore]
    public double Value => (double)Numerator / Denominator;

    /// <summary>Gerundete Bildrate für den Timecode (29,97 → 30).</summary>
    [JsonIgnore]
    public int NominalFps => (int)Math.Round(Value);

    /// <summary>NTSC-Raten 29,97 und 59,94 können Drop-Frame-Timecode verwenden.</summary>
    [JsonIgnore]
    public bool SupportsDropFrame => Denominator == 1001 && (Numerator == 30000 || Numerator == 60000);

    /// <summary>Dauer eines Bildes in Sekunden.</summary>
    [JsonIgnore]
    public double FrameDuration => (double)Denominator / Numerator;

    public double FramesToSeconds(long frames) => frames * (double)Denominator / Numerator;

    /// <summary>Rechnet Sekunden in die nächstgelegene Bildnummer um.</summary>
    public long SecondsToFrames(double seconds) => (long)Math.Round(seconds * Numerator / Denominator);

    /// <summary>Liest Angaben wie „30000/1001“, „25/1“, „25“ oder „29.97“ (Schreibweise von ffprobe).</summary>
    public static FrameRate Parse(string text)
    {
        if (!TryParse(text, out var rate))
        {
            throw new FormatException($"Ungültige Bildrate: „{text}“");
        }

        return rate;
    }

    public static bool TryParse(string? text, out FrameRate rate)
    {
        rate = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Trim().Split('/');
        if (parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var num)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var den))
        {
            if (num <= 0 || den <= 0)
            {
                return false;
            }

            rate = new FrameRate(num, den);
            return true;
        }

        if (parts.Length == 1
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && value > 0)
        {
            rate = FromDouble(value);
            return true;
        }

        return false;
    }

    /// <summary>Erkennt die üblichen NTSC-Raten (23,976 / 29,97 / 59,94 / 119,88) als exakte Brüche.</summary>
    public static FrameRate FromDouble(double value)
    {
        foreach (var nominal in new[] { 24, 30, 48, 60, 120, 240 })
        {
            if (Math.Abs(value - nominal * 1000.0 / 1001.0) < 0.005)
            {
                return new FrameRate(nominal * 1000, 1001);
            }
        }

        if (Math.Abs(value - Math.Round(value)) < 0.0005)
        {
            return new FrameRate((int)Math.Round(value));
        }

        return new FrameRate((int)Math.Round(value * 1000), 1000);
    }

    /// <summary>Schreibweise für ffmpeg, z. B. „30000/1001“.</summary>
    public string ToFfmpeg() => $"{Numerator}/{Denominator}";

    public override string ToString() =>
        Denominator == 1
            ? Numerator.ToString(CultureInfo.CurrentCulture)
            : Value.ToString("0.###", CultureInfo.CurrentCulture);

    private static int Gcd(int a, int b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }
}
