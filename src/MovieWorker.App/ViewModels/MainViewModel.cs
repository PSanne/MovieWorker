using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
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
        Waveforms = new WaveformService(tools);
        Proxies = new ProxyService(tools, settings);
        Proxies.ProxyFinished += (_, _) => Editor.NotifyMediaChanged();
        MediaItem.UseProxies = settings.Settings.UseProxies;
        ExportQueue = new ExportQueueService(tools);
        ExportQueue.JobFinished += (_, job) => StatusText = job.State == ExportJobState.Done
            ? $"Export fertig: {job.FileName}"
            : $"Export {job.StateText.ToLowerInvariant()}: {job.FileName} – {job.Detail}";
        AudioSync = new AudioSyncService(tools);
        MediaView = CollectionViewSource.GetDefaultView(MediaItems);
        MediaView.Filter = o => o is MediaItemViewModel m && CurrentFilter.Matches(m.Model);
        RebuildBins();
        ApplySort();
        RefreshRecentProjects();
        StatusText = tools.IsAvailable ? "Bereit." : tools.StatusText;
    }

    public TimelineEditor Editor { get; }

    public IDialogService Dialogs => _dialogs;

    public SettingsService Settings => _settings;

    public MediaToolsService Tools => _tools;

    public ThumbnailService Thumbnails => _thumbnails;

    public WaveformService Waveforms { get; }

    public ProxyService Proxies { get; }

    public ExportQueueService ExportQueue { get; }

    public string ProjectName => ProjectPath is null ? "Mein Film" : Path.GetFileNameWithoutExtension(ProjectPath);

    /// <summary>Vorschau mit Proxys (statt Originalen).</summary>
    public bool UseProxies
    {
        get => _settings.Settings.UseProxies;
        set
        {
            _settings.Settings.UseProxies = value;
            MediaItem.UseProxies = value;
            _settings.Save();
            OnPropertyChanged();
            Editor.NotifyMediaChanged();
        }
    }

    public bool AutoCreateProxies
    {
        get => _settings.Settings.AutoCreateProxies;
        set
        {
            _settings.Settings.AutoCreateProxies = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public ObservableCollection<MediaItemViewModel> MediaItems { get; } = [];

    /// <summary>Gefilterte und sortierte Ansicht des Medienpools.</summary>
    public ICollectionView MediaView { get; }

    public AudioSyncService AudioSync { get; }

    public ObservableCollection<BinViewModel> Bins { get; } = [];

    /// <summary>Ordner, in die eine Aufnahme einsortiert werden kann (inkl. „Nicht einsortiert“).</summary>
    public IEnumerable<BinViewModel> TargetBins => Bins.Where(b => b.Kind != BinKind.All);

    public IEnumerable<MediaItemViewModel> AudioMediaItems => MediaItems.Where(m => m.IsAudioOnly);

    public IReadOnlyList<string> RatingFilters { get; } = ["Alle Bewertungen", "★ und mehr", "★★ und mehr", "★★★ und mehr", "★★★★ und mehr", "★★★★★"];

    public IReadOnlyList<string> KindFilters { get; } = ["Alles", "Nur Video", "Nur Ton"];

    public IReadOnlyList<string> SortModes { get; } = ["Name", "Aufnahmezeit", "Bewertung", "Länge"];

    [ObservableProperty]
    private BinViewModel? _selectedBin;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _minRatingFilter;

    [ObservableProperty]
    private int _kindFilter;

    [ObservableProperty]
    private int _sortMode;

    [ObservableProperty]
    private MediaItemViewModel? _selectedSyncAudio;

    [ObservableProperty]
    private bool _isSyncing;

    private MediaFilter CurrentFilter => new(
        SearchText,
        MinRatingFilter,
        (MediaKindFilter)KindFilter,
        BinId: SelectedBin?.Kind == BinKind.Folder ? SelectedBin.Id : null,
        OnlyUnsorted: SelectedBin?.Kind == BinKind.Unsorted);

    partial void OnSelectedBinChanged(BinViewModel? value) => MediaView.Refresh();

    partial void OnSearchTextChanged(string value) => MediaView.Refresh();

    partial void OnMinRatingFilterChanged(int value) => MediaView.Refresh();

    partial void OnKindFilterChanged(int value) => MediaView.Refresh();

    partial void OnSortModeChanged(int value) => ApplySort();

    /// <summary>Ordner der ausgewählten Aufnahme (für den Inspektor).</summary>
    public BinViewModel? SelectedMediaBin
    {
        get => SelectedMedia is { } m ? TargetBins.FirstOrDefault(b => b.Id == m.Model.BinId) : null;
        set
        {
            if (SelectedMedia is { } m && value is not null && m.Model.BinId != value.Id)
            {
                m.Model.BinId = value.Id;
                Editor.NotifyMediaChanged();
                MediaView.Refresh();
                OnPropertyChanged();
            }
        }
    }

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
    [NotifyPropertyChangedFor(nameof(HasSelectedMedia), nameof(SelectedMediaBin))]
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayheadText))]
    private long _playheadFrame;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedClipCommand), nameof(RippleDeleteSelectedClipCommand))]
    private Guid? _selectedClipId;

    [ObservableProperty]
    private ClipViewModel? _selectedClip;

    [ObservableProperty]
    private bool _snapping = true;

    public string PlayheadText => Timecode.FromFrames(PlayheadFrame, Project.Settings.FrameRate, Project.Settings.DropFrameTimecode);

    public bool HasSelectedClip => SelectedClipId is not null && Project.FindClip(SelectedClipId.Value) is not null;

    partial void OnSelectedClipIdChanged(Guid? value) =>
        SelectedClip = value is { } id && Project.FindClip(id) is not null ? new ClipViewModel(Editor, id) : null;

    partial void OnPlayheadFrameChanged(long value)
    {
        if (value < 0)
        {
            PlayheadFrame = 0;
        }
    }

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

            if (_settings.Settings.AutoCreateProxies)
            {
                _ = CreateProxiesAsync(viewModels.Where(v => Proxies.NeedsProxy(v)).ToList());
            }

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

    // ---------------------------------------------------------------- Ordner & Synchronisation (Sprint 6)

    [RelayCommand]
    public void NewBin()
    {
        var name = _dialogs.Prompt("Name des neuen Ordners:", "Neuer Ordner", "Neuer Ordner");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var bin = new MediaBin { Name = name };
        Project.Bins.Add(bin);
        Editor.NotifyMediaChanged();
        RebuildBins();
        SelectedBin = Bins.First(b => b.Id == bin.Id);

        // Ausgewählte Aufnahme gleich einsortieren, wenn sie noch keinen Ordner hat.
        if (SelectedMedia is { } m && m.Model.BinId is null)
        {
            m.Model.BinId = bin.Id;
            MediaView.Refresh();
        }
    }

    [RelayCommand]
    private void RenameBin()
    {
        if (SelectedBin is not { Kind: BinKind.Folder, Bin: { } bin })
        {
            return;
        }

        var name = _dialogs.Prompt("Neuer Name des Ordners:", "Ordner umbenennen", bin.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            bin.Name = name;
            Editor.NotifyMediaChanged();
            SelectedBin.Refresh();
            OnPropertyChanged(nameof(TargetBins));
        }
    }

    [RelayCommand]
    private void DeleteBin()
    {
        if (SelectedBin is not { Kind: BinKind.Folder, Bin: { } bin }
            || !_dialogs.Confirm($"Ordner „{bin.Name}“ löschen? Die Aufnahmen darin bleiben erhalten (dann „nicht einsortiert“)."))
        {
            return;
        }

        foreach (var media in Project.Media.Where(m => m.BinId == bin.Id))
        {
            media.BinId = null;
        }

        Project.Bins.Remove(bin);
        Editor.NotifyMediaChanged();
        RebuildBins();
    }

    public void MoveMediaToBin(MediaItemViewModel item, Guid? binId)
    {
        item.Model.BinId = binId;
        Editor.NotifyMediaChanged();
        MediaView.Refresh();
        OnPropertyChanged(nameof(SelectedMediaBin));
    }

    private void RebuildBins()
    {
        var selected = SelectedBin?.Id;
        var selectedKind = SelectedBin?.Kind ?? BinKind.All;
        Bins.Clear();
        Bins.Add(new BinViewModel(BinKind.All));
        Bins.Add(new BinViewModel(BinKind.Unsorted));
        foreach (var bin in Project.Bins)
        {
            Bins.Add(new BinViewModel(BinKind.Folder, bin));
        }

        SelectedBin = Bins.FirstOrDefault(b => b.Kind == selectedKind && b.Id == selected) ?? Bins[0];
        OnPropertyChanged(nameof(TargetBins));
        OnPropertyChanged(nameof(SelectedMediaBin));
    }

    private void ApplySort()
    {
        MediaView.SortDescriptions.Clear();
        var (property, direction) = SortMode switch
        {
            1 => (nameof(MediaItemViewModel.RecordedSort), ListSortDirection.Ascending),
            2 => (nameof(MediaItemViewModel.Rating), ListSortDirection.Descending),
            3 => (nameof(MediaItemViewModel.DurationSeconds), ListSortDirection.Descending),
            _ => (nameof(MediaItemViewModel.Name), ListSortDirection.Ascending),
        };
        MediaView.SortDescriptions.Add(new SortDescription(property, direction));
        if (property != nameof(MediaItemViewModel.Name))
        {
            MediaView.SortDescriptions.Add(new SortDescription(nameof(MediaItemViewModel.Name), ListSortDirection.Ascending));
        }
    }

    /// <summary>Legt den gewählten externen Ton per Wellenform an die ausgewählte Kameraaufnahme an.</summary>
    [RelayCommand]
    public async Task<bool> SyncSelectedAsync()
    {
        if (SelectedMedia is not { IsAudioOnly: false } video || SelectedSyncAudio is not { } audio)
        {
            _dialogs.Error("Bitte eine Kameraaufnahme auswählen und darunter den externen Ton (z. B. vom Rekorder) wählen.");
            return false;
        }

        return await SyncPairAsync(video, audio, quiet: false);
    }

    public async Task<bool> SyncPairAsync(MediaItemViewModel video, MediaItemViewModel audio, bool quiet)
    {
        if (!video.Model.First.HasAudio)
        {
            if (!quiet)
            {
                _dialogs.Error("Diese Aufnahme hat keinen Kameraton – ohne Ton lässt sich nicht synchronisieren.");
            }

            return false;
        }

        IsSyncing = true;
        StatusText = $"Synchronisiere „{video.Name}“ mit „{audio.Name}“ …";
        try
        {
            var result = await AudioSync.SyncAsync(video.Model, audio.Model);
            if (!result.IsReliable)
            {
                StatusText = $"Keine eindeutige Übereinstimmung zwischen „{video.Name}“ und „{audio.Name}“ gefunden.";
                if (!quiet)
                {
                    _dialogs.Error(StatusText + "\nTipp: Bei der Aufnahme einmal deutlich klatschen – das hilft der Synchronisation.");
                }

                return false;
            }

            video.Model.SyncedAudio = new SyncedAudio
            {
                AudioMediaId = audio.Model.Id,
                OffsetSeconds = result.OffsetSeconds,
                Confidence = result.Confidence,
            };
            video.RefreshAll();
            Editor.NotifyMediaChanged();
            StatusText = $"Synchronisiert: Versatz {result.OffsetSeconds:+0.000;-0.000} s (Sicherheit {result.Confidence:P0}). Beim Einfügen kommt der externe Ton mit.";
            return true;
        }
        catch (Exception ex) when (ex is Core.Media.FfmpegException or IOException or InvalidOperationException)
        {
            StatusText = "Synchronisation fehlgeschlagen: " + ex.Message;
            return false;
        }
        finally
        {
            IsSyncing = false;
        }
    }

    /// <summary>Versucht jede Kameraaufnahme ohne Verknüpfung mit allen Tonaufnahmen und nimmt die beste.</summary>
    [RelayCommand]
    public async Task<int> AutoSyncAllAsync()
    {
        var audios = MediaItems.Where(m => m.IsAudioOnly).ToList();
        var count = 0;
        foreach (var video in MediaItems.Where(m => !m.IsAudioOnly && !m.HasSync && m.Model.First.HasAudio).ToList())
        {
            (MediaItemViewModel Audio, SyncResult Result)? best = null;
            foreach (var audio in audios)
            {
                var result = await AudioSync.SyncAsync(video.Model, audio.Model);
                if (result.IsReliable && (best is null || result.Confidence > best.Value.Result.Confidence))
                {
                    best = (audio, result);
                }
            }

            if (best is { } match)
            {
                video.Model.SyncedAudio = new SyncedAudio
                {
                    AudioMediaId = match.Audio.Model.Id, OffsetSeconds = match.Result.OffsetSeconds, Confidence = match.Result.Confidence,
                };
                video.RefreshAll();
                count++;
            }
        }

        Editor.NotifyMediaChanged();
        StatusText = count == 0 ? "Keine passenden Ton-Aufnahmen gefunden." : $"{count} Aufnahme(n) mit externem Ton synchronisiert.";
        return count;
    }

    [RelayCommand]
    private void RemoveSync()
    {
        if (SelectedMedia is { HasSync: true } media)
        {
            media.Model.SyncedAudio = null;
            media.RefreshAll();
            Editor.NotifyMediaChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedMedia))]
    private async Task CreateProxyForSelectedAsync()
    {
        if (SelectedMedia is { } item)
        {
            await CreateProxiesAsync([item], force: true);
        }
    }

    [RelayCommand]
    private async Task CreateAllProxiesAsync() =>
        await CreateProxiesAsync(MediaItems.Where(m => Proxies.NeedsProxy(m)).ToList());

    /// <summary>Erstellt Proxys nacheinander im Hintergrund; Fehler landen in der Statuszeile.</summary>
    public async Task CreateProxiesAsync(IReadOnlyList<MediaItemViewModel> items, bool force = false)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            StatusText = $"Proxy {i + 1}/{items.Count}: {item.Name} …";
            try
            {
                await Proxies.CreateAsync(item, force);
            }
            catch (Exception ex) when (ex is Core.Media.FfmpegException or IOException or InvalidOperationException)
            {
                StatusText = $"Proxy für {item.Name} fehlgeschlagen: {ex.Message}";
                return;
            }
        }

        if (items.Count > 0)
        {
            StatusText = items.Count == 1 ? "Proxy fertig." : $"{items.Count} Proxys fertig.";
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
    public TimelineClip AddToTimeline(MediaItem media, double sourceIn = 0, double? sourceOut = null) =>
        PlaceMedia(media, sourceIn, sourceOut, null, null)
        ?? throw new InvalidOperationException("Anhängen am Ende ist immer möglich.");

    /// <summary>
    /// Legt eine Aufnahme in die Timeline – am Ende oder an <paramref name="start"/>. Ist externer Ton synchronisiert,
    /// kommt er passend auf eine Tonspur (und der Kameraton wird auf Wunsch stummgeschaltet).
    /// </summary>
    public TimelineClip? PlaceMedia(MediaItem media, double sourceIn, double? sourceOut, long? start, Guid? trackId)
    {
        AdoptSettingsIfFirstClip(media);
        var kind = media.IsAudioOnly ? TrackKind.Audio : TrackKind.Video;
        var track = Project.Tracks.FirstOrDefault(t => t.Id == trackId && t.Kind == kind) ?? Project.Tracks.First(t => t.Kind == kind);
        var clip = Editor.CreateClip(media, sourceIn, sourceOut);
        ApplyDefaultColor(clip, media);

        TimelineClip? audioClip = null;
        long audioOffset = 0;
        var rate = Project.Settings.FrameRate;
        if (!media.IsAudioOnly && media.SyncedAudio is { } sync && Project.FindMedia(sync.AudioMediaId) is { } audio
            && SyncedAudioPlacement.For(clip, sync.OffsetSeconds, audio.Duration.TotalSeconds, rate) is { } placement)
        {
            audioClip = Editor.CreateClip(audio, placement.SourceIn, null);
            audioClip.LengthFrames = placement.LengthFrames;
            audioClip.Name = audio.Name + " (synchron)";
            audioOffset = placement.StartOffsetFrames;
            if (sync.ReplaceCameraAudio)
            {
                clip.AudioMuted = true;
            }
        }

        if (!Editor.InsertWithAudio(track, clip, start, audioClip, audioOffset))
        {
            return null;
        }

        SelectedClipId = clip.Id;
        StatusText = $"„{media.Name}“ in {track.Name} eingefügt" + (audioClip is null ? "." : " – mit synchronisiertem externem Ton.");
        return clip;
    }

    /// <summary>Fügt einen Ausschnitt am Abspielkopf ein (nur, wenn dort auf der Spur Platz ist).</summary>
    public bool InsertRangeAtPlayhead(MediaItem media, double sourceIn, double? sourceOut)
    {
        if (PlaceMedia(media, sourceIn, sourceOut, PlayheadFrame, null) is { } clip)
        {
            PlayheadFrame = clip.EndFrame;
            StatusText = $"„{media.Name}“ am Abspielkopf eingefügt.";
            return true;
        }

        StatusText = "Am Abspielkopf ist auf der Spur kein Platz – nutze „Ans Ende“ oder schaffe eine Lücke.";
        return false;
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
        if (media.IsAudioOnly || Project.VideoTracks.Any(t => t.Clips.Count > 0))
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

    /// <summary>Fügt eine Aufnahme an einer Stelle ein (Drag &amp; Drop aus dem Medienpool).</summary>
    public bool InsertMediaAt(Guid mediaId, Guid? trackId, long frame)
    {
        if (Project.FindMedia(mediaId) is not { } media)
        {
            return false;
        }

        if (PlaceMedia(media, 0, null, frame, trackId) is not null)
        {
            return true;
        }

        StatusText = "Dort ist kein Platz – der Clip würde einen anderen überdecken.";
        return false;
    }

    public void MoveClip(Guid clipId, long newStart, Guid trackId)
    {
        if (!Editor.Move(clipId, newStart, trackId))
        {
            StatusText = "Verschieben nicht möglich – der Clip würde einen anderen überdecken.";
        }
    }

    public void TrimClip(Guid clipId, bool atStart, long delta)
    {
        var ok = atStart ? Editor.TrimStart(clipId, delta) : Editor.TrimEnd(clipId, delta);
        if (!ok)
        {
            StatusText = "Trimmen nicht möglich (Ende der Aufnahme oder Nachbarclip erreicht).";
        }
    }

    public void ToggleTrackMute(Guid trackId)
    {
        if (Project.Tracks.FirstOrDefault(t => t.Id == trackId) is { } track)
        {
            Editor.SetTrackMuted(trackId, !track.Muted);
        }
    }

    [RelayCommand]
    public void SplitAtPlayhead()
    {
        // Ist ein Clip ausgewählt und liegt unter dem Abspielkopf, wird nur er geteilt – sonst alle Spuren.
        var only = SelectedClipId is { } id && Project.FindClip(id)?.Clip.Contains(PlayheadFrame) == true ? SelectedClipId : null;
        StatusText = Editor.SplitAt(PlayheadFrame, only) ? "Geteilt." : "An dieser Stelle gibt es nichts zu teilen.";
    }

    [RelayCommand(CanExecute = nameof(HasSelectedClip))]
    public void DeleteSelectedClip()
    {
        if (SelectedClipId is { } id && Editor.Delete(id))
        {
            SelectedClipId = null;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedClip))]
    public void RippleDeleteSelectedClip()
    {
        if (SelectedClipId is { } id && Editor.Delete(id, ripple: true))
        {
            SelectedClipId = null;
        }
    }

    [RelayCommand]
    public void StepFrames(int frames) => PlayheadFrame = Math.Max(0, PlayheadFrame + frames);

    [RelayCommand]
    public void StepSeconds(int seconds) => StepFrames(seconds * Project.Settings.FrameRate.NominalFps);

    [RelayCommand]
    public void GoToStart() => PlayheadFrame = 0;

    [RelayCommand]
    public void GoToEnd() => PlayheadFrame = Editor.Duration;

    [RelayCommand]
    public void NextEdit() => PlayheadFrame = TimelineNavigation.NextEditPoint(Project, PlayheadFrame);

    [RelayCommand]
    public void PreviousEdit() => PlayheadFrame = TimelineNavigation.PreviousEditPoint(Project, PlayheadFrame);

    [RelayCommand]
    public void MarkIn() => Editor.SetMarks(PlayheadFrame, Project.MarkOut is { } o && o > PlayheadFrame ? o : null);

    [RelayCommand]
    public void MarkOut()
    {
        var markIn = Project.MarkIn is { } i && i < PlayheadFrame ? i : 0;
        Editor.SetMarks(markIn, PlayheadFrame);
    }

    [RelayCommand]
    public void ClearMarks() => Editor.SetMarks(null, null);

    [RelayCommand]
    private void AddVideoTrack() => Editor.AddTrack(TrackKind.Video);

    [RelayCommand]
    private void AddAudioTrack() => Editor.AddTrack(TrackKind.Audio);

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

    // ---------------------------------------------------------------- Export

    [RelayCommand]
    public void Export()
    {
        if (!_tools.IsAvailable)
        {
            _dialogs.Error(_tools.StatusText);
            return;
        }

        var vm = new ExportDialogViewModel(Project, ProjectName, _dialogs, _settings);
        if (_dialogs.ShowDialog(vm) && vm.Result is { } job)
        {
            ExportQueue.Enqueue(job);
            StatusText = $"Export gestartet: {job.FileName}";
        }
    }

    /// <summary>Legt einen Export-Auftrag direkt an (ohne Dialog, z. B. für den Selbsttest).</summary>
    public ExportJobViewModel EnqueueExport(Core.Export.ExportPreset preset, string outputPath, bool useRange)
    {
        var job = new ExportJobViewModel(Project.Clone(), preset, outputPath,
            useRange && Project.HasRange ? Project.MarkIn : null, useRange && Project.HasRange ? Project.MarkOut : null);
        ExportQueue.Enqueue(job);
        return job;
    }

    [RelayCommand]
    public async Task ExportStillAsync()
    {
        if (Core.Preview.PlaybackPlanner.VideoAt(Project, PlayheadFrame) is not { } source)
        {
            _dialogs.Error("Am Abspielkopf ist kein Bild.");
            return;
        }

        var path = _dialogs.SaveFile("Einzelbild speichern", "PNG-Bild (*.png)|*.png",
            $"{ProjectName} {Timecode.FromFrames(PlayheadFrame, Project.Settings.FrameRate).Replace(':', '-').Replace(';', '-')}.png",
            _settings.Settings.LastExportFolder);
        if (path is null)
        {
            return;
        }

        await ExportStillToAsync(path);
    }

    public async Task<bool> ExportStillToAsync(string path)
    {
        if (Core.Preview.PlaybackPlanner.VideoAt(Project, PlayheadFrame) is not { } source)
        {
            return false;
        }

        try
        {
            await _tools.Require().RunAsync(
                Core.Export.FfmpegArgs.StillFrame(source.OriginalPath, source.OriginalSeconds, Core.Preview.PlaybackPlanner.PreviewColorFilter(source.Clip), path), 0);
            StatusText = $"Einzelbild gespeichert: {Path.GetFileName(path)}";
            return true;
        }
        catch (Exception ex) when (ex is Core.Media.FfmpegException or IOException or InvalidOperationException)
        {
            _dialogs.Error("Das Einzelbild konnte nicht gespeichert werden:\n" + ex.Message);
            return false;
        }
    }

    [RelayCommand]
    private void CancelExport(ExportJobViewModel? job)
    {
        if (job is not null)
        {
            ExportQueue.Cancel(job);
        }
    }

    [RelayCommand]
    private void ClearFinishedExports() => ExportQueue.ClearFinished();

    [RelayCommand]
    private void ShowExportInFolder(ExportJobViewModel? job)
    {
        if (job is { State: ExportJobState.Done } && File.Exists(job.OutputPath))
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{job.OutputPath}\"");
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
            RebuildBins();
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
        OnPropertyChanged(nameof(AudioMediaItems));
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
        if (SelectedClipId is { } id && Project.FindClip(id) is null)
        {
            SelectedClipId = null;
        }

        SelectedClip?.Refresh();
        DeleteSelectedClipCommand.NotifyCanExecuteChanged();
        RippleDeleteSelectedClipCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(PlayheadText));
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
