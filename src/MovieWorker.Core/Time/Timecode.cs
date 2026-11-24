using System.Globalization;

namespace MovieWorker.Core.Time;

/// <summary>
/// Umrechnung zwischen Bildnummern und SMPTE-Timecode (HH:MM:SS:FF).
/// Für 29,97 und 59,94 fps wird auf Wunsch Drop-Frame verwendet (Trennzeichen „;“),
/// damit der Timecode zur tatsächlich vergangenen Zeit passt.
/// </summary>
public static class Timecode
{
    public static string FromFrames(long frames, FrameRate rate, bool dropFrame = false)
    {
        var negative = frames < 0;
        frames = Math.Abs(frames);
        var fps = rate.NominalFps;
        var useDrop = dropFrame && rate.SupportsDropFrame;

        if (useDrop)
        {
            frames = AddDroppedFrameNumbers(frames, fps);
        }

        var ff = frames % fps;
        var totalSeconds = frames / fps;
        var ss = totalSeconds % 60;
        var mm = totalSeconds / 60 % 60;
        var hh = totalSeconds / 3600;
        var separator = useDrop ? ';' : ':';
        var sign = negative ? "-" : string.Empty;
        return string.Create(CultureInfo.InvariantCulture, $"{sign}{hh:00}:{mm:00}:{ss:00}{separator}{ff:00}");
    }

    /// <summary>Liest „HH:MM:SS:FF“ oder „HH:MM:SS;FF“ (Drop-Frame) in eine Bildnummer.</summary>
    public static long ToFrames(string timecode, FrameRate rate)
    {
        ArgumentNullException.ThrowIfNull(timecode);
        var text = timecode.Trim();
        var dropFrame = text.Contains(';');
        var parts = text.Split(':', ';');
        if (parts.Length != 4)
        {
            throw new FormatException($"Ungültiger Timecode: „{timecode}“");
        }

        var values = parts.Select(p => long.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        long hh = values[0], mm = values[1], ss = values[2], ff = values[3];
        var fps = rate.NominalFps;
        if (ff >= fps || ss >= 60 || mm >= 60)
        {
            throw new FormatException($"Ungültiger Timecode: „{timecode}“");
        }

        var frames = ((hh * 60 + mm) * 60 + ss) * fps + ff;
        if (dropFrame && rate.SupportsDropFrame)
        {
            var drop = fps / 15; // 2 bei 29,97 – 4 bei 59,94
            var totalMinutes = hh * 60 + mm;
            frames -= drop * (totalMinutes - totalMinutes / 10);
        }

        return frames;
    }

    /// <summary>Kurze Zeitangabe für die Oberfläche, z. B. „1:05:03“ oder „0:42“.</summary>
    public static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : duration.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    private static long AddDroppedFrameNumbers(long frames, int fps)
    {
        var drop = fps / 15;
        var framesPerMinute = fps * 60 - drop;
        var framesPer10Minutes = fps * 600 - drop * 9;
        var tens = frames / framesPer10Minutes;
        var rest = frames % framesPer10Minutes;
        frames += drop * 9 * tens;
        if (rest > drop)
        {
            frames += drop * ((rest - drop) / framesPerMinute);
        }

        return frames;
    }
}
