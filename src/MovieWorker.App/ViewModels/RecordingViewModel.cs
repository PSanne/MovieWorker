using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MovieWorker.Core.Audio;
using MovieWorker.Core.Camera;
using MovieWorker.Core.Media;
using MovieWorker.Core.Projects;
using MovieWorker.Core.Timeline;

namespace MovieWorker.App.ViewModels;

/// <summary>Zielspur-Auswahl für Aufnahmen (null = neue Spur anlegen).</summary>
public sealed record TrackOption(Guid? TrackId, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Sprachaufnahme (Voice-over) und ADR (Nachsynchronisation mit Vorlauf, Schleife und Takes).</summary>
public sealed partial class RecordingViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private bool _stopRequested;

    public RecordingViewModel(MainViewModel main)
    {
        _main = main;
        Devices.Add(RecordingDevice.TestTone);
        _device = RecordingDevice.TestTone;
    }

    /// <summary>Wiedergabe ab einem Bild starten (der Programmmonitor hört darauf).</summary>
    public event EventHandler<long>? PlaybackRequested;

    public event EventHandler? StopPlaybackRequested;

    private TimelineEditor Editor => _main.Editor;

    public ObservableCollection<RecordingDevice> Devices { get; } = [];

    [ObservableProperty]
    private RecordingDevice? _device;

    public IReadOnlyList<TrackOption> TargetTracks =>
        Editor.Project.AudioTracks.Select(t => new TrackOption(t.Id, t.Name))
            .Append(new TrackOption(null, AdrMode ? "Neue Spur „ADR“" : "Neue Spur „Sprecher“"))
            .ToList();

    [ObservableProperty]
    private TrackOption? _targetTrack;

    /// <summary>Vorlauf in Sekunden: so lange läuft das Bild vor der Aufnahmestelle (zum Einfühlen).</summary>
    [ObservableProperty]
    private double _preRollSeconds = 2;

    /// <summary>ADR: nur den In/Out-Bereich aufnehmen (Stopp automatisch am Out-Punkt).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetTracks), nameof(ModeText))]
    private bool _adrMode;

    /// <summary>ADR: nach jedem Take automatisch den nächsten aufnehmen, bis „Stopp“.</summary>
    [ObservableProperty]
    private bool _loop;

    /// <summary>ADR: Originalton im Bereich stummschalten, wenn ein Take verwendet wird.</summary>
    [ObservableProperty]
    private bool _muteOriginal = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordButtonText))]
    private bool _isRecording;

    [ObservableProperty]
    private string _statusText = "Mikrofon wählen, Abspielkopf setzen und „Aufnehmen“ drücken. Für ADR In/Out um den Dialog setzen.";

    /// <summary>Ordner für Aufnahmen (sonst neben dem Projekt bzw. unter Dokumente\MovieWorker).</summary>
    public string? FolderOverride { get; set; }

    public string RecordButtonText => IsRecording ? "■ Stopp" : "● Aufnehmen";

    public string ModeText => AdrMode
        ? "ADR: Das Bild läuft mit Vorlauf über den In/Out-Bereich, die Aufnahme stoppt am Out-Punkt. Jeder Durchgang ist ein Take."
        : "Sprachaufnahme: Das Bild läuft ab dem Abspielkopf, die Aufnahme läuft bis „Stopp“.";

    // ---------- ADR-Szenen und Takes ----------
    public IReadOnlyList<AdrCue> Cues => Editor.Project.AdrCues;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Takes), nameof(Dialog), nameof(HasCue))]
    private AdrCue? _selectedCue;

    public bool HasCue => SelectedCue is not null;

    public IReadOnlyList<AdrTakeViewModel> Takes => SelectedCue is { } cue
        ? cue.Takes.OrderByDescending(t => t.Number)
            .Select(t => new AdrTakeViewModel(t, t.Id == cue.UsedTakeId, Editor.Project.Settings.FrameRate.FramesToSeconds(cue.LengthFrames)))
            .ToList()
        : [];

    public string Dialog
    {
        get => SelectedCue?.Dialog ?? string.Empty;
        set
        {
            if (SelectedCue is { } cue)
            {
                Editor.UpdateAdrCue(cue.Id, "Dialogtext", c => c.Dialog = value);
            }
        }
    }

    [RelayCommand]
    private async Task RefreshDevicesAsync()
    {
        if (_main.Tools.Client is not { } client)
        {
            StatusText = "FFmpeg fehlt – ohne FFmpeg keine Aufnahme.";
            return;
        }

        var devices = await client.ListRecordingDevicesAsync();
        var previous = Device?.Name;
        Devices.Clear();
        foreach (var device in devices)
        {
            Devices.Add(device);
        }

        Devices.Add(RecordingDevice.TestTone);
        Device = Devices.FirstOrDefault(d => d.Name == previous) ?? Devices[0];
        StatusText = devices.Count == 0
            ? "Kein Mikrofon gefunden. Ist eines angeschlossen und unter Windows freigegeben (Einstellungen → Datenschutz → Mikrofon)?"
            : $"{devices.Count} Mikrofon(e) gefunden.";
    }

    [RelayCommand]
    private void CreateCueFromRange()
    {
        var project = Editor.Project;
        if (!project.HasRange)
        {
            StatusText = "Setze zuerst In (I) und Out (O) um den Dialog, der neu gesprochen werden soll.";
            return;
        }

        SelectedCue = Editor.AddAdrCue(project.MarkIn!.Value, project.MarkOut!.Value);
        AdrMode = true;
        Sync();
    }

    [RelayCommand]
    private void RemoveCue()
    {
        if (SelectedCue is { } cue && Editor.RemoveAdrCue(cue.Id))
        {
            SelectedCue = null;
            Sync();
        }
    }

    /// <summary>Aufnahme starten – oder stoppen, wenn sie schon läuft.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ToggleRecordAsync()
    {
        if (IsRecording)
        {
            _stopRequested = true;
            return;
        }

        if (Device is not { } device)
        {
            StatusText = "Bitte zuerst ein Mikrofon wählen.";
            return;
        }

        if (_main.Tools.Client is not { } client)
        {
            StatusText = "FFmpeg fehlt – ohne FFmpeg keine Aufnahme.";
            return;
        }

        var project = Editor.Project;
        var rate = project.Settings.FrameRate;
        AdrCue? cue = null;
        if (AdrMode)
        {
            if (!project.HasRange && SelectedCue is null)
            {
                StatusText = "Für ADR zuerst In (I) und Out (O) um den Dialog setzen.";
                return;
            }

            cue = SelectedCue is { } selected && (!project.HasRange || (selected.StartFrame == project.MarkIn && selected.EndFrame == project.MarkOut))
                ? selected
                : Editor.AddAdrCue(project.MarkIn!.Value, project.MarkOut!.Value);
            SelectedCue = Editor.Project.AdrCues.First(c => c.Id == cue.Id);
        }

        var trackId = EnsureTargetTrack();
        _stopRequested = false;
        IsRecording = true;
        try
        {
            do
            {
                await RecordOnceAsync(client, device, cue?.Id, trackId);
                if (cue is not null && Loop && !_stopRequested)
                {
                    StatusText = "Nächster Take gleich …";
                    await Task.Delay(1200);
                }
            }
            while (cue is not null && Loop && !_stopRequested);
        }
        catch (Exception ex) when (ex is FfmpegException or IOException or InvalidOperationException)
        {
            StatusText = "Aufnahme nicht möglich: " + ex.Message;
        }
        finally
        {
            StopPlaybackRequested?.Invoke(this, EventArgs.Empty);
            IsRecording = false;
            Sync();
        }
    }

    private Guid EnsureTargetTrack()
    {
        if (TargetTrack?.TrackId is { } id && Editor.Project.Tracks.Any(t => t.Id == id))
        {
            return id;
        }

        var track = Editor.AddTrack(TrackKind.Audio, AdrMode ? "ADR" : "Sprecher");
        TargetTrack = new TrackOption(track.Id, track.Name);
        OnPropertyChanged(nameof(TargetTracks));
        return track.Id;
    }

    private async Task RecordOnceAsync(FfmpegClient client, RecordingDevice device, Guid? cueId, Guid trackId)
    {
        var project = Editor.Project;
        var rate = project.Settings.FrameRate;
        var cue = cueId is { } cid ? project.AdrCues.First(c => c.Id == cid) : null;
        var timelineStart = cue?.StartFrame ?? _main.PlayheadFrame;
        var startFrame = Math.Max(0, timelineStart - rate.SecondsToFrames(PreRollSeconds));
        var preRoll = rate.FramesToSeconds(timelineStart - startFrame);
        double? needed = cue is null ? null : preRoll + rate.FramesToSeconds(cue.LengthFrames) + 0.3;

        var folder = RecordingsFolder();
        Directory.CreateDirectory(folder);
        var number = cue?.NextTakeNumber ?? 0;
        var name = cue is null ? "Sprecher" : $"{cue.Name} Take {number}";
        var path = Path.Combine(folder, $"{name} {DateTime.Now:yyyy-MM-dd HH-mm-ss}.wav");

        StatusText = "Mikrofon wird geöffnet …";
        await using var recording = client.StartRecording(device, path);
        await recording.WaitUntilStartedAsync(TimeSpan.FromSeconds(10));

        // Ab hier entspricht die Stelle `offset` in der Aufnahme dem Bild `startFrame` der Timeline.
        var offset = recording.Seconds;
        PlaybackRequested?.Invoke(this, startFrame);
        while (!_stopRequested && recording.IsRunning)
        {
            var elapsed = recording.Seconds - offset;
            if (needed is { } n && elapsed >= n)
            {
                break;
            }

            StatusText = elapsed < preRoll
                ? $"Vorlauf … {Math.Ceiling(preRoll - elapsed):0}"
                : $"● Aufnahme läuft – {elapsed - preRoll:0.0} s{(needed is { } total ? $" von {total - preRoll - 0.3:0.0} s" : string.Empty)}";
            await Task.Delay(50);
        }

        var recorded = recording.Seconds - offset;
        StopPlaybackRequested?.Invoke(this, EventArgs.Empty);
        await recording.StopAsync();
        if (needed is { } full && recorded < full - 0.35)
        {
            // ADR-Take vor dem Out-Punkt abgebrochen: nicht verwenden.
            TryDelete(path);
            StatusText = "Take abgebrochen – er wird nicht verwendet.";
            return;
        }

        var info = await client.ProbeAsync(path);
        var media = MediaItem.FromRecording(new RecordingGroup([info]));
        _main.AddRecordedMedia(media);
        var sourceIn = offset + preRoll;
        var available = info.Duration.TotalSeconds - sourceIn;
        if (available < 0.2)
        {
            StatusText = "Die Aufnahme war zu kurz (sie wurde vor dem Vorlauf-Ende gestoppt).";
            return;
        }

        if (cue is not null)
        {
            var take = new AdrTake { Number = number, Path = path, OffsetSeconds = sourceIn, MediaId = media.Id };
            Editor.AddAdrTake(cue.Id, take);
            UseTakeCore(cue.Id, take, trackId);
            StatusText = $"Take {number} aufgenommen und eingesetzt ({available:0.0} s).";
        }
        else
        {
            var clip = Editor.CreateClip(media, sourceIn, info.Duration.TotalSeconds);
            Editor.Place(EditMode.Overwrite, trackId, clip, timelineStart);
            StatusText = $"Sprachaufnahme eingesetzt ({available:0.0} s).";
        }
    }

    private void UseTakeCore(Guid cueId, AdrTake take, Guid trackId)
    {
        var cue = Editor.Project.AdrCues.First(c => c.Id == cueId);
        var media = take.MediaId is { } mid ? Editor.Project.FindMedia(mid) : null;
        if (media is null)
        {
            StatusText = "Die Aufnahme dieses Takes ist nicht mehr im Projekt.";
            return;
        }

        var rate = Editor.Project.Settings.FrameRate;
        var clip = Editor.CreateClip(media, take.OffsetSeconds, Math.Min(media.Recording.TotalDuration.TotalSeconds, take.OffsetSeconds + rate.FramesToSeconds(cue.LengthFrames)));
        Editor.UseAdrTake(cueId, take.Id, trackId, clip);
        if (MuteOriginal)
        {
            Editor.MuteOriginalAudio(cue.StartFrame, cue.EndFrame);
        }
    }

    /// <summary>Einen älteren Take statt des aktuellen verwenden.</summary>
    [RelayCommand]
    private void UseTake(AdrTakeViewModel? take)
    {
        if (take is null || SelectedCue is not { } cue)
        {
            return;
        }

        var trackId = Editor.Project.Tracks.FirstOrDefault(t => t.Kind == TrackKind.Audio
            && t.Clips.Any(c => c.StartFrame == cue.StartFrame && Editor.Project.FindMedia(c.MediaId) is { } m
                && cue.Takes.Any(k => k.MediaId == m.Id)))?.Id ?? EnsureTargetTrack();
        UseTakeCore(cue.Id, take.Take, trackId);
        StatusText = $"{take.Take} wird verwendet.";
        Sync();
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // egal
        }
        catch (UnauthorizedAccessException)
        {
            // egal
        }
    }

    private string RecordingsFolder()
    {
        if (FolderOverride is { } folder)
        {
            return folder;
        }

        if (_main.ProjectPath is { } projectPath)
        {
            return Path.Combine(Path.GetDirectoryName(projectPath)!, Path.GetFileNameWithoutExtension(projectPath) + " Aufnahmen");
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MovieWorker", "Aufnahmen");
    }

    /// <summary>Nach Projektänderungen abgleichen.</summary>
    public void Sync()
    {
        if (SelectedCue is { } cue)
        {
            var current = Editor.Project.AdrCues.FirstOrDefault(c => c.Id == cue.Id);
            if (!ReferenceEquals(current, cue))
            {
                SelectedCue = current;
            }
        }

        if (TargetTrack?.TrackId is { } id && Editor.Project.Tracks.All(t => t.Id != id))
        {
            TargetTrack = null;
        }

        OnPropertyChanged(nameof(Cues));
        OnPropertyChanged(nameof(Takes));
        OnPropertyChanged(nameof(TargetTracks));
        OnPropertyChanged(nameof(Dialog));
    }
}

/// <summary>Ein Take in der Liste.</summary>
public sealed class AdrTakeViewModel(AdrTake take, bool used, double seconds)
{
    public AdrTake Take { get; } = take;

    public bool IsUsed { get; } = used;

    public string Text => $"{(IsUsed ? "✔ " : string.Empty)}Take {Take.Number} · {seconds:0.0} s · {Take.CreatedAt:HH:mm}";
}
