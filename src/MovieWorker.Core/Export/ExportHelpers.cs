using System.Globalization;

namespace MovieWorker.Core.Export;

/// <summary>Schätzt die Restzeit aus dem bisherigen Fortschritt (geglättet über die letzten Sekunden).</summary>
public sealed class EtaEstimator
{
    private readonly Queue<(double Seconds, double Progress)> _samples = new();

    public void Reset() => _samples.Clear();

    /// <summary>Fügt einen Messpunkt hinzu und liefert die geschätzte Restzeit (null, solange zu wenig Daten).</summary>
    public TimeSpan? Add(double elapsedSeconds, double progress)
    {
        _samples.Enqueue((elapsedSeconds, progress));
        while (_samples.Count > 2 && elapsedSeconds - _samples.Peek().Seconds > 20)
        {
            _samples.Dequeue();
        }

        var first = _samples.Peek();
        var dt = elapsedSeconds - first.Seconds;
        var dp = progress - first.Progress;
        if (dt < 1 || dp <= 0)
        {
            return null;
        }

        return TimeSpan.FromSeconds(Math.Max(0, (1 - progress) * dt / dp));
    }

    public static string Format(TimeSpan? remaining) => remaining switch
    {
        null => "Restzeit wird berechnet …",
        { TotalHours: >= 1 } r => $"noch {(int)r.TotalHours} h {r.Minutes} min",
        { TotalMinutes: >= 1 } r => $"noch {(int)r.TotalMinutes} min {r.Seconds} s",
        { } r => $"noch {Math.Max(1, (int)Math.Ceiling(r.TotalSeconds))} s",
    };
}

/// <summary>Vorschläge für Dateinamen beim Export.</summary>
public static class ExportNaming
{
    public static string Suggest(string projectName, ExportPreset preset)
    {
        var label = preset.Id switch
        {
            "web-h264" => "YouTube",
            "web-h264-1080" => "Full HD",
            "web-h265-4k" => "YouTube H265",
            "social-vertical" => "Hochformat",
            "social-square" => "Quadrat",
            "social-portrait" => "4x5",
            "prores-hq" => "ProRes HQ",
            "prores-lt" => "ProRes LT",
            "dnxhr-hqx" => "DNxHR",
            "hevc-10bit" => "Archiv",
            _ => "Ton",
        };
        var name = string.IsNullOrWhiteSpace(projectName) ? "Film" : projectName.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        name = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return $"{name} - {label}{preset.Extension}";
    }

    /// <summary>Hängt „(2)“, „(3)“ … an, falls die Datei schon existiert.</summary>
    public static string MakeUnique(string path, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        if (!exists(path))
        {
            return path;
        }

        var folder = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{name} ({i}){extension}"));
            if (!exists(candidate))
            {
                return candidate;
            }
        }
    }
}
