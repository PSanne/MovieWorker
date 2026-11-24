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
    }

    public FfmpegClient Require() =>
        Client ?? throw new InvalidOperationException(
            "FFmpeg wurde nicht gefunden. Es liegt dem Download im Ordner „ffmpeg“ bei; alternativ unter Extras → FFmpeg-Ordner festlegen.");
}
