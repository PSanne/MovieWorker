using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using MovieWorker.Core.Export;
using MovieWorker.Core.Timeline;

namespace MovieWorker.App.ViewModels;

public enum ExportJobState
{
    Waiting,
    Running,
    Done,
    Failed,
    Cancelled,
}

/// <summary>Ein Auftrag in der Export-Warteschlange (mit eigener Kopie des Projekts zum Zeitpunkt des Auftrags).</summary>
public sealed partial class ExportJobViewModel : ObservableObject
{
    public ExportJobViewModel(MovieProject snapshot, ExportPreset preset, string outputPath, long? rangeStart, long? rangeEnd)
    {
        Snapshot = snapshot;
        Preset = preset;
        OutputPath = outputPath;
        RangeStart = rangeStart;
        RangeEnd = rangeEnd;
    }

    public MovieProject Snapshot { get; }

    public ExportPreset Preset { get; }

    public string OutputPath { get; }

    public long? RangeStart { get; }

    public long? RangeEnd { get; }

    public string FileName => Path.GetFileName(OutputPath);

    public string PresetName => Preset.Name;

    public Stopwatch Clock { get; } = new();

    public CancellationTokenSource Cancellation { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(IsRunning), nameof(IsFinished), nameof(CanCancel))]
    private ExportJobState _state;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _detail = "Wartet …";

    public bool IsRunning => State == ExportJobState.Running;

    public bool IsFinished => State is ExportJobState.Done;

    public bool CanCancel => State is ExportJobState.Waiting or ExportJobState.Running;

    public string StateText => State switch
    {
        ExportJobState.Waiting => "Wartet",
        ExportJobState.Running => "Läuft",
        ExportJobState.Done => "Fertig",
        ExportJobState.Failed => "Fehler",
        _ => "Abgebrochen",
    };
}
