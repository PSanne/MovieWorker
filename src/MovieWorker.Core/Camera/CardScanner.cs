namespace MovieWorker.Core.Camera;

/// <summary>Findet die Videodateien auf einer Speicherkarte bzw. in einem kopierten DCIM-Ordner.</summary>
public static class CardScanner
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mov", ".mp4", ".m4v", ".mxf", ".mts",
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".mp3", ".m4a", ".aac", ".flac", ".aif", ".aiff", ".ogg", ".wma",
    };

    public static bool IsVideoFile(string path) => VideoExtensions.Contains(Path.GetExtension(path));

    public static bool IsAudioFile(string path) => AudioExtensions.Contains(Path.GetExtension(path));

    /// <summary>Video- oder Audiodatei (für den Import einzelner Dateien, z. B. Musik).</summary>
    public static bool IsMediaFile(string path) => IsVideoFile(path) || IsAudioFile(path);

    /// <summary>Dateifilter für Öffnen-Dialoge.</summary>
    public static string DialogFilter =>
        "Videos und Audio|" + string.Join(';', VideoExtensions.Concat(AudioExtensions).Select(e => "*" + e))
        + "|Videos|" + string.Join(';', VideoExtensions.Select(e => "*" + e))
        + "|Audio|" + string.Join(';', AudioExtensions.Select(e => "*" + e))
        + "|Alle Dateien|*.*";

    /// <summary>Ist dieser Ordner eine Kamerakarte (enthält einen DCIM-Ordner)?</summary>
    public static bool LooksLikeCameraCard(string root) => Directory.Exists(Path.Combine(root, "DCIM"));

    /// <summary>
    /// Filtert eine Dateiliste: nur Videos, ohne versteckte Dateien (z. B. macOS „._MVI_0001.MOV“),
    /// ohne Vorschaubilder (.THM) und Kamera-Verwaltungsdateien (.CTG).
    /// </summary>
    public static IReadOnlyList<string> FilterVideos(IEnumerable<string> paths) =>
        paths.Where(IsVideoFile)
            .Where(p => !Path.GetFileName(p).StartsWith("._", StringComparison.Ordinal))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IReadOnlyList<string> Scan(string folder) =>
        FilterVideos(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories));
}
