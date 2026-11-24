using System.Collections.Concurrent;
using MovieWorker.Core.Media;
using MovieWorker.Core.Projects;

namespace MovieWorker.App.Services;

/// <summary>Liest Ton-Hüllkurven über ffmpeg und synchronisiert Kamera- mit externem Ton.</summary>
public sealed class AudioSyncService(MediaToolsService tools)
{
    private readonly ConcurrentDictionary<Guid, Task<float[]>> _envelopes = new();

    public async Task<SyncResult> SyncAsync(MediaItem video, MediaItem audio, CancellationToken token = default)
    {
        var camera = await EnvelopeAsync(video, token);
        var external = await EnvelopeAsync(audio, token);
        return await Task.Run(() => AudioSync.FindOffset(camera, external, maxOffsetSeconds: 600), token);
    }

    private Task<float[]> EnvelopeAsync(MediaItem media, CancellationToken token) =>
        _envelopes.GetOrAdd(media.Id, _ => LoadAsync(media, token));

    private async Task<float[]> LoadAsync(MediaItem media, CancellationToken token)
    {
        var client = tools.Require();
        var result = new List<float>();
        foreach (var part in media.Parts)
        {
            var pcm = await client.RunForBytesAsync(AudioSync.FfmpegArguments(part.Path), token).ConfigureAwait(false);
            result.AddRange(AudioSync.EnvelopeFromPcm16(pcm));
        }

        return [.. result];
    }
}
