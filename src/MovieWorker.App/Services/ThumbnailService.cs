using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media.Imaging;
using MovieWorker.Core.Export;

namespace MovieWorker.App.Services;

/// <summary>Erzeugt Vorschaubilder über ffmpeg und hält sie im Speicher (höchstens zwei gleichzeitig).</summary>
public sealed class ThumbnailService(MediaToolsService tools)
{
    private readonly ConcurrentDictionary<string, Task<BitmapSource?>> _cache = new();
    private readonly SemaphoreSlim _gate = new(2);

    public Task<BitmapSource?> GetAsync(string path, double seconds, int height, string? colorFilter = null)
    {
        var key = $"{path}|{seconds:0.###}|{height}|{colorFilter}";
        return _cache.GetOrAdd(key, _ => LoadAsync(path, seconds, height, colorFilter));
    }

    /// <summary>Ein Bild ohne Zwischenspeicher (z. B. für die Programmvorschau an der Abspielposition).</summary>
    public async Task<BitmapSource?> RenderAsync(string path, double seconds, int height, string? colorFilter, CancellationToken token)
    {
        if (tools.Client is not { } client)
        {
            return null;
        }

        var png = await client.RunForBytesAsync(FfmpegArgs.Thumbnail(path, seconds, height, colorFilter), token).ConfigureAwait(false);
        return Decode(png);
    }

    private async Task<BitmapSource?> LoadAsync(string path, double seconds, int height, string? colorFilter)
    {
        if (tools.Client is not { } client)
        {
            return null;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var png = await client.RunForBytesAsync(FfmpegArgs.Thumbnail(path, seconds, height, colorFilter)).ConfigureAwait(false);
            return Decode(png);
        }
        catch (Exception)
        {
            // Kein Vorschaubild ist kein Fehler – die Liste zeigt dann einen Platzhalter.
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public static BitmapSource? Decode(byte[] png)
    {
        if (png.Length == 0)
        {
            return null;
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(png);
        image.EndInit();
        image.Freeze();
        return image;
    }
}
