using System.Globalization;
using System.Text.Json;

namespace MovieWorker.Core.Audio;

/// <summary>Kanalzug einer Spur im Mischpult: Panorama, Solo und Effekte.</summary>
public sealed class TrackAudio
{
    /// <summary>Panorama −1 (links) … +1 (rechts).</summary>
    public double Pan { get; set; }

    public bool Solo { get; set; }

    /// <summary>Hochpass in Hz (0 = aus) – gegen Wind und Rumpeln.</summary>
    public double HighPassHz { get; set; }

    /// <summary>Rauschminderung 0 (aus) … 1 (stark).</summary>
    public double NoiseReduction { get; set; }

    /// <summary>3-Band-Equalizer in dB (Tiefen 120 Hz, Mitten 1 kHz, Höhen 8 kHz).</summary>
    public double EqLow { get; set; }

    public double EqMid { get; set; }

    public double EqHigh { get; set; }

    public bool Compressor { get; set; }

    public double CompressorThresholdDb { get; set; } = -18;

    public double CompressorRatio { get; set; } = 3;

    /// <summary>Hall 0 (aus) … 1 (viel).</summary>
    public double Reverb { get; set; }

    /// <summary>Auto-Ducking: diese Spur wird leiser, sobald auf der angegebenen Spur Ton ist (z. B. Musik bei Sprache).</summary>
    public Guid? DuckBy { get; set; }

    /// <summary>Wie stark abgesenkt wird (dB).</summary>
    public double DuckAmountDb { get; set; } = 12;

    /// <summary>Raumklang: Tiefe 0 (vorne) … 1 (hinten).</summary>
    public double Depth { get; set; }

    /// <summary>Raumklang: Anteil auf dem Center-Lautsprecher 0 … 1 (Dialog meist 1).</summary>
    public double Center { get; set; }

    /// <summary>Raumklang: Anteil auf dem Subwoofer (LFE) 0 … 1.</summary>
    public double Lfe { get; set; }

    /// <summary>Hat die Spur Raumklang-Einstellungen (über links/rechts hinaus)?</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsSpatial => Depth > 0.001 || Center > 0.001 || Lfe > 0.001;

    public TrackAudio Clone() => (TrackAudio)MemberwiseClone();

    /// <summary>Effektkette des Kanalzugs (ohne Lautstärke) als ffmpeg-Filter, zuletzt die Position (Stereo oder 5.1).</summary>
    public IReadOnlyList<string> Filters(bool surround = false)
    {
        string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        var filters = new List<string>();
        if (HighPassHz > 1)
        {
            filters.Add($"highpass=f={N(HighPassHz)}");
        }

        if (NoiseReduction > 0.001)
        {
            filters.Add($"afftdn=nr={N(6 + 24 * Math.Clamp(NoiseReduction, 0, 1))}:nf=-40");
        }

        if (Math.Abs(EqLow) > 0.01)
        {
            filters.Add($"bass=g={N(EqLow)}:f=120");
        }

        if (Math.Abs(EqMid) > 0.01)
        {
            filters.Add($"equalizer=f=1000:t=q:w=1:g={N(EqMid)}");
        }

        if (Math.Abs(EqHigh) > 0.01)
        {
            filters.Add($"treble=g={N(EqHigh)}:f=8000");
        }

        if (Compressor)
        {
            filters.Add($"acompressor=threshold={N(CompressorThresholdDb)}dB:ratio={N(Math.Clamp(CompressorRatio, 1, 20))}:attack=10:release=200:makeup={N(Math.Clamp(-CompressorThresholdDb / 6, 1, 4))}");
        }

        if (Reverb > 0.001)
        {
            var a = Math.Clamp(Reverb, 0, 1);
            filters.Add($"aecho=0.9:0.9:43|71|113|163:{N(0.35 * a)}|{N(0.27 * a)}|{N(0.2 * a)}|{N(0.12 * a)}");
        }

        if (SurroundPanner.Filter(this, surround) is { } pan)
        {
            filters.Add(pan);
        }

        return filters;
    }
}

/// <summary>Gesamtsumme: Lautstärke, Limiter und Lautheits-Ziel.</summary>
public sealed class MasterAudio
{
    public double VolumeDb { get; set; }

    /// <summary>Verhindert Übersteuerung (−0,5 dBFS).</summary>
    public bool Limiter { get; set; } = true;

    /// <summary>Lautheit in LUFS (z. B. −14 für YouTube) – null = nicht anpassen.</summary>
    public double? LoudnessTarget { get; set; }

    public MasterAudio Clone() => (MasterAudio)MemberwiseClone();
}

/// <summary>Messwerte der Lautheit (EBU R128) aus ffmpeg <c>loudnorm</c>.</summary>
public sealed record LoudnessMeasurement(double IntegratedLufs, double TruePeakDb, double Range, double Threshold, double Offset)
{
    public static IReadOnlyList<(double? Target, string Label)> Targets { get; } =
    [
        (null, "Nicht anpassen"),
        (-14, "−14 LUFS (YouTube, Spotify)"),
        (-16, "−16 LUFS (Podcast, Apple)"),
        (-23, "−23 LUFS (Fernsehen, EBU R128)"),
    ];

    /// <summary>Liest den JSON-Block, den <c>loudnorm=print_format=json</c> auf stderr ausgibt.</summary>
    public static LoudnessMeasurement? Parse(string stderr)
    {
        var start = stderr.LastIndexOf('{');
        var end = stderr.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(stderr[start..(end + 1)]);
            double Read(string name) =>
                double.TryParse(doc.RootElement.GetProperty(name).GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                    ? (double.IsInfinity(v) ? -99 : v)
                    : -99;
            return new LoudnessMeasurement(Read("input_i"), Read("input_tp"), Read("input_lra"), Read("input_thresh"), Read("target_offset"));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    public override string ToString() =>
        string.Create(CultureInfo.CurrentCulture, $"{IntegratedLufs:0.0} LUFS · Spitze {TruePeakDb:0.0} dBTP · Umfang {Range:0.0} LU");
}
