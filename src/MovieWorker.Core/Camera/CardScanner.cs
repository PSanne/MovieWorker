namespace MovieWorker.Core.Camera;

/// <summary>Findet die Videodateien auf einer Speicherkarte bzw. in einem kopierten DCIM-Ordner.</summary>
public static class CardScanner
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mov", ".mp4", ".m4v", ".mxf", ".mts",
    };

    public static bool IsVideoFile(string path) => VideoExtensions.Contains(Path.GetExtension(path));

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
