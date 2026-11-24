using System.Text.Json;
using System.Text.Json.Serialization;

namespace MovieWorker.Core.Settings;

/// <summary>Einstellungen, die MovieWorker zwischen zwei Starts behält.</summary>
public sealed class AppSettings
{
    public const int MaxRecentProjects = 10;

    /// <summary>Eigener Pfad zu ffmpeg.exe (leer = automatisch suchen).</summary>
    public string? FfmpegPath { get; set; }

    public List<string> RecentProjects { get; set; } = [];

    /// <summary>Zielordner für den Karten-Import (z. B. D:\Videos).</summary>
    public string? ImportFolder { get; set; }

    /// <summary>Beim Karten-Import Dateien auf die Festplatte kopieren (empfohlen) statt von der Karte zu schneiden.</summary>
    public bool ImportCopyFiles { get; set; } = true;

    /// <summary>Kopierte Dateien per SHA-256-Prüfsumme kontrollieren.</summary>
    public bool ImportVerifyChecksums { get; set; } = true;

    /// <summary>Beim Import automatisch Proxys für Aufnahmen erzeugen, die Windows nicht flüssig abspielt.</summary>
    public bool AutoCreateProxies { get; set; } = true;

    /// <summary>Vorschau mit Proxys statt Originalen.</summary>
    public bool UseProxies { get; set; } = true;

    /// <summary>Ordner für Proxys (leer = %LocalAppData%\MovieWorker\Proxys).</summary>
    public string? ProxyFolder { get; set; }

    public string EffectiveProxyFolder => string.IsNullOrWhiteSpace(ProxyFolder)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MovieWorker", "Proxys")
        : ProxyFolder;

    /// <summary>Ordner mit LUT-Dateien (.cube).</summary>
    public string? LutFolder { get; set; }

    /// <summary>Standard-LUT für Canon Log 3 (wird neuen Log-Clips zugewiesen).</summary>
    public string? DefaultCanonLog3Lut { get; set; }

    public string? LastExportFolder { get; set; }

    public string? LastExportPresetId { get; set; }

    /// <summary>Automatisch speichern alle x Minuten (0 = aus).</summary>
    public int AutosaveMinutes { get; set; } = 3;

    /// <summary>Hardware-Encoder (NVENC/Quick Sync) anbieten, wenn vorhanden.</summary>
    public bool UseHardwareEncoders { get; set; } = true;

    public void AddRecentProject(string path)
    {
        RecentProjects.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentProjects.Insert(0, path);
        if (RecentProjects.Count > MaxRecentProjects)
        {
            RecentProjects.RemoveRange(MaxRecentProjects, RecentProjects.Count - MaxRecentProjects);
        }
    }
}

/// <summary>Liest und schreibt <see cref="AppSettings"/> als JSON-Datei.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public SettingsStore(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    /// <summary>Standardort: %AppData%\MovieWorker\settings.json.</summary>
    public static SettingsStore ForCurrentUser() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MovieWorker", "settings.json"));

    public AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Beschädigte Einstellungen sollen den Start nie verhindern.
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, FilePath, overwrite: true);
    }
}
