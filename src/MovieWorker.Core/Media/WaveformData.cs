namespace MovieWorker.Core.Media;

/// <summary>
/// Hüllkurve einer Tonspur für die Anzeige: pro Zeitabschnitt der höchste Ausschlag (0…1).
/// Wird aus 16-Bit-PCM (mono) berechnet, das ffmpeg liefert.
/// </summary>
public sealed class WaveformData
{
    public WaveformData(float[] peaks, double peaksPerSecond)
    {
        Peaks = peaks;
        PeaksPerSecond = peaksPerSecond;
    }

    public float[] Peaks { get; }

    public double PeaksPerSecond { get; }

    public double DurationSeconds => Peaks.Length / PeaksPerSecond;

    /// <summary>ffmpeg-Argumente: Ton als 16-Bit-mono-PCM mit <paramref name="sampleRate"/> Hz auf die Standardausgabe.</summary>
    public static IReadOnlyList<string> FfmpegArguments(string path, int sampleRate = 4000) =>
        ["-v", "error", "-i", path, "-vn", "-ac", "1", "-ar", sampleRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
         "-f", "s16le", "-acodec", "pcm_s16le", "-"];

    public static WaveformData FromPcm16(ReadOnlySpan<byte> pcm, int sampleRate = 4000, int peaksPerSecond = 100)
    {
        var samplesPerPeak = Math.Max(1, sampleRate / peaksPerSecond);
        var sampleCount = pcm.Length / 2;
        var peaks = new float[(sampleCount + samplesPerPeak - 1) / samplesPerPeak];
        for (var i = 0; i < sampleCount; i++)
        {
            var sample = Math.Abs((short)(pcm[2 * i] | (pcm[2 * i + 1] << 8)) / 32768f);
            ref var peak = ref peaks[i / samplesPerPeak];
            if (sample > peak)
            {
                peak = sample;
            }
        }

        return new WaveformData(peaks, (double)sampleRate / samplesPerPeak);
    }

    /// <summary>Höchster Ausschlag im Zeitraum [<paramref name="from"/>, <paramref name="to"/>) in Sekunden.</summary>
    public float PeakBetween(double from, double to)
    {
        var start = Math.Max(0, (int)Math.Floor(from * PeaksPerSecond));
        var end = Math.Min(Peaks.Length, Math.Max(start + 1, (int)Math.Ceiling(to * PeaksPerSecond)));
        var max = 0f;
        for (var i = start; i < end; i++)
        {
            max = Math.Max(max, Peaks[i]);
        }

        return max;
    }
}
