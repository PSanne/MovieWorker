using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using MovieWorker.App.ViewModels;
using MovieWorker.Core.Export;
using MovieWorker.Core.Media;

namespace MovieWorker.App.Services;

/// <summary>Arbeitet Export-Aufträge nacheinander ab. Jeder Auftrag exportiert seine eigene Projektkopie.</summary>
public sealed class ExportQueueService(MediaToolsService tools)
{
    private bool _running;

    public ObservableCollection<ExportJobViewModel> Jobs { get; } = [];

    public event EventHandler<ExportJobViewModel>? JobFinished;

    public bool IsBusy => _running;

    public void Enqueue(ExportJobViewModel job)
    {
        Jobs.Add(job);
        _ = RunQueueAsync();
    }

    public void Cancel(ExportJobViewModel job)
    {
        if (job.State == ExportJobState.Waiting)
        {
            job.State = ExportJobState.Cancelled;
            job.Detail = "Abgebrochen.";
        }
        else if (job.State == ExportJobState.Running)
        {
            job.Cancellation.Cancel();
        }
    }

    public void ClearFinished()
    {
        foreach (var job in Jobs.Where(j => !j.CanCancel).ToList())
        {
            Jobs.Remove(job);
        }
    }

    /// <summary>Wartet, bis alle Aufträge erledigt sind (für den Selbsttest).</summary>
    public async Task WaitAllAsync()
    {
        while (_running || Jobs.Any(j => j.State == ExportJobState.Waiting))
        {
            await Task.Delay(200);
        }
    }

    private async Task RunQueueAsync()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        try
        {
            while (Jobs.FirstOrDefault(j => j.State == ExportJobState.Waiting) is { } job)
            {
                await RunJobAsync(job);
                JobFinished?.Invoke(this, job);
            }
        }
        finally
        {
            _running = false;
        }
    }

    private async Task RunJobAsync(ExportJobViewModel job)
    {
        job.State = ExportJobState.Running;
        job.Detail = "Wird vorbereitet …";
        var temp = Path.Combine(Path.GetDirectoryName(job.OutputPath)!, Path.GetFileNameWithoutExtension(job.OutputPath) + ".part" + job.Preset.Extension);
        try
        {
            var client = tools.Require();
            Directory.CreateDirectory(Path.GetDirectoryName(job.OutputPath)!);
            // Lautheit: erst die fertige Mischung messen, dann genau (linear) auf das Ziel bringen.
            Core.Audio.LoudnessMeasurement? loudness = null;
            if (job.Snapshot.Master.LoudnessTarget is not null && !job.Preset.IsStillImage)
            {
                job.Detail = "Lautheit wird gemessen …";
                var measure = ExportBuilder.BuildLoudnessMeasurement(job.Snapshot, job.RangeStart, job.RangeEnd, job.Preset.SurroundAudio);
                loudness = Core.Audio.LoudnessMeasurement.Parse(await client.RunForStderrAsync(measure.Arguments, job.Cancellation.Token));
                job.Measured = loudness;
            }

            var preset = job.Preset;
            var ffmpegJob = ExportBuilder.Build(job.Snapshot, preset, temp, job.RangeStart, job.RangeEnd, loudness);
            var eta = new EtaEstimator();
            job.Clock.Restart();
            var progress = new Progress<double>(p =>
            {
                job.Progress = p;
                job.Detail = $"{p:P0} · {EtaEstimator.Format(eta.Add(job.Clock.Elapsed.TotalSeconds, p))}";
            });
            try
            {
                await client.RunAsync(ffmpegJob.Arguments, ffmpegJob.DurationSeconds, progress, job.Cancellation.Token);
            }
            catch (FfmpegException) when (preset.UsesHardware && !job.Cancellation.IsCancellationRequested)
            {
                // Grafikkarte nicht nutzbar (Treiber, Auflösung, Format): einfach mit dem Prozessor noch einmal.
                job.UsedFallback = true;
                job.Detail = "Grafikkarte nicht nutzbar – Export mit dem Prozessor …";
                preset = preset.Software;
                ffmpegJob = ExportBuilder.Build(job.Snapshot, preset, temp, job.RangeStart, job.RangeEnd, loudness);
                eta = new EtaEstimator();
                job.Clock.Restart();
                await client.RunAsync(ffmpegJob.Arguments, ffmpegJob.DurationSeconds, progress, job.Cancellation.Token);
            }
            File.Move(temp, job.OutputPath, overwrite: true);
            job.Progress = 1;
            job.State = ExportJobState.Done;
            job.Detail = $"Fertig in {Core.Time.Timecode.FormatDuration(job.Clock.Elapsed)} · {MediaItemViewModel.FormatSize(new FileInfo(job.OutputPath).Length)}"
                + (loudness is null ? string.Empty : $" · Ton auf {job.Snapshot.Master.LoudnessTarget:0} LUFS gebracht (vorher {loudness.IntegratedLufs:0.0})")
                + (job.UsedFallback ? " · Grafikkarte nicht nutzbar, mit dem Prozessor exportiert" : preset.HardwareEncoder is { } hw ? $" · mit {hw.VendorName}" : string.Empty);
        }
        catch (OperationCanceledException)
        {
            job.State = ExportJobState.Cancelled;
            job.Detail = "Abgebrochen.";
            TryDelete(temp);
        }
        catch (Exception ex) when (ex is FfmpegException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            job.State = ExportJobState.Failed;
            job.Detail = ex.Message;
            TryDelete(temp);
        }
        finally
        {
            job.Clock.Stop();
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}
