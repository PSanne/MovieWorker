using System.Collections.Concurrent;
using MovieWorker.Core.Media;

namespace MovieWorker.App.Services;

/// <summary>Lädt Wellenformen über ffmpeg im Hintergrund (eine nach der anderen) und hält sie im Speicher.</summary>
public sealed class WaveformService(MediaToolsService tools)
{
    private readonly ConcurrentDictionary<string, Task<WaveformData?>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1);

    /// <summary>Liefert die Wellenform, falls schon geladen; sonst null und <paramref name="onLoaded"/> wird später aufgerufen.</summary>
    public WaveformData? TryGet(IReadOnlyList<string> paths, Action onLoaded)
    {
        if (paths.Count == 0)
        {
            return null;
        }

        var key = string.Join('|', paths);
        var task = _cache.GetOrAdd(key, _ => LoadAsync(paths));
        if (task.IsCompletedSuccessfully)
        {
            return task.Result;
        }

        task.ContinueWith(_ => onLoaded(), TaskScheduler.FromCurrentSynchronizationContext());
        return null;
    }

    public Task<WaveformData?> GetAsync(IReadOnlyList<string> paths) =>
        _cache.GetOrAdd(string.Join('|', paths), _ => LoadAsync(paths));

    private async Task<WaveformData?> LoadAsync(IReadOnlyList<string> paths)
    {
        if (tools.Client is not { } client)
        {
            return null;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var peaks = new List<float>();
            double peaksPerSecond = 100;
            foreach (var path in paths)
            {
                var pcm = await client.RunForBytesAsync(WaveformData.FfmpegArguments(path)).ConfigureAwait(false);
                var part = WaveformData.FromPcm16(pcm);
                peaks.AddRange(part.Peaks);
                peaksPerSecond = part.PeaksPerSecond;
            }

            return new WaveformData([.. peaks], peaksPerSecond);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }
}
