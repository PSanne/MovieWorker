using System.Globalization;
using MovieWorker.Core.Color;

namespace MovieWorker.Core.Effects;

public enum EffectKind
{
    /// <summary>Greenscreen/Bluescreen freistellen (mit Spill-Unterdrückung).</summary>
    ChromaKey,

    /// <summary>Helles oder Dunkles freistellen (z. B. Titel auf Schwarz).</summary>
    LumaKey,

    Blur,
    Pixelate,
    Sharpen,
    BlackWhite,
    Sepia,
    FilmGrain,
    Stabilize,
}

/// <summary>
/// Ein Videoeffekt in der Effektkette eines Clips. Effekte wirken nacheinander (Knoten-Editor),
/// optional nur innerhalb einer Maske, die auch einem Objekt folgen kann (z. B. Gesicht verpixeln).
/// </summary>
public sealed class VideoEffect
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public EffectKind Kind { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Stärke 0 … 1 (bei Keys: Toleranz).</summary>
    public double Amount { get; set; } = 0.5;

    /// <summary>Keying: Farbe, die durchsichtig wird (#RRGGBB).</summary>
    public string KeyColor { get; set; } = GreenScreen;

    /// <summary>Keying: weiche Kante 0 … 1.</summary>
    public double Softness { get; set; } = 0.1;

    /// <summary>Keying: Spill-Unterdrückung 0 … 1 (grüner/blauer Farbsaum an Haaren und Kanten).</summary>
    public double Spill { get; set; } = 0.5;

    /// <summary>Luma-Key: Dunkles (statt Helles) freistellen.</summary>
    public bool KeyDark { get; set; } = true;

    /// <summary>Nur innerhalb dieser Maske wirken (bei Keys: Bereich, der überhaupt sichtbar bleibt – „Garbage Matte“).</summary>
    public ColorMask? Mask { get; set; }

    public const string GreenScreen = "#00B140";
    public const string BlueScreen = "#0047BB";

    public bool IsKey => Kind is EffectKind.ChromaKey or EffectKind.LumaKey;

    public string Name => DisplayName(Kind);

    public VideoEffect Clone()
    {
        var copy = (VideoEffect)MemberwiseClone();
        copy.Mask = Mask?.Clone();
        return copy;
    }

    public static IReadOnlyList<EffectKind> All { get; } = Enum.GetValues<EffectKind>();

    public static string DisplayName(EffectKind kind) => kind switch
    {
        EffectKind.ChromaKey => "Greenscreen (Chroma-Key)",
        EffectKind.LumaKey => "Luma-Key",
        EffectKind.Blur => "Weichzeichnen",
        EffectKind.Pixelate => "Verpixeln",
        EffectKind.Sharpen => "Schärfen",
        EffectKind.BlackWhite => "Schwarzweiß",
        EffectKind.Sepia => "Sepia",
        EffectKind.FilmGrain => "Filmkorn",
        EffectKind.Stabilize => "Verwacklung reduzieren",
        _ => kind.ToString(),
    };

    public static string ShortName(EffectKind kind) => kind switch
    {
        EffectKind.ChromaKey => "Key",
        EffectKind.LumaKey => "Luma",
        EffectKind.Blur => "Weich",
        EffectKind.Pixelate => "Pixel",
        EffectKind.Sharpen => "Schärfe",
        EffectKind.BlackWhite => "S/W",
        EffectKind.Sepia => "Sepia",
        EffectKind.FilmGrain => "Korn",
        EffectKind.Stabilize => "Ruhig",
        _ => kind.ToString(),
    };

    /// <summary>
    /// ffmpeg-Filter des Effekts für ein Bild der Größe <paramref name="width"/> × <paramref name="height"/>.
    /// Keys liefern ein Bild mit Alphakanal (yuva420p).
    /// </summary>
    public string Filter(int width, int height)
    {
        var a = Math.Clamp(Amount, 0, 1);
        switch (Kind)
        {
            case EffectKind.ChromaKey:
            {
                var color = "0x" + NormalizeColor(KeyColor)[1..];
                var key = $"format=yuva420p,chromakey=color={color}:similarity={N(0.02 + 0.5 * a)}:blend={N(Math.Clamp(Softness, 0, 1) * 0.3)}";
                if (Spill > 0.001)
                {
                    var type = IsBluish(KeyColor) ? "blue" : "green";
                    key += $",format=rgba,despill=type={type}:mix={N(Math.Clamp(Spill, 0, 1))}:expand={N(Math.Clamp(Spill, 0, 1) * 0.3)},format=yuva420p";
                }

                return key;
            }

            case EffectKind.LumaKey:
            {
                // Alpha aus der Helligkeit: Dunkles (bzw. Helles) wird durchsichtig, mit weicher Kante.
                var threshold = 255 * (0.02 + 0.6 * a);
                var soft = N(Math.Max(1, 255 * Math.Clamp(Softness, 0, 1) * 0.5));
                var alpha = KeyDark
                    ? $"255*clip((lum(X,Y)-{N(threshold)})/{soft},0,1)"
                    : $"255*clip(({N(255 - threshold)}-lum(X,Y))/{soft},0,1)";
                return $"format=yuva420p,geq=lum='lum(X,Y)':cb='cb(X,Y)':cr='cr(X,Y)':a='{alpha}'";
            }

            case EffectKind.Blur:
                return $"gblur=sigma={N(0.5 + 30 * a)}";
            case EffectKind.Pixelate:
            {
                var block = Math.Clamp((int)Math.Round(width * (0.006 + 0.05 * a)), 2, 1024);
                return $"pixelize=w={block}:h={block}";
            }

            case EffectKind.Sharpen:
                return $"unsharp=5:5:{N(0.3 + 2.2 * a)}:5:5:0";
            case EffectKind.BlackWhite:
                return $"hue=s={N(1 - a)}";
            case EffectKind.Sepia:
            {
                // Mischung aus unveränderten Farben und klassischer Sepia-Matrix.
                double M(double identity, double sepia) => (1 - a) * identity + a * sepia;
                return "colorchannelmixer=" + string.Join(':', new[]
                {
                    M(1, 0.393), M(0, 0.769), M(0, 0.189), 0,
                    M(0, 0.349), M(1, 0.686), M(0, 0.168), 0,
                    M(0, 0.272), M(0, 0.534), M(1, 0.131), 0,
                }.Select(v => N(v)));
            }

            case EffectKind.FilmGrain:
                return $"noise=alls={(int)Math.Round(4 + 36 * a)}:allf=t";
            case EffectKind.Stabilize:
            {
                var range = Math.Clamp((int)Math.Round(8 + 56 * a), 4, 64);
                return $"deshake=rx={range}:ry={range}:edge=mirror";
            }

            default:
                return "null";
        }
    }

    /// <summary>Kurzbeschreibung der Einstellungen (für den Knoten-Editor).</summary>
    public string Summary => Kind switch
    {
        EffectKind.ChromaKey => $"{(IsBluish(KeyColor) ? "Blau" : "Grün")} · Toleranz {Amount:P0} · Spill {Spill:P0}",
        EffectKind.LumaKey => $"{(KeyDark ? "Dunkles" : "Helles")} weg · {Amount:P0}",
        _ => $"Stärke {Amount:P0}",
    } + (Mask is null ? string.Empty : Mask.TrackX is { Keys.Count: > 0 } ? " · Maske (verfolgt)" : " · Maske");

    public static string NormalizeColor(string? color)
    {
        var text = (color ?? string.Empty).Trim().TrimStart('#');
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        return text.Length == 6 && int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)
            ? "#" + text.ToUpperInvariant()
            : GreenScreen;
    }

    public static bool IsBluish(string? color)
    {
        var hex = NormalizeColor(color);
        var r = int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var g = int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var b = int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return b > g && b > r;
    }

    private static string N(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
}
