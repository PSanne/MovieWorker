using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MovieWorker.App.Services;
using MovieWorker.Core.Camera;
using MovieWorker.Core.Media;
using MovieWorker.Core.Projects;
using MovieWorker.Core.Time;
using MovieWorker.Core.Timeline;

namespace MovieWorker.App.ViewModels;

/// <summary>Zentrales ViewModel: Projekt, Medienpool, Timeline und alle Menübefehle.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private const string ProjectFilter = "MovieWorker-Projekt (*.mvproj)|*.mvproj";

    private readonly IDialogService _dialogs;
    private readonly SettingsService _settings;
    private readonly MediaToolsService _tools;
    private readonly ThumbnailService _thumbnails;
    private bool _suppressDirty;

    public MainViewModel(IDialogService dialogs, SettingsService settings, MediaToolsService tools, ThumbnailService thumbnails)
    {
        _dialogs = dialogs;
        _settings = settings;
        _tools = tools;
        _thumbnails = thumbnails;
        Editor = new TimelineEditor();
        Editor.Changed += OnEditorChanged;
        RefreshRecentProjects();
        StatusText = tools.IsAvailable ? "Bereit." : tools.StatusText;
    }

    public TimelineEditor Editor { get; }

    public IDialogService Dialogs => _dialogs;

    public SettingsService Settings => _settings;

    public MediaToolsService Tools => _tools;

    public ThumbnailService Thumbnails => _thumbnails;

    public ObservableCollection<MediaItemViewModel> MediaItems { get; } = [];

    public ObservableCollection<string> RecentProjects { get; } = [];

    public MovieProject Project => Editor.Project;

    public string ProjectSettingsText => Project.Settings.Label;

    public string FfmpegStatus => _tools.StatusText;

    public string WindowTitle =>
        $"{(ProjectPath is null ? "Neues Projekt" : Path.GetFileNameWithoutExtension(ProjectPath))}{(IsDirty ? " *" : string.Empty)} – MovieWorker";

    public string DurationText => Timecode.FromFrames(Editor.Duration, Project.Settings.FrameRate, Project.Settings.DropFrameTimecode);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string? _projectPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private bool _isDirty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedMedia))]
    [NotifyCanExecuteChangedFor(nameof(AddSelectedToTimelineCommand), nameof(RemoveSelectedMediaCommand))]
    private MediaItemViewModel? _selectedMedia;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyText = string.Empty;

    [ObservableProperty]
    private double _busyProgress;

    public bool HasSelectedMedia => SelectedMedia is not null;

    /// <summary>Wird nach jeder Änderung an Timeline oder Medienpool ausgelöst (für Timeline-Ansicht und Monitore).</summary>
    public event EventHandler? ProjectChanged;

    // ---------------------------------------------------------------- Projekt

    [RelayCommand]
    public async Task NewProjectAsync()
    {
        if (!await ConfirmDiscardAsync())
        {
            return;
        }

        LoadProject(MovieProject.CreateDefault(), null);
        StatusText = "Neues Projekt.";
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        if (!await ConfirmDiscardAsync())
        {
            return;
        }

        var path = _dialogs.OpenFile("Projekt öffnen", ProjectFilter);
        if (path is not null)
        {
            await OpenProjectFileAsync(path);
        }
    }

    [RelayCommand]
    private async Task OpenRecentAsync(string? path)
    {
        if (path is null || !await ConfirmDiscardAsync())
        {
            return;
        }

        if (!File.Exists(path))
        {
            _dialogs.Error($"Die Projektdatei wurde nicht gefunden:\n{path}");
            _settings.Settings.RecentProjects.Remove(path);
            _settings.Save();
            RefreshRecentProjects();
            return;
        }

        await OpenProjectFileAsync(path);
    }

    public async Task OpenProjectFileAsync(string path)
    {
        try
        {
            var project = await ProjectSerializer.LoadAsync(path);
            LoadProject(project, path);
            RememberProject(path);
            var missing = ProjectSerializer.FindMissingMedia(project);
            StatusText = $"Projekt geöffnet: {Path.GetFileName(path)}";
            if (missing.Count > 0)
            {
                _dialogs.Error(
                    $"{missing.Count} Mediendatei(en) wurden nicht gefunden (z. B. Karte nicht eingesteckt oder Ordner verschoben):\n\n"
                    + string.Join("\n", missing.Take(8)) + (missing.Count > 8 ? "\n…" : string.Empty),
                    "Fehlende Medien");
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            _dialogs.Error($"Das Projekt konnte nicht geöffnet werden:\n{ex.Message}");
        }
    }

    [RelayCommand]
    public async Task<bool> SaveProjectAsync()
    {
        if (ProjectPath is null)
        {
            return await SaveProjectAsAsync();
        }

        return await SaveToAsync(ProjectPath);
    }

    [RelayCommand]
    public async Task<bool> SaveProjectAsAsync()
    {
        var path = _dialogs.SaveFile("Projekt speichern", ProjectFilter, "Mein Film.mvproj", _settings.Settings.ImportFolder);
        return path is not null && await SaveToAsync(path);
    }

    public async Task<bool> SaveToAsync(string path)
    {
        try
        {
            await ProjectSerializer.SaveAsync(Project, path);
            ProjectPath = path;
            IsDirty = false;
            RememberProject(path);
            StatusText = $"Gespeichert: {Path.GetFileName(path)}";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Error($"Das Projekt konnte nicht gespeichert werden:\n{ex.Message}");
            return false;
        }
    }

    /// <summary>Fragt bei ungespeicherten Änderungen nach. Liefert false, wenn der Benutzer abbricht.</summary>
    public async Task<bool> ConfirmDiscardAsync()
    {
        if (!IsDirty)
        {
            return true;
        }

        switch (_dialogs.AskYesNoCancel("Das Projekt wurde geändert. Änderungen speichern?"))
        {
            case DialogAnswer.Yes:
                return await SaveProjectAsync();
            case DialogAnswer.No:
                return true;
            default:
                return false;
        }
    }

    [RelayCommand]
    private void EditProjectSettings()
    {
        var vm = new ProjectSettingsViewModel(Project.Settings);
        if (_dialogs.ShowDialog(vm))
        {
            Editor.ChangeSettings(vm.ToSettings(Project.Settings));
            StatusText = $"Projekteinstellungen: {Project.Settings.Label}";
        }
    }

    // ---------------------------------------------------------------- Import

    [RelayCommand]
    private async Task ImportFilesAsync()
    {
        var files = _dialogs.OpenFiles("Videos und Audio importieren", CardScanner.DialogFilter);
        if (files.Count > 0)
        {
            await ImportPathsAsync(files);
        }
    }

    [RelayCommand]
    private async Task ImportCardAsync()
    {
        var vm = new ImportCardViewModel(_dialogs, _settings);
        if (_dialogs.ShowDialog(vm) && vm.ResultFiles.Count > 0)
        {
            await ImportPathsAsync(vm.ResultFiles);
        }
    }

    /// <summary>Liest Dateien ein und nimmt sie in den Medienpool auf (auch per Drag &amp; Drop).</summary>
    public async Task<IReadOnlyList<MediaItemViewModel>> ImportPathsAsync(IEnumerable<string> paths)
    {
        if (!_tools.IsAvailable)
        {
            _dialogs.Error(_tools.StatusText);
            return [];
        }

        var expanded = ExpandFolders(paths).ToList();
        IsBusy = true;
        BusyText = "Dateien werden gelesen …";
        try
        {
            var client = _tools.Require();
            var progress = new Progress<(int Done, int Total)>(p =>
            {
                BusyProgress = p.Total == 0 ? 1 : (double)p.Done / p.Total;
                BusyText = $"Dateien werden gelesen … {p.Done}/{p.Total}";
            });
            var result = await MediaImport.ProbeAsync(expanded, client.ProbeAsync, progress);
            var added = Editor.AddMedia(result.Items);
            var viewModels = added.Select(AddMediaViewModel).ToList();
            SelectedMedia = viewModels.FirstOrDefault() ?? SelectedMedia;

            var skipped = result.Items.Count - added.Count;
            StatusText = $"{added.Count} Aufnahme(n) importiert" + (skipped > 0 ? $", {skipped} waren schon im Projekt" : string.Empty) + ".";
            if (result.Failed.Count > 0)
            {
                _dialogs.Error("Diese Dateien konnten nicht gelesen werden:\n\n"
                    + string.Join("\n", result.Failed.Take(10).Select(f => $"{Path.GetFileName(f.Path)}: {f.Error}")));
            }

            return viewModels;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedMedia))]
    private void RemoveSelectedMedia()
    {
        if (SelectedMedia is not { } item)
        {
            return;
        }

        if (!Editor.RemoveMedia(item.Model.Id))
        {
            _dialogs.Error("Diese Aufnahme wird in der Timeline verwendet und kann nicht entfernt werden.");
            return;
        }

        MediaItems.Remove(item);
        SelectedMedia = MediaItems.FirstOrDefault();
    }

    // ---------------------------------------------------------------- Timeline

    [RelayCommand(CanExecute = nameof(HasSelectedMedia))]
    private void AddSelectedToTimeline()
    {
        if (SelectedMedia is { } media)
        {
            AddToTimeline(media.Model);
        }
    }

    /// <summary>Hängt eine Aufnahme (oder einen Ausschnitt) ans Ende der passenden Spur an.</summary>
    public TimelineClip AddToTimeline(MediaItem media, double sourceIn = 0, double? sourceOut = null)
    {
        AdoptSettingsIfFirstClip(media);
        var track = media.IsAudioOnly
            ? Project.AudioTracks.First()
            : Project.VideoTracks.First();
        var clip = Editor.CreateClip(media, sourceIn, sourceOut);
        ApplyDefaultColor(clip, media);
        Editor.Append(track, clip);
        StatusText = $"„{media.Name}“ in {track.Name} eingefügt.";
        return clip;
    }

    /// <summary>Weist Log-Clips die Standard-LUT zu (falls in den Einstellungen festgelegt).</summary>
    public void ApplyDefaultColor(TimelineClip clip, MediaItem media)
    {
        if (media.Detection.Transfer == ColorTransfer.CanonLog3
            && _settings.Settings.DefaultCanonLog3Lut is { } lut && File.Exists(lut))
        {
            clip.ColorTransform = ColorTransform.Lut;
            clip.LutPath = lut;
        }
    }

    /// <summary>Wie in Profi-Programmen: Der erste Videoclip bestimmt Auflösung und Bildrate eines leeren Projekts.</summary>
    private void AdoptSettingsIfFirstClip(MediaItem media)
    {
        if (media.IsAudioOnly || Project.AllClips.Any())
        {
            return;
        }

        var settings = ProjectSettings.FromMedia(media.First);
        if (settings.Width != Project.Settings.Width || settings.Height != Project.Settings.Height
            || settings.FrameRate != Project.Settings.FrameRate)
        {
            Editor.ChangeSettings(settings);
            StatusText = $"Projekteinstellungen vom ersten Clip übernommen: {settings.Label}";
        }
    }

    [RelayCommand]
    public void Undo()
    {
        if (Editor.Undo())
        {
            StatusText = "Rückgängig.";
        }
    }

    [RelayCommand]
    public void Redo()
    {
        if (Editor.Redo())
        {
            StatusText = "Wiederholt.";
        }
    }

    // ---------------------------------------------------------------- Extras

    [RelayCommand]
    private void SetFfmpegFolder()
    {
        var folder = _dialogs.PickFolder("Ordner mit ffmpeg.exe und ffprobe.exe wählen");
        if (folder is null)
        {
            return;
        }

        _settings.Settings.FfmpegPath = folder;
        _settings.Save();
        _tools.Reload();
        OnPropertyChanged(nameof(FfmpegStatus));
        if (_tools.IsAvailable)
        {
            StatusText = "FFmpeg gefunden.";
        }
        else
        {
            _dialogs.Error("In diesem Ordner liegen keine ffmpeg.exe und ffprobe.exe.");
        }
    }

    [RelayCommand]
    private void ShowCameraProfiles()
    {
        var text = string.Join("\n\n", CanonCameraProfile.All.Select(p =>
            $"{p.DisplayName}\nContainer: {p.Container}\nVideo: {string.Join("; ", p.VideoCodecs)}\nTon: {string.Join("; ", p.AudioFormats)}\n"
            + string.Join("\n", p.EditingTips.Select(t => "• " + t))));
        _dialogs.Info(text, "Kameraprofile");
    }

    [RelayCommand]
    private void ShowAbout()
    {
        var version = typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "?";
        _dialogs.Info($"MovieWorker {version}\nVideoschnitt für Canon EOS R6 Mark II und EOS 70D.\n\n{_tools.StatusText}\n\n"
            + "Lizenz: Apache 2.0 · Videoverarbeitung: FFmpeg (ffmpeg.org)", "Über MovieWorker");
    }

    // ---------------------------------------------------------------- intern

    private void LoadProject(MovieProject project, string? path)
    {
        _suppressDirty = true;
        try
        {
            Editor.Load(project);
            MediaItems.Clear();
            foreach (var media in project.Media)
            {
                AddMediaViewModel(media);
            }

            SelectedMedia = MediaItems.FirstOrDefault();
            ProjectPath = path;
            IsDirty = false;
        }
        finally
        {
            _suppressDirty = false;
        }

        RaiseProjectChanged();
    }

    private MediaItemViewModel AddMediaViewModel(MediaItem media)
    {
        var vm = new MediaItemViewModel(media, () => Editor.NotifyMediaChanged());
        MediaItems.Add(vm);
        _ = LoadThumbnailAsync(vm);
        return vm;
    }

    private async Task LoadThumbnailAsync(MediaItemViewModel vm)
    {
        if (vm.Model.IsAudioOnly)
        {
            return;
        }

        var seconds = Math.Min(1.0, vm.Model.Duration.TotalSeconds / 2);
        vm.Thumbnail = await _thumbnails.GetAsync(vm.Model.PreviewPath, seconds, 90);
    }

    private void OnEditorChanged(object? sender, EventArgs e)
    {
        if (!_suppressDirty)
        {
            IsDirty = true;
        }

        RaiseProjectChanged();
    }

    private void RaiseProjectChanged()
    {
        OnPropertyChanged(nameof(Project));
        OnPropertyChanged(nameof(ProjectSettingsText));
        OnPropertyChanged(nameof(DurationText));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RememberProject(string path)
    {
        _settings.Settings.AddRecentProject(path);
        _settings.Save();
        RefreshRecentProjects();
    }

    private void RefreshRecentProjects()
    {
        RecentProjects.Clear();
        foreach (var path in _settings.Settings.RecentProjects)
        {
            RecentProjects.Add(path);
        }
    }

    private static IEnumerable<string> ExpandFolders(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Where(CardScanner.IsMediaFile))
                {
                    yield return file;
                }
            }
            else
            {
                yield return path;
            }
        }
    }
}
