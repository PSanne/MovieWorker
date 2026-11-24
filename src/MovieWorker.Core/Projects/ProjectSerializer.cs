using System.Text.Json;
using System.Text.Json.Serialization;
using MovieWorker.Core.Timeline;

namespace MovieWorker.Core.Projects;

/// <summary>Speichert Projekte als lesbare JSON-Datei (<c>Name.mvproj</c>). Die Videos selbst bleiben, wo sie sind.</summary>
public static class ProjectSerializer
{
    public const string FileExtension = ".mvproj";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(MovieProject project) => JsonSerializer.Serialize(project, Options);

    public static MovieProject Deserialize(string json)
    {
        var project = JsonSerializer.Deserialize<MovieProject>(json, Options)
            ?? throw new InvalidDataException("Die Projektdatei ist leer.");
        if (project.FormatVersion > MovieProject.CurrentFormatVersion)
        {
            throw new InvalidDataException("Die Projektdatei stammt aus einer neueren MovieWorker-Version.");
        }

        return project;
    }

    public static async Task SaveAsync(MovieProject project, string path, CancellationToken token = default)
    {
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, Serialize(project), token).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }

    public static async Task<MovieProject> LoadAsync(string path, CancellationToken token = default) =>
        Deserialize(await File.ReadAllTextAsync(path, token).ConfigureAwait(false));

    /// <summary>Quelldateien, die nicht (mehr) vorhanden sind – z. B. weil die Karte nicht eingesteckt ist.</summary>
    public static IReadOnlyList<string> FindMissingMedia(MovieProject project) =>
        project.AllClips.SelectMany(c => c.SourcePaths).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(p => !File.Exists(p)).ToList();
}
