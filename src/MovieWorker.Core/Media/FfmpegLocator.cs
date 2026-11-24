namespace MovieWorker.Core.Media;

/// <summary>Pfade zu ffmpeg und ffprobe.</summary>
public sealed record FfmpegPaths(string Ffmpeg, string Ffprobe);

/// <summary>
/// Sucht ffmpeg in dieser Reihenfolge: eingestellter Pfad → Unterordner „ffmpeg“ neben MovieWorker.exe
/// (so wird es im Download mitgeliefert) → Programmordner → PATH.
/// </summary>
public static class FfmpegLocator
{
    public static string ExecutableName(string tool) => OperatingSystem.IsWindows() ? tool + ".exe" : tool;

    public static IEnumerable<string> CandidateFolders(string? configuredPath, string appDirectory, string? pathVariable)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            yield return File.Exists(configuredPath) ? Path.GetDirectoryName(Path.GetFullPath(configuredPath))! : configuredPath;
        }

        yield return Path.Combine(appDirectory, "ffmpeg");
        yield return Path.Combine(appDirectory, "ffmpeg", "bin");
        yield return appDirectory;

        if (!string.IsNullOrEmpty(pathVariable))
        {
            foreach (var folder in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                yield return folder.Trim().Trim('"');
            }
        }
    }

    public static FfmpegPaths? Locate(string? configuredPath, string appDirectory, string? pathVariable, Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        foreach (var folder in CandidateFolders(configuredPath, appDirectory, pathVariable))
        {
            var ffmpeg = Path.Combine(folder, ExecutableName("ffmpeg"));
            var ffprobe = Path.Combine(folder, ExecutableName("ffprobe"));
            if (fileExists(ffmpeg) && fileExists(ffprobe))
            {
                return new FfmpegPaths(ffmpeg, ffprobe);
            }
        }

        return null;
    }

    public static FfmpegPaths? LocateDefault(string? configuredPath = null) =>
        Locate(configuredPath, AppContext.BaseDirectory, Environment.GetEnvironmentVariable("PATH"));
}
