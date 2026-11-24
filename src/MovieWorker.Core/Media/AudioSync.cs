using System.Numerics;

namespace MovieWorker.Core.Media;

/// <summary>Ergebnis der Ton-Synchronisation.</summary>
/// <param name="OffsetSeconds">Ein Ereignis zur Zeit t im Kameraton liegt im externen Ton bei t + Offset.</param>
/// <param name="Confidence">0…1 – wie eindeutig die Übereinstimmung ist (ab etwa 0,3 verlässlich).</param>
public sealed record SyncResult(double OffsetSeconds, double Confidence)
{
    public bool IsReliable => Confidence >= 0.3;
}

/// <summary>
/// Legt externen Ton (z. B. vom Rekorder) an den Kameraton an – wie „Synchronisieren per Wellenform“
/// in Profi-Programmen. Verglichen werden die Lautstärke-Verläufe (Hüllkurven) beider Aufnahmen per
/// Kreuzkorrelation (über FFT, daher auch bei langen Aufnahmen schnell). Genauigkeit: 5 ms.
/// </summary>
public static class AudioSync
{
    public const int SampleRate = 8000;
    public const int WindowSamples = 40; // 5 ms
    public const double EnvelopeRate = (double)SampleRate / WindowSamples;

    /// <summary>ffmpeg-Argumente: Ton als 16-Bit-mono-PCM mit 8 kHz.</summary>
    public static IReadOnlyList<string> FfmpegArguments(string path, double maxSeconds = 1800) =>
        WaveformData.FfmpegArguments(path, SampleRate).Prepend(maxSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)).Prepend("-t").ToList();

    /// <summary>Hüllkurve: Lautstärke (RMS) je 5 ms aus 16-Bit-PCM mit 8 kHz.</summary>
    public static float[] EnvelopeFromPcm16(ReadOnlySpan<byte> pcm)
    {
        var samples = pcm.Length / 2;
        var result = new float[samples / WindowSamples];
        for (var w = 0; w < result.Length; w++)
        {
            double sum = 0;
            for (var i = 0; i < WindowSamples; i++)
            {
                var index = (w * WindowSamples + i) * 2;
                var s = (short)(pcm[index] | (pcm[index + 1] << 8)) / 32768.0;
                sum += s * s;
            }

            result[w] = (float)Math.Sqrt(sum / WindowSamples);
        }

        return result;
    }

    /// <summary>Findet den Versatz zwischen Kameraton und externem Ton.</summary>
    public static SyncResult FindOffset(float[] cameraEnvelope, float[] externalEnvelope, double maxOffsetSeconds = 120)
    {
        if (cameraEnvelope.Length < 20 || externalEnvelope.Length < 20)
        {
            return new SyncResult(0, 0);
        }

        // Auf Einsätze (Anstiege der Lautstärke) achten – das macht die Spitze schärfer als die reine Hüllkurve.
        var a = Prepare(cameraEnvelope);
        var b = Prepare(externalEnvelope);
        var n = Fft.NextPowerOfTwo(a.Length + b.Length);
        var fa = new Complex[n];
        var fb = new Complex[n];
        for (var i = 0; i < a.Length; i++)
        {
            fa[i] = a[i];
        }

        for (var i = 0; i < b.Length; i++)
        {
            fb[i] = b[i];
        }

        Fft.Transform(fa, inverse: false);
        Fft.Transform(fb, inverse: false);
        for (var i = 0; i < n; i++)
        {
            fa[i] = Complex.Conjugate(fa[i]) * fb[i];
        }

        Fft.Transform(fa, inverse: true);

        // corr[lag] = Σ a[t]·b[t+lag]; negative Versätze liegen am Ende des Puffers.
        var maxLag = (int)Math.Min(maxOffsetSeconds * EnvelopeRate, n / 2 - 1);
        double best = double.MinValue, second = double.MinValue;
        var bestLag = 0;
        for (var lag = -maxLag; lag <= maxLag; lag++)
        {
            var value = fa[(lag + n) % n].Real;
            if (value > best)
            {
                best = value;
                bestLag = lag;
            }
        }

        // Zweitbeste Spitze außerhalb von ±50 ms um die beste – für die Eindeutigkeit.
        for (var lag = -maxLag; lag <= maxLag; lag++)
        {
            if (Math.Abs(lag - bestLag) > 10)
            {
                second = Math.Max(second, fa[(lag + n) % n].Real);
            }
        }

        var energy = Math.Sqrt(a.Sum(x => (double)x * x) * b.Sum(x => (double)x * x));
        var normalized = energy > 0 ? best / energy : 0;
        var distinct = best > 0 ? 1 - Math.Max(0, second) / best : 0;
        var confidence = Math.Clamp(Math.Sqrt(Math.Max(0, normalized)) * 0.5 + distinct * 0.5, 0, 1);
        return new SyncResult(bestLag / EnvelopeRate, confidence);
    }

    private static float[] Prepare(float[] envelope)
    {
        var onset = new float[envelope.Length];
        for (var i = 1; i < envelope.Length; i++)
        {
            onset[i] = Math.Max(0, envelope[i] - envelope[i - 1]) + envelope[i] * 0.1f;
        }

        var mean = onset.Average();
        double variance = 0;
        foreach (var v in onset)
        {
            variance += (v - mean) * (v - mean);
        }

        var std = (float)Math.Sqrt(variance / onset.Length);
        if (std <= 1e-9f)
        {
            return new float[onset.Length];
        }

        for (var i = 0; i < onset.Length; i++)
        {
            onset[i] = (onset[i] - mean) / std;
        }

        return onset;
    }
}
