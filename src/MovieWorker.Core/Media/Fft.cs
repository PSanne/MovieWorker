using System.Numerics;

namespace MovieWorker.Core.Media;

/// <summary>Schnelle Fourier-Transformation (Radix-2, in place) – Grundlage für Ton-Synchronisation und Analysen.</summary>
public static class Fft
{
    public static int NextPowerOfTwo(int n)
    {
        var p = 1;
        while (p < n)
        {
            p <<= 1;
        }

        return p;
    }

    public static void Transform(Complex[] data, bool inverse)
    {
        var n = data.Length;
        if ((n & (n - 1)) != 0)
        {
            throw new ArgumentException("Länge muss eine Zweierpotenz sein.", nameof(data));
        }

        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;
            if (i < j)
            {
                (data[i], data[j]) = (data[j], data[i]);
            }
        }

        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = 2 * Math.PI / len * (inverse ? 1 : -1);
            var wLen = new Complex(Math.Cos(angle), Math.Sin(angle));
            for (var i = 0; i < n; i += len)
            {
                var w = Complex.One;
                for (var k = 0; k < len / 2; k++)
                {
                    var u = data[i + k];
                    var v = data[i + k + len / 2] * w;
                    data[i + k] = u + v;
                    data[i + k + len / 2] = u - v;
                    w *= wLen;
                }
            }
        }

        if (inverse)
        {
            for (var i = 0; i < n; i++)
            {
                data[i] /= n;
            }
        }
    }
}
