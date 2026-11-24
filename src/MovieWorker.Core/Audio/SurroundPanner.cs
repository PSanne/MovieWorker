using System.Globalization;

namespace MovieWorker.Core.Audio;

/// <summary>
/// Räumlicher Ton: setzt eine (Stereo-)Spur in einen 5.1-Raum (links/rechts, vorne/hinten, Center, Subwoofer).
/// Für Stereo-Ausgaben wird dieselbe Position nach ITU-R BS.775 heruntergemischt – so klingt der Stereo-Export
/// wie die 5.1-Mischung, und ohne Raumeinstellungen bleibt alles wie beim einfachen Panorama.
/// </summary>
public static class SurroundPanner
{
    /// <summary>Kanäle in ffmpeg-Reihenfolge für 5.1.</summary>
    public static IReadOnlyList<string> Channels51 { get; } = ["FL", "FR", "FC", "LFE", "BL", "BR"];

    private static readonly double Minus3Db = Math.Sqrt(0.5);

    /// <summary>Verstärkungen 6 × 2: Ausgangskanal (FL, FR, FC, LFE, BL, BR) aus Eingang links/rechts.</summary>
    public static double[,] Gains51(TrackAudio audio)
    {
        var pan = Math.Clamp(audio.Pan, -1, 1);
        var left = Math.Min(1, 1 - pan);
        var right = Math.Min(1, 1 + pan);
        var depth = Math.Clamp(audio.Depth, 0, 1);
        var front = Math.Cos(depth * Math.PI / 2);
        var back = Math.Sin(depth * Math.PI / 2);
        var center = Math.Clamp(audio.Center, 0, 1);
        var lfe = Math.Clamp(audio.Lfe, 0, 1);
        var side = 1 - center;

        var g = new double[6, 2];
        g[0, 0] = side * front * left;
        g[1, 1] = side * front * right;
        g[2, 0] = g[2, 1] = center * 0.5;
        g[3, 0] = g[3, 1] = lfe * 0.5;
        g[4, 0] = side * back * left;
        g[5, 1] = side * back * right;
        return g;
    }

    /// <summary>Stereo-Downmix (ITU): Lo = L + 0,707·C + 0,707·Ls, Ro = R + 0,707·C + 0,707·Rs; der Subwoofer entfällt.</summary>
    public static double[,] GainsStereo(TrackAudio audio)
    {
        var g = Gains51(audio);
        var down = new double[,]
        {
            { 1, 0, Minus3Db, 0, Minus3Db, 0 },
            { 0, 1, Minus3Db, 0, 0, Minus3Db },
        };
        var result = new double[2, 2];
        for (var o = 0; o < 2; o++)
        {
            for (var i = 0; i < 2; i++)
            {
                for (var k = 0; k < 6; k++)
                {
                    result[o, i] += down[o, k] * g[k, i];
                }
            }
        }

        return result;
    }

    /// <summary>
    /// ffmpeg-<c>pan</c>-Filter für die Spur. Bei Stereo ohne Panorama und Raum: null (keine Änderung).
    /// Bei 5.1 immer ein Filter, damit alle Busse dasselbe Kanallayout haben.
    /// </summary>
    public static string? Filter(TrackAudio audio, bool surround)
    {
        if (!surround && !audio.IsSpatial && Math.Abs(audio.Pan) <= 0.001)
        {
            return null;
        }

        var gains = surround ? Gains51(audio) : GainsStereo(audio);
        var names = surround ? Channels51 : ["c0", "c1"];
        var parts = new List<string> { surround ? "pan=5.1" : "pan=stereo" };
        for (var o = 0; o < names.Count; o++)
        {
            var terms = new List<string>();
            for (var i = 0; i < 2; i++)
            {
                if (Math.Abs(gains[o, i]) > 1e-4)
                {
                    terms.Add($"{N(gains[o, i])}*c{i}");
                }
            }

            parts.Add($"{names[o]}={(terms.Count == 0 ? "0*c0" : string.Join('+', terms))}");
        }

        return string.Join('|', parts);
    }

    /// <summary>Kurzbeschreibung der Position („vorne links, Center 100 %“ …).</summary>
    public static string Describe(TrackAudio audio)
    {
        var horizontal = Math.Abs(audio.Pan) < 0.05 ? "Mitte" : audio.Pan < 0 ? "links" : "rechts";
        var depth = audio.Depth < 0.2 ? "vorne" : audio.Depth > 0.8 ? "hinten" : "seitlich";
        var text = $"{depth} {horizontal}";
        if (audio.Center > 0.01)
        {
            text += $", Center {audio.Center:P0}";
        }

        if (audio.Lfe > 0.01)
        {
            text += $", Subwoofer {audio.Lfe:P0}";
        }

        return text;
    }

    private static string N(double v) => Math.Round(v, 4).ToString("0.####", CultureInfo.InvariantCulture);
}
