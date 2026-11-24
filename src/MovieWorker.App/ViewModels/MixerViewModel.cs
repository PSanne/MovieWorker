using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MovieWorker.App.Services;
using MovieWorker.Core.Audio;
using MovieWorker.Core.Export;
using MovieWorker.Core.Timeline;

namespace MovieWorker.App.ViewModels;

/// <summary>Eintrag in der Ducking-Auswahl: „leiser werden, wenn auf dieser Spur Ton ist“.</summary>
public sealed record DuckOption(Guid? TrackId, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Eintrag in der Auswahl des Lautheits-Ziels.</summary>
public sealed record LoudnessOption(double? Target, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Ein Kanalzug im Mischpult (eine Spur).</summary>
public sealed partial class MixerStripViewModel(TimelineEditor editor, Guid trackId, Func<IReadOnlyList<DuckOption>> duckOptions) : ObservableObject
{
    public Guid TrackId { get; } = trackId;

    private TimelineTrack Track => editor.Project.Tracks.FirstOrDefault(t => t.Id == TrackId) ?? new TimelineTrack();

    public string Name => string.IsNullOrWhiteSpace(Track.Name) ? (Track.Kind == TrackKind.Video ? "Video (Kameraton)" : "Ton") : Track.Name;

    public string KindText => Track.Kind switch
    {
        TrackKind.Video => "Kameraton der Videospur",
        TrackKind.Title => "Titel",
        _ => "Tonspur",
    };

    private void Set(string description, Action<TimelineTrack> change) => editor.SetTrackAudio(TrackId, description, change);

    public double VolumeDb
    {
        get => Track.VolumeDb;
        set => Set("Spurlautstärke", t => t.VolumeDb = Math.Round(Math.Clamp(value, -60, 12), 1));
    }

    public string VolumeText => Track.VolumeDb <= -59.9 ? "−∞ dB" : $"{Track.VolumeDb:+0.0;−0.0;0.0} dB";

    public double Pan
    {
        get => Track.Audio.Pan;
        set => Set("Panorama", t => t.Audio.Pan = Math.Round(Math.Clamp(value, -1, 1), 2));
    }

    public string PanText => Math.Abs(Track.Audio.Pan) < 0.005 ? "Mitte" : Track.Audio.Pan < 0 ? $"L {-Track.Audio.Pan * 100:0}" : $"R {Track.Audio.Pan * 100:0}";

    public bool Muted
    {
        get => Track.Muted;
        set => editor.SetTrackMuted(TrackId, value);
    }

    public bool Solo
    {
        get => Track.Audio.Solo;
        set => Set("Solo", t => t.Audio.Solo = value);
    }

    public double HighPassHz
    {
        get => Track.Audio.HighPassHz;
        set => Set("Hochpass", t => t.Audio.HighPassHz = Math.Round(Math.Clamp(value, 0, 400)));
    }

    public double NoiseReduction
    {
        get => Track.Audio.NoiseReduction;
        set => Set("Rauschminderung", t => t.Audio.NoiseReduction = Math.Round(Math.Clamp(value, 0, 1), 2));
    }

    public double EqLow
    {
        get => Track.Audio.EqLow;
        set => Set("EQ Tiefen", t => t.Audio.EqLow = Math.Round(Math.Clamp(value, -12, 12), 1));
    }

    public double EqMid
    {
        get => Track.Audio.EqMid;
        set => Set("EQ Mitten", t => t.Audio.EqMid = Math.Round(Math.Clamp(value, -12, 12), 1));
    }

    public double EqHigh
    {
        get => Track.Audio.EqHigh;
        set => Set("EQ Höhen", t => t.Audio.EqHigh = Math.Round(Math.Clamp(value, -12, 12), 1));
    }

    public bool Compressor
    {
        get => Track.Audio.Compressor;
        set => Set("Kompressor", t => t.Audio.Compressor = value);
    }

    public double CompressorThresholdDb
    {
        get => Track.Audio.CompressorThresholdDb;
        set => Set("Kompressor-Schwelle", t => t.Audio.CompressorThresholdDb = Math.Round(Math.Clamp(value, -40, 0)));
    }

    public double CompressorRatio
    {
        get => Track.Audio.CompressorRatio;
        set => Set("Kompressor-Verhältnis", t => t.Audio.CompressorRatio = Math.Round(Math.Clamp(value, 1, 20), 1));
    }

    public double Reverb
    {
        get => Track.Audio.Reverb;
        set => Set("Hall", t => t.Audio.Reverb = Math.Round(Math.Clamp(value, 0, 1), 2));
    }

    public IReadOnlyList<DuckOption> DuckOptions => duckOptions().Where(o => o.TrackId != TrackId).ToList();

    public DuckOption? DuckBy
    {
        get => DuckOptions.FirstOrDefault(o => o.TrackId == Track.Audio.DuckBy) ?? DuckOptions.FirstOrDefault();
        set => Set("Ducking", t => t.Audio.DuckBy = value?.TrackId);
    }

    public double DuckAmountDb
    {
        get => Track.Audio.DuckAmountDb;
        set => Set("Ducking-Stärke", t => t.Audio.DuckAmountDb = Math.Round(Math.Clamp(value, 3, 30)));
    }

    public string EffectsText
    {
        get
        {
            var count = Track.Audio.Filters().Count(f => !f.StartsWith("pan=", StringComparison.Ordinal));
            return count == 0 ? "keine Effekte" : count == 1 ? "1 Effekt" : $"{count} Effekte";
        }
    }

    /// <summary>Pegel 0…1 für den Balken (bei Wiedergabe bzw. an der Abspielposition).</summary>
    [ObservableProperty]
    private double _level;

    [ObservableProperty]
    private string _levelText = "−∞";

    /// <summary>Kurze Zusammenfassung für die Timeline-Spurköpfe und Tests.</summary>
    public string Summary => $"{Name}: {VolumeText}, {PanText}{(Track.Audio.Solo ? ", Solo" : string.Empty)}{(Track.Muted ? ", stumm" : string.Empty)}, {EffectsText}";

    public void UpdateLevel(double db)
    {
        Level = LevelMeter.ToBar(db);
        LevelText = db <= LevelMeter.Floor ? "−∞" : $"{db:0}";
    }

    public void Reset()
    {
        Set("Kanalzug zurücksetzen", t =>
        {
            var duck = t.Audio.DuckBy;
            t.Audio = new TrackAudio { DuckBy = duck };
            t.VolumeDb = 0;
        });
    }

    public void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>Reiter „Ton“: Mischpult mit allen Spuren und der Summe.</summary>
public sealed partial class MixerViewModel : ObservableObject
{
    private readonly TimelineEditor _editor;
    private readonly MediaToolsService _tools;
    private readonly WaveformService _waveforms;

    public MixerViewModel(TimelineEditor editor, MediaToolsService tools, WaveformService waveforms)
    {
        _editor = editor;
        _tools = tools;
        _waveforms = waveforms;
        Sync();
    }

    public ObservableCollection<MixerStripViewModel> Strips { get; } = [];

    public IReadOnlyList<LoudnessOption> LoudnessTargets { get; } =
        LoudnessMeasurement.Targets.Select(t => new LoudnessOption(t.Target, t.Label)).ToList();

    public IReadOnlyList<DuckOption> DuckOptions =>
        new[] { new DuckOption(null, "Kein Ducking") }
            .Concat(_editor.Project.Tracks.Where(t => t.Kind != TrackKind.Title)
                .Select(t => new DuckOption(t.Id, $"Leiser bei Ton auf „{(string.IsNullOrWhiteSpace(t.Name) ? "Spur" : t.Name)}“")))
            .ToList();

    public double MasterVolumeDb
    {
        get => _editor.Project.Master.VolumeDb;
        set => _editor.SetMaster("Summenlautstärke", m => m.VolumeDb = Math.Round(Math.Clamp(value, -30, 12), 1));
    }

    public string MasterVolumeText => $"{_editor.Project.Master.VolumeDb:+0.0;−0.0;0.0} dB";

    public bool Limiter
    {
        get => _editor.Project.Master.Limiter;
        set => _editor.SetMaster("Limiter", m => m.Limiter = value);
    }

    public LoudnessOption? LoudnessTarget
    {
        get => LoudnessTargets.FirstOrDefault(o => o.Target == _editor.Project.Master.LoudnessTarget) ?? LoudnessTargets[0];
        set => _editor.SetMaster("Lautheits-Ziel", m => m.LoudnessTarget = value?.Target);
    }

    [ObservableProperty]
    private string _loudnessText = "Noch nicht gemessen. „Lautheit messen“ spielt die ganze Mischung einmal schnell durch.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MeasureLoudnessCommand))]
    private bool _isMeasuring;

    /// <summary>Zuletzt gemessene Lautheit der Mischung.</summary>
    public LoudnessMeasurement? LastMeasurement { get; private set; }

    [RelayCommand(CanExecute = nameof(CanMeasure))]
    private async Task MeasureLoudnessAsync()
    {
        if (_editor.Project.DurationFrames <= 0)
        {
            LoudnessText = "Die Timeline ist leer – es gibt nichts zu messen.";
            return;
        }

        IsMeasuring = true;
        LoudnessText = "Lautheit wird gemessen …";
        try
        {
            var job = ExportBuilder.BuildLoudnessMeasurement(_editor.Project.Clone());
            var stderr = await _tools.Require().RunForStderrAsync(job.Arguments);
            LastMeasurement = LoudnessMeasurement.Parse(stderr);
            LoudnessText = LastMeasurement is not { } m
                ? "Die Messung hat keine Werte geliefert."
                : m.IntegratedLufs <= -70
                    ? "Die Mischung ist stumm."
                    : $"{m}\n{Advice(m)}";
        }
        catch (Exception ex) when (ex is Core.Media.FfmpegException or InvalidOperationException or System.IO.IOException)
        {
            LoudnessText = "Messung nicht möglich: " + ex.Message;
        }
        finally
        {
            IsMeasuring = false;
        }
    }

    private bool CanMeasure() => !IsMeasuring;

    private string Advice(LoudnessMeasurement m)
    {
        if (_editor.Project.Master.LoudnessTarget is { } target)
        {
            return $"Beim Export wird der Ton genau auf {target:0} LUFS gebracht ({target - m.IntegratedLufs:+0.0;−0.0} dB).";
        }

        return m.TruePeakDb > -1
            ? "Achtung: Die Spitzen liegen nah an 0 dB – Limiter einschalten oder ein Lautheits-Ziel wählen."
            : "Tipp: Für YouTube „−14 LUFS“ wählen, dann passt der Export automatisch.";
    }

    [RelayCommand]
    private void ResetStrip(MixerStripViewModel? strip) => strip?.Reset();

    /// <summary>Spuren abgleichen (nach Änderungen am Projekt).</summary>
    public void Sync()
    {
        var tracks = _editor.Project.Tracks.Where(t => t.Kind != TrackKind.Title).Select(t => t.Id).ToList();
        if (!tracks.SequenceEqual(Strips.Select(s => s.TrackId)))
        {
            Strips.Clear();
            foreach (var id in tracks)
            {
                Strips.Add(new MixerStripViewModel(_editor, id, () => DuckOptions));
            }
        }
        else
        {
            foreach (var strip in Strips)
            {
                strip.Refresh();
            }
        }

        OnPropertyChanged(nameof(MasterVolumeDb));
        OnPropertyChanged(nameof(MasterVolumeText));
        OnPropertyChanged(nameof(Limiter));
        OnPropertyChanged(nameof(LoudnessTarget));
    }

    /// <summary>Pegelanzeigen auf die Abspielposition setzen.</summary>
    public void UpdateMeters(long frame)
    {
        var project = _editor.Project;
        foreach (var strip in Strips)
        {
            if (project.Tracks.FirstOrDefault(t => t.Id == strip.TrackId) is not { } track)
            {
                continue;
            }

            strip.UpdateLevel(LevelMeter.TrackLevelDb(project, track, frame,
                clip => _waveforms.TryGet(clip.SourcePaths, static () => { })));
        }
    }
}
