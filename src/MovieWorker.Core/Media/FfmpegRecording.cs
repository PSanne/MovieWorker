using System.Diagnostics;
using System.Text;
using MovieWorker.Core.Export;

namespace MovieWorker.Core.Media;

/// <summary>
/// Eine laufende ffmpeg-Aufnahme (Mikrofon → WAV). <see cref="Seconds"/> zeigt, wie viel schon aufgenommen ist –
/// damit lässt sich die Aufnahme genau an die Timeline anlegen (Startverzögerung des Geräts wird herausgerechnet).
/// </summary>
public sealed class FfmpegRecording : IAsyncDisposable
{
    private readonly Process _process;
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly StringBuilder _errors = new();
    private readonly Task _readers;
    private long _microseconds;
    private long _updatedAt = Stopwatch.GetTimestamp();

    internal FfmpegRecording(Process process, string outputPath)
    {
        _process = process;
        OutputPath = outputPath;
        var stdout = Task.Run(async () =>
        {
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false)) is not null)
            {
                if (FfmpegArgs.TryParseProgress(line, out var seconds))
                {
                    Interlocked.Exchange(ref _microseconds, (long)(seconds * 1_000_000));
                    Interlocked.Exchange(ref _updatedAt, Stopwatch.GetTimestamp());
                    if (seconds > 0)
                    {
                        _started.TrySetResult();
                    }
                }
            }
        });
        var stderr = Task.Run(async () =>
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync().ConfigureAwait(false)) is not null)
            {
                lock (_errors)
                {
                    _errors.AppendLine(line);
                }
            }
        });
        _readers = Task.WhenAll(stdout, stderr).ContinueWith(_ =>
            _started.TrySetException(new FfmpegException(ErrorText(), process.HasExited ? process.ExitCode : -1)), TaskScheduler.Default);
    }

    public string OutputPath { get; }

    /// <summary>Bisher aufgenommene Sekunden (zwischen zwei Fortschrittsmeldungen per Uhr fortgeschrieben).</summary>
    public double Seconds
    {
        get
        {
            var reported = Interlocked.Read(ref _microseconds) / 1_000_000.0;
            if (reported <= 0 || _process.HasExited)
            {
                return reported;
            }

            var since = Stopwatch.GetElapsedTime(Interlocked.Read(ref _updatedAt)).TotalSeconds;
            return reported + Math.Min(since, 0.25);
        }
    }

    public bool IsRunning => !_process.HasExited;

    /// <summary>Erfüllt, sobald das Gerät wirklich Ton liefert.</summary>
    public Task Started => _started.Task;

    /// <summary>Wartet, bis das Gerät Ton liefert (oder meldet einen Fehler, z. B. Mikrofon belegt).</summary>
    public async Task WaitUntilStartedAsync(TimeSpan timeout, CancellationToken token = default)
    {
        var finished = await Task.WhenAny(Started, Task.Delay(timeout, token)).ConfigureAwait(false);
        if (finished != Started)
        {
            await StopAsync().ConfigureAwait(false);
            throw new FfmpegException("Das Aufnahmegerät liefert keinen Ton. " + ErrorText(), -1);
        }

        await Started.ConfigureAwait(false);
    }

    /// <summary>Beendet die Aufnahme sauber (ffmpeg schließt die WAV-Datei ab).</summary>
    public async Task StopAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                await _process.StandardInput.WriteAsync('q').ConfigureAwait(false);
                await _process.StandardInput.FlushAsync().ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
            // Prozess hat die Eingabe schon geschlossen.
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        await _readers.ConfigureAwait(false);
        if (!File.Exists(OutputPath) || new FileInfo(OutputPath).Length < 100)
        {
            throw new FfmpegException("Die Aufnahme ist leer. " + ErrorText(), _process.ExitCode);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            try
            {
                await StopAsync().ConfigureAwait(false);
            }
            catch (FfmpegException)
            {
                // beim Aufräumen egal
            }
        }

        _process.Dispose();
    }

    private string ErrorText()
    {
        lock (_errors)
        {
            var lines = _errors.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return string.Join(' ', lines.TakeLast(3));
        }
    }
}
