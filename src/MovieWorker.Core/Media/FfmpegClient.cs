using System.Diagnostics;
using System.Text;
using MovieWorker.Core.Export;

namespace MovieWorker.Core.Media;

/// <summary>Fehler beim Aufruf von ffmpeg/ffprobe – mit der letzten Fehlermeldung des Programms.</summary>
public sealed class FfmpegException(string message, int exitCode) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
}

/// <summary>
/// Startet ffmpeg und ffprobe als eigene Prozesse. Argumente werden einzeln übergeben (ArgumentList),
/// dadurch sind Leerzeichen und Sonderzeichen in Dateinamen kein Problem.
/// </summary>
public sealed class FfmpegClient(FfmpegPaths paths)
{
    public FfmpegPaths Paths { get; } = paths;

    public async Task<MediaInfo> ProbeAsync(string path, CancellationToken token = default)
    {
        var json = await RunForTextAsync(Paths.Ffprobe, FfprobeParser.BuildArguments(path), token).ConfigureAwait(false);
        var size = File.Exists(path) ? new FileInfo(path).Length : 0;
        return FfprobeParser.Parse(path, json, size);
    }

    /// <summary>Führt ffmpeg aus und meldet den Fortschritt (0…1) anhand von <c>-progress pipe:1</c>.</summary>
    public async Task RunAsync(IReadOnlyList<string> arguments, double totalSeconds, IProgress<double>? progress = null,
        CancellationToken token = default)
    {
        using var process = Start(Paths.Ffmpeg, arguments, redirectInput: true);
        var errors = new StringBuilder();
        var stderrTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync().ConfigureAwait(false)) is not null)
            {
                lock (errors)
                {
                    errors.AppendLine(line);
                    if (errors.Length > 20_000)
                    {
                        errors.Remove(0, errors.Length - 10_000);
                    }
                }
            }
        }, CancellationToken.None);

        await using (token.Register(() => Cancel(process)))
        {
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false)) is not null)
            {
                if (progress is not null && totalSeconds > 0 && FfmpegArgs.TryParseProgress(line, out var seconds))
                {
                    progress.Report(Math.Clamp(seconds / totalSeconds, 0, 1));
                }
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await stderrTask.ConfigureAwait(false);
        }

        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
        {
            throw new FfmpegException(LastLines(errors.ToString()), process.ExitCode);
        }

        progress?.Report(1);
    }

    /// <summary>Führt ffmpeg aus und liefert die Standardausgabe als Bytes (z. B. ein PNG oder Rohdaten).</summary>
    public async Task<byte[]> RunForBytesAsync(IReadOnlyList<string> arguments, CancellationToken token = default)
    {
        using var process = Start(Paths.Ffmpeg, arguments, redirectInput: false);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var memory = new MemoryStream();
        await using (token.Register(() => Cancel(process)))
        {
            await process.StandardOutput.BaseStream.CopyToAsync(memory, CancellationToken.None).ConfigureAwait(false);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        var stderr = await stderrTask.ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
        {
            throw new FfmpegException(LastLines(stderr), process.ExitCode);
        }

        return memory.ToArray();
    }

    /// <summary>Führt ffmpeg aus und liefert die Fehlerausgabe (dort landen Messwerte wie loudnorm/ebur128).</summary>
    public async Task<string> RunForStderrAsync(IReadOnlyList<string> arguments, CancellationToken token = default)
    {
        using var process = Start(Paths.Ffmpeg, arguments, redirectInput: false);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        string stderr;
        await using (token.Register(() => Cancel(process)))
        {
            stderr = await process.StandardError.ReadToEndAsync(CancellationToken.None).ConfigureAwait(false);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await stdoutTask.ConfigureAwait(false);
        }

        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
        {
            throw new FfmpegException(LastLines(stderr), process.ExitCode);
        }

        return stderr;
    }

    /// <summary>Liest die Ausgabe von <c>ffmpeg -hide_banner -encoders</c> (für Hardware-Encoder-Erkennung).</summary>
    public Task<string> ListEncodersAsync(CancellationToken token = default) =>
        RunForTextAsync(Paths.Ffmpeg, ["-hide_banner", "-encoders"], token);

    private static async Task<string> RunForTextAsync(string exe, IReadOnlyList<string> arguments, CancellationToken token)
    {
        using var process = Start(exe, arguments, redirectInput: false);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        string output;
        await using (token.Register(() => Cancel(process)))
        {
            output = await process.StandardOutput.ReadToEndAsync(CancellationToken.None).ConfigureAwait(false);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        var stderr = await stderrTask.ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
        {
            throw new FfmpegException(LastLines(stderr), process.ExitCode);
        }

        return output;
    }

    private static Process Start(string exe, IReadOnlyList<string> arguments, bool redirectInput)
    {
        var info = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectInput,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return Process.Start(info) ?? throw new FfmpegException($"{Path.GetFileName(exe)} konnte nicht gestartet werden.", -1);
    }

    private static void Cancel(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                // Erst höflich „q“ senden (ffmpeg schließt die Datei sauber ab), sonst beenden.
                if (process.StartInfo.RedirectStandardInput)
                {
                    process.StandardInput.Write('q');
                    process.StandardInput.Flush();
                    if (process.WaitForExit(1500))
                    {
                        return;
                    }
                }

                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Prozess ist bereits beendet.
        }
        catch (IOException)
        {
            process.Kill(entireProcessTree: true);
        }
    }

    private static string LastLines(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? "ffmpeg meldet einen unbekannten Fehler." : string.Join(Environment.NewLine, lines.TakeLast(6));
    }
}
