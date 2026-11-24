using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MovieWorker.Core.Color;

namespace MovieWorker.App.Services;

public enum ScopeKind
{
    Waveform,
    Parade,
    Vectorscope,
    Histogram,
}

/// <summary>Zeichnet Messinstrumente als Bild (dunkler Hintergrund, Raster, Leuchtspur wie im Profi-Monitor).</summary>
public static class ScopeRenderer
{
    public static BitmapSource Render(ScopeData data, ScopeKind kind) => kind switch
    {
        ScopeKind.Parade => Parade(data),
        ScopeKind.Vectorscope => Vectorscope(data),
        ScopeKind.Histogram => Histogram(data),
        _ => Waveform(data),
    };

    private static BitmapSource Waveform(ScopeData data)
    {
        var width = data.Width;
        var pixels = new uint[width * 256];
        var max = Max(data.Waveform);
        for (var x = 0; x < width; x++)
        {
            for (var v = 0; v < 256; v++)
            {
                var intensity = Intensity(data.Waveform[x, v], max);
                pixels[(255 - v) * width + x] = Bgra(intensity * 0.6, intensity, intensity * 0.6);
            }
        }

        Graticule(pixels, width, 256);
        return Create(pixels, width, 256);
    }

    private static BitmapSource Parade(ScopeData data)
    {
        var columns = data.Width;
        var width = columns * 3;
        var pixels = new uint[width * 256];
        for (var c = 0; c < 3; c++)
        {
            var max = Max(data.Parade[c]);
            for (var x = 0; x < columns; x++)
            {
                for (var v = 0; v < 256; v++)
                {
                    var i = Intensity(data.Parade[c][x, v], max);
                    pixels[(255 - v) * width + c * columns + x] = Bgra(c == 0 ? i : i * 0.15, c == 1 ? i : i * 0.15, c == 2 ? i : i * 0.15);
                }
            }
        }

        Graticule(pixels, width, 256);
        return Create(pixels, width, 256);
    }

    private static BitmapSource Vectorscope(ScopeData data)
    {
        var pixels = new uint[256 * 256];
        var max = Max(data.Vectorscope);
        for (var cb = 0; cb < 256; cb++)
        {
            for (var cr = 0; cr < 256; cr++)
            {
                var i = Intensity(data.Vectorscope[cb, cr], max);
                // Farbe ungefähr wie der Farbort (Cr nach oben = rötlich, Cb nach rechts = bläulich)
                var r = 0.4 + (cr - 128) / 256.0;
                var b = 0.4 + (cb - 128) / 256.0;
                pixels[(255 - cr) * 256 + cb] = Bgra(i * Math.Clamp(r + 0.3, 0, 1), i * 0.8, i * Math.Clamp(b + 0.3, 0, 1));
            }
        }

        // Kreise und Hautton-Linie (≈ 123°)
        for (var a = 0; a < 720; a++)
        {
            var angle = a * Math.PI / 360;
            foreach (var radius in new[] { 64.0, 120.0 })
            {
                var x = (int)(128 + radius * Math.Cos(angle));
                var y = (int)(128 - radius * Math.Sin(angle));
                if (x is >= 0 and < 256 && y is >= 0 and < 256)
                {
                    pixels[y * 256 + x] = Bgra(0.35, 0.35, 0.35);
                }
            }
        }

        var skin = 123 * Math.PI / 180;
        for (var r = 0; r < 125; r++)
        {
            var x = (int)(128 + r * Math.Cos(skin));
            var y = (int)(128 - r * Math.Sin(skin));
            pixels[y * 256 + x] = Bgra(0.8, 0.6, 0.3);
        }

        return Create(pixels, 256, 256);
    }

    private static BitmapSource Histogram(ScopeData data)
    {
        const int height = 160;
        var pixels = new uint[256 * height];
        var max = data.Histogram.Take(3).SelectMany(h => h).DefaultIfEmpty(1).Max();
        for (var x = 0; x < 256; x++)
        {
            var heights = Enumerable.Range(0, 3).Select(c => (int)(Math.Sqrt(data.Histogram[c][x] / (double)Math.Max(1, max)) * (height - 1))).ToArray();
            for (var y = 0; y < height; y++)
            {
                var level = height - 1 - y;
                double r = heights[0] >= level ? 0.85 : 0.07, g = heights[1] >= level ? 0.85 : 0.07, b = heights[2] >= level ? 0.85 : 0.07;
                pixels[y * 256 + x] = Bgra(r, g, b);
            }
        }

        return Create(pixels, 256, height);
    }

    private static void Graticule(uint[] pixels, int width, int height)
    {
        foreach (var level in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
        {
            var y = Math.Clamp((int)Math.Round((1 - level) * (height - 1)), 0, height - 1);
            for (var x = 0; x < width; x += 2)
            {
                pixels[y * width + x] = Bgra(0.3, 0.3, 0.3);
            }
        }
    }

    private static int Max(int[,] values)
    {
        var max = 1;
        foreach (var v in values)
        {
            max = Math.Max(max, v);
        }

        return max;
    }

    private static double Intensity(int count, int max) => count == 0 ? 0.04 : Math.Clamp(0.25 + Math.Log(1 + count) / Math.Log(1 + max) * 0.75, 0, 1);

    private static uint Bgra(double r, double g, double b) =>
        0xFF000000u | ((uint)(Math.Clamp(r, 0, 1) * 255) << 16) | ((uint)(Math.Clamp(g, 0, 1) * 255) << 8) | (uint)(Math.Clamp(b, 0, 1) * 255);

    private static BitmapSource Create(uint[] pixels, int width, int height)
    {
        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }
}
