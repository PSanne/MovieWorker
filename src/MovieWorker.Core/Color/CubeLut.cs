using System.Globalization;

namespace MovieWorker.Core.Color;

/// <summary>Eine 3D-LUT aus einer .cube-Datei (z. B. die offizielle Canon-LUT „Canon Log 3 → BT.709“).</summary>
public sealed class CubeLut
{
    private CubeLut(string title, int size, float[] data)
    {
        Title = title;
        Size = size;
        Data = data;
    }

    public string Title { get; }

    public int Size { get; }

    /// <summary>RGB-Werte, Rot läuft am schnellsten (wie in der Datei).</summary>
    public float[] Data { get; }

    public static CubeLut Parse(string text, string fallbackTitle = "LUT")
    {
        var title = fallbackTitle;
        var size = 0;
        var values = new List<float>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("TITLE", StringComparison.OrdinalIgnoreCase))
            {
                title = line[5..].Trim().Trim('"');
            }
            else if (line.StartsWith("LUT_3D_SIZE", StringComparison.OrdinalIgnoreCase))
            {
                size = int.Parse(line[11..].Trim(), CultureInfo.InvariantCulture);
            }
            else if (line.StartsWith("LUT_1D_SIZE", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("1D-LUTs werden nicht unterstützt – bitte eine 3D-LUT (.cube) verwenden.");
            }
            else if (char.IsDigit(line[0]) || line[0] is '-' or '.')
            {
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    for (var i = 0; i < 3; i++)
                    {
                        values.Add(float.Parse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture));
                    }
                }
            }
        }

        if (size < 2 || size > 256)
        {
            throw new InvalidDataException("Keine gültige 3D-LUT: LUT_3D_SIZE fehlt oder ist unplausibel.");
        }

        if (values.Count != size * size * size * 3)
        {
            throw new InvalidDataException($"Die LUT ist unvollständig ({values.Count / 3} statt {size * size * size} Einträge).");
        }

        return new CubeLut(title, size, [.. values]);
    }

    public static CubeLut Load(string path) => Parse(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));

    /// <summary>Wert an einer Farbe (trilinear interpoliert) – für Messinstrumente und Tests.</summary>
    public (float R, float G, float B) Apply(float r, float g, float b)
    {
        float Sample(int ri, int gi, int bi, int c) => Data[((bi * Size + gi) * Size + ri) * 3 + c];
        var n = Size - 1;
        var (fr, fg, fb) = (Math.Clamp(r, 0, 1) * n, Math.Clamp(g, 0, 1) * n, Math.Clamp(b, 0, 1) * n);
        int r0 = (int)fr, g0 = (int)fg, b0 = (int)fb;
        int r1 = Math.Min(n, r0 + 1), g1 = Math.Min(n, g0 + 1), b1 = Math.Min(n, b0 + 1);
        float dr = fr - r0, dg = fg - g0, db = fb - b0;
        var result = new float[3];
        for (var c = 0; c < 3; c++)
        {
            var c00 = Sample(r0, g0, b0, c) * (1 - dr) + Sample(r1, g0, b0, c) * dr;
            var c10 = Sample(r0, g1, b0, c) * (1 - dr) + Sample(r1, g1, b0, c) * dr;
            var c01 = Sample(r0, g0, b1, c) * (1 - dr) + Sample(r1, g0, b1, c) * dr;
            var c11 = Sample(r0, g1, b1, c) * (1 - dr) + Sample(r1, g1, b1, c) * dr;
            result[c] = (c00 * (1 - dg) + c10 * dg) * (1 - db) + (c01 * (1 - dg) + c11 * dg) * db;
        }

        return (result[0], result[1], result[2]);
    }
}

/// <summary>LUT-Bibliothek: alle .cube-Dateien eines Ordners.</summary>
public static class LutLibrary
{
    public static IReadOnlyList<string> Scan(string? folder) =>
        string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)
            ? []
            : Directory.EnumerateFiles(folder, "*.cube", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Sieht der Dateiname nach einer Canon-Log-3-LUT aus? (z. B. „CinemaGamut_CanonLog3-to-BT709_WideDR_33_FF_Ver.2.0.cube“).</summary>
    public static bool LooksLikeCanonLog3(string path)
    {
        var name = Path.GetFileName(path).Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        return name.Contains("log3", StringComparison.Ordinal) || name.Contains("clog3", StringComparison.Ordinal);
    }
}
