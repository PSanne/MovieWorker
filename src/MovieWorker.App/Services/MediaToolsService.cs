using MovieWorker.Core.Export;
using MovieWorker.Core.Media;

namespace MovieWorker.App.Services;

/// <summary>Stellt den ffmpeg-Zugang bereit (oder meldet, dass er fehlt).</summary>
public sealed class MediaToolsService
{
    private readonly SettingsService _settings;

    public MediaToolsService(SettingsService settings)
    {
        _settings = settings;
        Reload();
    }

    public FfmpegClient? Client { get; private set; }

    public bool IsAvailable => Client is not null;

    public string StatusText => Client is { } c
        ? $"FFmpeg: {c.Paths.Ffmpeg}"
        : "FFmpeg nicht gefunden – bitte unter Extras → FFmpeg-Ordner festlegen.";

    public void Reload()
    {
        var paths = FfmpegLocator.LocateDefault(_settings.Settings.FfmpegPath);
        Client = paths is null ? null : new FfmpegClient(paths);
        _hardware = null;
    }

    private Task<IReadOnlyList<HardwareEncoder>>? _hardware;

    /// <summary>Grafikkarten-Encoder erkennen (einmal pro Programmstart, im Hintergrund).</summary>
    public Task<IReadOnlyList<HardwareEncoder>> DetectHardwareAsync() =>
        _hardware ??= Client is { } client
            ? Task.Run(() => MovieWorker.Core.Export.HardwareEncoders.DetectAsync(client))
            : Task.FromResult<IReadOnlyList<HardwareEncoder>>([]);

    /// <summary>Bereits erkannte Grafikkarten-Encoder (leer, solange die Erkennung läuft).</summary>
    public IReadOnlyList<HardwareEncoder> HardwareEncoders =>
        _hardware is { IsCompletedSuccessfully: true } task ? task.Result : [];

    public FfmpegClient Require() =>
        Client ?? throw new InvalidOperationException(
            "FFmpeg wurde nicht gefunden. Es liegt dem Download im Ordner „ffmpeg“ bei; alternativ unter Extras → FFmpeg-Ordner festlegen.");
}
