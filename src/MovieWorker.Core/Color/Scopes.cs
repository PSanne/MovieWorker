namespace MovieWorker.Core.Color;

/// <summary>Messinstrumente aus einem RGB24-Bild: Waveform, RGB-Parade, Vektorskop, Histogramm.</summary>
public sealed class ScopeData
{
    public required int Width { get; init; }

    /// <summary>Waveform: [Spalte, Helligkeit 0…255] = Anzahl Pixel.</summary>
    public required int[,] Waveform { get; init; }

    /// <summary>RGB-Parade: je Kanal [Spalte, Wert].</summary>
    public required int[][,] Parade { get; init; }

    /// <summary>Vektorskop: [Cb 0…255, Cr 0…255] = Anzahl Pixel.</summary>
    public required int[,] Vectorscope { get; init; }

    /// <summary>Histogramm: R, G, B, Luma je 256 Werte.</summary>
    public required int[][] Histogram { get; init; }

    /// <summary>Mittelwerte und Streuung je Kanal (0…1) – Grundlage für den Farbabgleich.</summary>
    public required (double Mean, double StdDev)[] ChannelStats { get; init; }

    public static ScopeData FromRgb24(ReadOnlySpan<byte> rgb, int width, int height, int columns = 256)
    {
        columns = Math.Min(columns, width);
        var waveform = new int[columns, 256];
        var parade = new[] { new int[columns, 256], new int[columns, 256], new int[columns, 256] };
        var vector = new int[256, 256];
        var histogram = new[] { new int[256], new int[256], new int[256], new int[256] };
        var sum = new double[3];
        var sumSq = new double[3];
        var count = width * height;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 3;
                int r = rgb[i], g = rgb[i + 1], b = rgb[i + 2];
                var luma = (int)Math.Round(0.2126 * r + 0.7152 * g + 0.0722 * b);
                var cb = (int)Math.Clamp(Math.Round(128 - 0.1146 * r - 0.3854 * g + 0.5 * b), 0, 255);
                var cr = (int)Math.Clamp(Math.Round(128 + 0.5 * r - 0.4542 * g - 0.0458 * b), 0, 255);
                var column = x * columns / width;
                waveform[column, luma]++;
                parade[0][column, r]++;
                parade[1][column, g]++;
                parade[2][column, b]++;
                vector[cb, cr]++;
                histogram[0][r]++;
                histogram[1][g]++;
                histogram[2][b]++;
                histogram[3][luma]++;
                sum[0] += r;
                sum[1] += g;
                sum[2] += b;
                sumSq[0] += r * r;
                sumSq[1] += g * g;
                sumSq[2] += b * b;
            }
        }

        var stats = new (double, double)[3];
        for (var c = 0; c < 3; c++)
        {
            var mean = sum[c] / Math.Max(1, count);
            var variance = Math.Max(0, sumSq[c] / Math.Max(1, count) - mean * mean);
            stats[c] = (mean / 255, Math.Sqrt(variance) / 255);
        }

        return new ScopeData
        {
            Width = columns, Waveform = waveform, Parade = parade, Vectorscope = vector, Histogram = histogram, ChannelStats = stats,
        };
    }
}

/// <summary>
/// Farbabgleich: bringt ein Bild (z. B. EOS 70D) farblich an ein Referenzbild (z. B. R6 Mark II) heran,
/// indem Mittelwert und Kontrast jedes Farbkanals angeglichen werden.
/// </summary>
public static class ColorMatch
{
    public static void Apply(ScopeData reference, ScopeData target, ColorGrade grade)
    {
        double[] gains = new double[3], offsets = new double[3];
        for (var c = 0; c < 3; c++)
        {
            var (refMean, refStd) = reference.ChannelStats[c];
            var (tgtMean, tgtStd) = target.ChannelStats[c];
            var gain = tgtStd > 0.01 ? Math.Clamp(refStd / tgtStd, 0.5, 2) : 1;
            gains[c] = gain;
            offsets[c] = Math.Clamp(refMean - tgtMean * gain, -1, 1);
        }

        grade.MatchGainR = gains[0];
        grade.MatchGainG = gains[1];
        grade.MatchGainB = gains[2];
        grade.MatchOffsetR = offsets[0];
        grade.MatchOffsetG = offsets[1];
        grade.MatchOffsetB = offsets[2];
    }
}
