using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MovieWorker.App.Services;
using MovieWorker.Core.Export;
using MovieWorker.Core.Time;
using MovieWorker.Core.Timeline;

namespace MovieWorker.App.ViewModels;

public sealed record ReframeChoice(ReframeMode Mode, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Dialog „Exportieren“: Vorlage, Ziel, Bereich und Prüfung vor dem Export.</summary>
public sealed partial class ExportDialogViewModel : ObservableObject
{
    private readonly MovieProject _project;
    private readonly IDialogService _dialogs;
    private readonly SettingsService _settings;
    private readonly string _projectName;

    public ExportDialogViewModel(MovieProject project, string projectName, IDialogService dialogs, SettingsService settings)
    {
        _project = project;
        _projectName = projectName;
        _dialogs = dialogs;
        _settings = settings;
        Folder = settings.Settings.LastExportFolder is { } f && Directory.Exists(f)
            ? f
            : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        HasRange = project.HasRange;
        UseRange = HasRange;
        _selectedPreset = ExportPreset.Find(settings.Settings.LastExportPresetId) ?? ExportPreset.Default;
        _selectedReframe = ReframeChoices.First(r => r.Mode == _selectedPreset.Reframe);
        FileName = ExportNaming.Suggest(projectName, _selectedPreset);
        Refresh();
    }

    public IReadOnlyList<ExportPreset> Presets => ExportPreset.All;

    public IReadOnlyList<ReframeChoice> ReframeChoices { get; } =
    [
        new(ReframeMode.BlurBackground, "Ganzes Bild, Rand unscharf gefüllt"),
        new(ReframeMode.Fill, "Bildfüllend zuschneiden (Mitte)"),
        new(ReframeMode.Fit, "Ganzes Bild, Rand schwarz"),
    ];

    public ObservableCollection<PreflightIssue> Issues { get; } = [];

    /// <summary>Ergebnis nach „Exportieren“: der fertige Auftrag.</summary>
    public ExportJobViewModel? Result { get; private set; }

    public event EventHandler<bool>? Finished;

    [ObservableProperty]
    private ExportPreset _selectedPreset;

    [ObservableProperty]
    private ReframeChoice _selectedReframe;

    [ObservableProperty]
    private string _fileName;

    [ObservableProperty]
    private string _folder;

    [ObservableProperty]
    private bool _useRange;

    public bool HasRange { get; }

    public bool CanReframe => SelectedPreset.HasFixedSize;

    public string RangeText => HasRange
        ? $"In/Out-Bereich ({Timecode.FromFrames(_project.MarkIn!.Value, _project.Settings.FrameRate)} – {Timecode.FromFrames(_project.MarkOut!.Value, _project.Settings.FrameRate)})"
        : "In/Out-Bereich (keine Marken gesetzt)";

    public string OutputText
    {
        get
        {
            var preset = EffectivePreset;
            var duration = TimeSpan.FromSeconds(_project.Settings.FrameRate.FramesToSeconds(
                UseRange && HasRange ? _project.MarkOut!.Value - _project.MarkIn!.Value : _project.DurationFrames));
            if (preset.AudioOnly)
            {
                return $"Nur Ton · Länge {Timecode.FormatDuration(duration)}";
            }

            var (w, h) = ExportBuilder.OutputSize(_project.Settings, preset);
            var rate = ExportBuilder.OutputFrameRate(_project.Settings.FrameRate, preset);
            return $"{w}×{h} · {rate} fps · Länge {Timecode.FormatDuration(duration)}";
        }
    }

    public bool HasErrors => ExportPreflight.HasErrors(Issues);

    public bool HasNoIssues => Issues.Count == 0;

    public string FullPath => Path.Combine(Folder, FileName);

    private ExportPreset EffectivePreset => SelectedPreset.HasFixedSize ? SelectedPreset.WithReframe(SelectedReframe.Mode) : SelectedPreset;

    partial void OnSelectedPresetChanged(ExportPreset value)
    {
        // Dateiendung und Namensvorschlag an die Vorlage anpassen.
        FileName = ExportNaming.Suggest(_projectName, value);
        SelectedReframe = ReframeChoices.First(r => r.Mode == value.Reframe);
        Refresh();
    }

    partial void OnSelectedReframeChanged(ReframeChoice value) => Refresh();

    partial void OnUseRangeChanged(bool value) => Refresh();

    [RelayCommand]
    private void BrowseFolder()
    {
        var folder = _dialogs.PickFolder("Zielordner für den Export", Folder);
        if (folder is not null)
        {
            Folder = folder;
        }
    }

    [RelayCommand]
    public void Export()
    {
        Refresh();
        if (HasErrors)
        {
            _dialogs.Error("Bitte zuerst die rot markierten Punkte beheben.");
            return;
        }

        var name = FileName.EndsWith(SelectedPreset.Extension, StringComparison.OrdinalIgnoreCase) ? FileName : FileName + SelectedPreset.Extension;
        var path = Path.Combine(Folder, name);
        if (File.Exists(path) && !_dialogs.Confirm($"„{name}“ gibt es schon. Überschreiben?"))
        {
            return;
        }

        _settings.Settings.LastExportFolder = Folder;
        _settings.Settings.LastExportPresetId = SelectedPreset.Id;
        _settings.Save();
        Result = new ExportJobViewModel(_project.Clone(), EffectivePreset, path,
            UseRange && HasRange ? _project.MarkIn : null, UseRange && HasRange ? _project.MarkOut : null);
        Finished?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel() => Finished?.Invoke(this, false);

    public void Refresh()
    {
        Issues.Clear();
        foreach (var issue in ExportPreflight.Check(_project, EffectivePreset,
                     UseRange && HasRange ? _project.MarkIn : null, UseRange && HasRange ? _project.MarkOut : null))
        {
            Issues.Add(issue);
        }

        OnPropertyChanged(nameof(OutputText));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasNoIssues));
        OnPropertyChanged(nameof(CanReframe));
        OnPropertyChanged(nameof(RangeText));
    }
}
