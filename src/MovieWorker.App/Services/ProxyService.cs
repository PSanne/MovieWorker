using System.IO;
using MovieWorker.App.ViewModels;
using MovieWorker.Core.Preview;

namespace MovieWorker.App.Services;

/// <summary>Erstellt Proxys im Hintergrund – einen nach dem anderen, damit der Rechner bedienbar bleibt.</summary>
public sealed class ProxyService(MediaToolsService tools, SettingsService settings)
{
    private readonly SemaphoreSlim _gate = new(1);

    public event EventHandler? ProxyFinished;

    public bool NeedsProxy(MediaItemViewModel item) =>
        ProxyPlanner.Decide(item.Model) != ProxyKind.None && !item.Model.HasProxy;

    /// <summary>Erstellt (falls nötig) den Proxy. <paramref name="force"/> erstellt auch einen für direkt abspielbare Dateien.</summary>
    public async Task<bool> CreateAsync(MediaItemViewModel item, bool force = false, CancellationToken token = default)
    {
        var kind = ProxyPlanner.Decide(item.Model);
        if (kind == ProxyKind.None)
        {
            if (!force)
            {
                return false;
            }

            kind = ProxyKind.Transcode;
        }

        if (tools.Client is not { } client || item.IsProxyRunning)
        {
            return false;
        }

        var folder = settings.Settings.EffectiveProxyFolder;
        Directory.CreateDirectory(folder);
        var target = ProxyPlanner.ProxyPath(item.Model, folder);
        if (File.Exists(target) && new FileInfo(target).Length > 0 && !force)
        {
            item.Model.ProxyPath = target;
            item.RefreshAll();
            ProxyFinished?.Invoke(this, EventArgs.Empty);
            return true;
        }

        item.IsProxyRunning = true;
        item.ProxyProgress = 0;
        try
        {
            await _gate.WaitAsync(token);
            try
            {
                var temp = Path.Combine(folder, Path.GetFileNameWithoutExtension(target) + ".part.mp4");
                var progress = new Progress<double>(p => item.ProxyProgress = p);
                await client.RunAsync(ProxyPlanner.Arguments(item.Model, kind, temp, settings.Settings.DefaultCanonLog3Lut),
                    item.Model.Duration.TotalSeconds, progress, token);
                File.Move(temp, target, overwrite: true);
                item.Model.ProxyPath = target;
            }
            finally
            {
                _gate.Release();
            }

            item.RefreshAll();
            ProxyFinished?.Invoke(this, EventArgs.Empty);
            return true;
        }
        finally
        {
            item.IsProxyRunning = false;
        }
    }
}
