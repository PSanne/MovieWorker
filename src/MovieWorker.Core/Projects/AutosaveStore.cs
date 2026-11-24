using System.Diagnostics;
using System.Text.Json;
using MovieWorker.Core.Timeline;

namespace MovieWorker.Core.Projects;

/// <summary>Eine automatische Sicherung, die nach einem Absturz wiederhergestellt werden kann.</summary>
public sealed record AutosaveEntry(string SessionId, string ProjectFile, string? OriginalPath, string Title, DateTime SavedAt, int ProcessId);

/// <summary>
/// Automatisches Speichern: Jede Programmsitzung sichert ungespeicherte Änderungen regelmäßig in einen eigenen
/// Ordner. Wird MovieWorker normal beendet, verschwindet die Sicherung wieder; nach einem Absturz bietet der
/// nächste Start die Wiederherstellung an.
/// </summary>
public sealed class AutosaveStore(string folder)
{
    private static readonly JsonSerializerOptions MetaOptions = new() { WriteIndented = true };

    public string Folder { get; } = folder;

    /// <summary>Kennung dieser Programmsitzung.</summary>
    public string SessionId { get; } = Guid.NewGuid().ToString("N");

    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MovieWorker", "Autosave");

    private string ProjectFile(string session) => Path.Combine(Folder, session + ProjectSerializer.FileExtension);

    private string MetaFile(string session) => Path.Combine(Folder, session + ".json");

    /// <summary>Sichert das Projekt (überschreibt die letzte Sicherung dieser Sitzung).</summary>
    public async Task SaveAsync(MovieProject project, string? originalPath, string title, CancellationToken token = default)
    {
        Directory.CreateDirectory(Folder);
        await ProjectSerializer.SaveAsync(project, ProjectFile(SessionId), token).ConfigureAwait(false);
        var meta = new AutosaveEntry(SessionId, ProjectFile(SessionId), originalPath, title, DateTime.Now, Environment.ProcessId);
        var temp = MetaFile(SessionId) + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(meta, MetaOptions), token).ConfigureAwait(false);
        File.Move(temp, MetaFile(SessionId), overwrite: true);
    }

    /// <summary>Sicherungen anderer, nicht mehr laufender Sitzungen (neueste zuerst).</summary>
    public IReadOnlyList<AutosaveEntry> FindRecoverable(Func<int, bool>? isRunning = null)
    {
        isRunning ??= IsMovieWorkerRunning;
        if (!Directory.Exists(Folder))
        {
            return [];
        }

        var entries = new List<AutosaveEntry>();
        foreach (var file in Directory.EnumerateFiles(Folder, "*.json"))
        {
            try
            {
                var entry = JsonSerializer.Deserialize<AutosaveEntry>(File.ReadAllText(file));
                if (entry is null || entry.SessionId == SessionId || !File.Exists(ProjectFile(entry.SessionId)))
                {
                    continue;
                }

                if (entry.ProcessId != Environment.ProcessId && isRunning(entry.ProcessId))
                {
                    continue;   // gehört zu einem anderen, noch geöffneten MovieWorker
                }

                entries.Add(entry with { ProjectFile = ProjectFile(entry.SessionId) });
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // Beschädigte Sicherung überspringen.
            }
        }

        return entries.OrderByDescending(e => e.SavedAt).ToList();
    }

    public Task<MovieProject> LoadAsync(AutosaveEntry entry, CancellationToken token = default) =>
        ProjectSerializer.LoadAsync(entry.ProjectFile, token);

    /// <summary>Sicherung einer (fremden) Sitzung löschen.</summary>
    public void Discard(AutosaveEntry entry) => Delete(entry.SessionId);

    /// <summary>Sicherung dieser Sitzung löschen (nach dem Speichern oder beim normalen Beenden).</summary>
    public void ClearOwn() => Delete(SessionId);

    public bool HasOwn => File.Exists(MetaFile(SessionId));

    private void Delete(string session)
    {
        foreach (var file in new[] { ProjectFile(session), MetaFile(session) })
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // nicht schlimm
            }
        }
    }

    private static bool IsMovieWorkerRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName.Contains("MovieWorker", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }
}
