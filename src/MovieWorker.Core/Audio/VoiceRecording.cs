using System.Text.RegularExpressions;

namespace MovieWorker.Core.Audio;

/// <summary>Ein Aufnahmegerät (Mikrofon) für Sprachaufnahme und ADR – aufgenommen wird über ffmpeg.</summary>
public sealed record RecordingDevice(string Name, string Format, string Input)
{
    /// <summary>Testsignal statt Mikrofon (für Tests und zum Ausprobieren ohne Mikrofon).</summary>
    public static RecordingDevice TestTone { get; } = new("Testton (Sinus 330 Hz, ohne Mikrofon)", "lavfi", "sine=f=330:sample_rate=48000");

    /// <summary>Windows-Mikrofon über DirectShow.</summary>
    public static RecordingDevice FromDirectShow(string name) => new(name, "dshow", "audio=" + name);

    /// <summary>ffmpeg-Aufruf, der die DirectShow-Geräte auflistet (Ausgabe auf stderr, Rückgabewert ≠ 0 ist normal).</summary>
    public static IReadOnlyList<string> ListArguments { get; } = ["-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy"];

    /// <summary>ffmpeg-Aufruf für die Aufnahme: WAV 48 kHz 24 Bit, Fortschritt auf stdout, Ende mit „q“ auf stdin.</summary>
    public IReadOnlyList<string> Arguments(string outputPath)
    {
        var args = new List<string> { "-y", "-hide_banner", "-v", "error", "-f", Format };
        if (Format == "dshow")
        {
            args.AddRange(["-audio_buffer_size", "50"]);
        }
        else
        {
            args.Add("-re");   // Testsignal in Echtzeit erzeugen, wie ein Mikrofon
        }

        args.AddRange(["-i", Input, "-ar", "48000", "-c:a", "pcm_s24le", "-progress", "pipe:1", "-stats_period", "0.1", "-nostats", outputPath]);
        return args;
    }

    public override string ToString() => Name;

    /// <summary>Liest die Mikrofone aus der Geräteliste von ffmpeg (alte und neue Ausgabeform).</summary>
    public static IReadOnlyList<RecordingDevice> ParseDirectShowList(string stderr)
    {
        var devices = new List<RecordingDevice>();
        var inAudioSection = false;
        foreach (var raw in stderr.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Contains("DirectShow audio devices", StringComparison.OrdinalIgnoreCase))
            {
                inAudioSection = true;
                continue;
            }

            if (line.Contains("DirectShow video devices", StringComparison.OrdinalIgnoreCase))
            {
                inAudioSection = false;
                continue;
            }

            if (line.Contains("Alternative name", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var match = Regex.Match(line, "\"(?<name>[^\"]+)\"\\s*(?<kind>\\((audio|video|none)\\))?\\s*$");
            if (!match.Success)
            {
                continue;
            }

            var kind = match.Groups["kind"].Value;
            var isAudio = kind.Length > 0 ? kind == "(audio)" : inAudioSection;
            var name = match.Groups["name"].Value;
            if (isAudio && devices.All(d => d.Name != name))
            {
                devices.Add(FromDirectShow(name));
            }
        }

        return devices;
    }
}

/// <summary>Ein Take einer ADR- oder Sprachaufnahme.</summary>
public sealed class AdrTake
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int Number { get; set; }

    public string Path { get; set; } = string.Empty;

    /// <summary>Stelle in der Aufnahme (Sekunden), die dem Anfang des Bereichs in der Timeline entspricht.</summary>
    public double OffsetSeconds { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public Guid? MediaId { get; set; }

    public AdrTake Clone() => (AdrTake)MemberwiseClone();

    public override string ToString() => $"Take {Number}";
}

/// <summary>
/// ADR-Szene (Nachsynchronisation): ein Timeline-Bereich, dessen Dialog neu gesprochen wird –
/// in mehreren Takes, von denen einer in die Timeline kommt.
/// </summary>
public sealed class AdrCue
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>Der zu sprechende Text (als Hilfe beim Aufnehmen).</summary>
    public string Dialog { get; set; } = string.Empty;

    public long StartFrame { get; set; }

    public long EndFrame { get; set; }

    public long LengthFrames => EndFrame - StartFrame;

    public List<AdrTake> Takes { get; set; } = [];

    /// <summary>Der Take, der in der Timeline liegt.</summary>
    public Guid? UsedTakeId { get; set; }

    public int NextTakeNumber => Takes.Select(t => t.Number).DefaultIfEmpty(0).Max() + 1;

    public AdrCue Clone()
    {
        var copy = (AdrCue)MemberwiseClone();
        copy.Takes = Takes.Select(t => t.Clone()).ToList();
        return copy;
    }

    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? "ADR" : Name;
}
