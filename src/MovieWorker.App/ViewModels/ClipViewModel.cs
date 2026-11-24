using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MovieWorker.Core.Projects;
using MovieWorker.Core.Time;
using MovieWorker.Core.Timeline;

namespace MovieWorker.App.ViewModels;

public sealed record SpeedChoice(double Speed, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Eigenschaften des ausgewählten Timeline-Clips im Inspektor. Jede Änderung ist rückgängig machbar.</summary>
public sealed partial class ClipViewModel : ObservableObject
{
    private readonly TimelineEditor _editor;

    public ClipViewModel(TimelineEditor editor, Guid clipId, Func<long>? playhead = null, Action<long>? setPlayhead = null,
        Func<IReadOnlyList<string>>? lutFiles = null)
    {
        _editor = editor;
        ClipId = clipId;
        Color = new ColorViewModel(editor, clipId, lutFiles ?? (() => []));
        playhead ??= () => 0;
        setPlayhead ??= _ => { };
        Motion =
        [
            new(editor, clipId, AnimatedProperty.Scale, "Zoom", "%", 10, 400, 100, playhead, setPlayhead),
            new(editor, clipId, AnimatedProperty.PositionX, "Position X", "%", -100, 100, 0, playhead, setPlayhead),
            new(editor, clipId, AnimatedProperty.PositionY, "Position Y", "%", -100, 100, 0, playhead, setPlayhead),
            new(editor, clipId, AnimatedProperty.Rotation, "Drehung", "°", -180, 180, 0, playhead, setPlayhead),
            new(editor, clipId, AnimatedProperty.Opacity, "Deckkraft", "%", 0, 100, 100, playhead, setPlayhead),
        ];
        VolumeKeys = new(editor, clipId, AnimatedProperty.Volume, "Lautstärke", "dB", -60, 12, 0, playhead, setPlayhead);
        Title = editor.Project.FindClip(clipId)?.Clip.Title is not null ? new TitleViewModel(editor, clipId) : null;
    }

    /// <summary>Farbkorrektur (Reiter „Farbe“).</summary>
    public ColorViewModel Color { get; }

    /// <summary>Titelwerkzeug (nur bei Titel-Clips).</summary>
    public TitleViewModel? Title { get; }

    public bool IsTitle => Title is not null;

    public bool IsMediaClip => !IsTitle;

    public bool IsVideoMediaClip => IsVideoClip && !IsTitle;

    // ------------------------------------------------------------ Übergang

    public static IReadOnlyList<LabeledValue<TransitionType?>> TransitionChoices { get; } =
        new LabeledValue<TransitionType?>[] { new(null, "Kein (harter Schnitt)") }
            .Concat(Enum.GetValues<TransitionType>().Select(t => new LabeledValue<TransitionType?>(t, Transition.Label(t))))
            .ToList();

    public LabeledValue<TransitionType?> SelectedTransition
    {
        get => TransitionChoices.First(t => t.Value == Clip.TransitionIn?.Type);
        set
        {
            if (value is null || value.Value == Clip.TransitionIn?.Type)
            {
                return;
            }

            _editor.SetTransition(ClipId, value.Value is { } type
                ? new Transition { Type = type, DurationFrames = Clip.TransitionIn?.DurationFrames ?? Rate.NominalFps }
                : null);
        }
    }

    public double TransitionSeconds
    {
        get => Math.Round(Rate.FramesToSeconds(Clip.TransitionIn?.DurationFrames ?? Rate.NominalFps), 2);
        set
        {
            if (Clip.TransitionIn is { } transition)
            {
                _editor.SetTransition(ClipId, new Transition
                {
                    Type = transition.Type,
                    DurationFrames = Math.Max(1, Rate.SecondsToFrames(Math.Clamp(value, 0.04, 10))),
                });
            }
        }
    }

    public bool HasTransition => Clip.TransitionIn is not null;

    // ------------------------------------------------------------ Mehrspur (Sprint 10)

    /// <summary>Clip liegt auf einer oberen Videospur (V2 …): Bild-in-Bild, Mischmodus.</summary>
    public bool IsOverlayClip => Track is { Kind: TrackKind.Video } t && _editor.Project.VideoTracks.First().Id != t.Id;

    public static IReadOnlyList<LabeledValue<BlendMode>> BlendModes { get; } =
        Enum.GetValues<BlendMode>().Select(m => new LabeledValue<BlendMode>(m, BlendModeInfo.Label(m))).ToList();

    public LabeledValue<BlendMode> Blend
    {
        get => BlendModes.First(b => b.Value == Clip.Transform.Blend);
        set => _editor.UpdateClip(ClipId, "Mischmodus", c => c.Transform.Blend = value.Value);
    }

    public double CropLeft
    {
        get => Clip.Transform.CropLeft;
        set => _editor.UpdateClip(ClipId, "Zuschneiden", c => c.Transform.CropLeft = Math.Clamp(value, 0, 45), mergeSimilar: true);
    }

    public double CropRight
    {
        get => Clip.Transform.CropRight;
        set => _editor.UpdateClip(ClipId, "Zuschneiden", c => c.Transform.CropRight = Math.Clamp(value, 0, 45), mergeSimilar: true);
    }

    public double CropTop
    {
        get => Clip.Transform.CropTop;
        set => _editor.UpdateClip(ClipId, "Zuschneiden", c => c.Transform.CropTop = Math.Clamp(value, 0, 45), mergeSimilar: true);
    }

    public double CropBottom
    {
        get => Clip.Transform.CropBottom;
        set => _editor.UpdateClip(ClipId, "Zuschneiden", c => c.Transform.CropBottom = Math.Clamp(value, 0, 45), mergeSimilar: true);
    }

    public bool FlipHorizontal
    {
        get => Clip.Transform.FlipHorizontal;
        set => _editor.UpdateClip(ClipId, "Spiegeln", c => c.Transform.FlipHorizontal = value);
    }

    public bool FlipVertical
    {
        get => Clip.Transform.FlipVertical;
        set => _editor.UpdateClip(ClipId, "Spiegeln", c => c.Transform.FlipVertical = value);
    }

    /// <summary>Schnellvorlagen für Bild-in-Bild.</summary>
    [RelayCommand]
    private void PipPreset(string? corner)
    {
        var (x, y) = corner switch
        {
            "TL" => (-32.0, -32.0),
            "TR" => (32.0, -32.0),
            "BL" => (-32.0, 32.0),
            _ => (32.0, 32.0),
        };
        _editor.UpdateClip(ClipId, "Bild-in-Bild", c =>
        {
            c.Keyframes.RemoveAll(k => k.Property is AnimatedProperty.Scale or AnimatedProperty.PositionX or AnimatedProperty.PositionY);
            c.Transform.Scale = 30;
            c.Transform.PositionX = x;
            c.Transform.PositionY = y;
        });
    }

    // ------------------------------------------------------------ Multicam

    public bool IsMulticam => Clip.IsMulticam;

    public IReadOnlyList<LabeledValue<int>> Angles =>
        Clip.MulticamAngles?.Select((a, i) => new LabeledValue<int>(i, $"{i + 1}: {a.Name}")).ToList() ?? [];

    public LabeledValue<int>? ActiveAngle
    {
        get => Angles.FirstOrDefault(a => a.Value == Clip.ActiveAngle);
        set
        {
            if (value is not null)
            {
                _editor.UpdateClip(ClipId, "Kamera", c => c.ActiveAngle = value.Value);
            }
        }
    }

    public LabeledValue<int>? AudioAngle
    {
        get => Angles.FirstOrDefault(a => a.Value == Clip.AudioAngle);
        set
        {
            if (value is not null)
            {
                _editor.UpdateClip(ClipId, "Ton-Kamera", c => c.AudioAngle = value.Value);
            }
        }
    }

    /// <summary>Bewegung: Zoom, Position, Drehung, Deckkraft – je mit Keyframes.</summary>
    public IReadOnlyList<AnimatedValueViewModel> Motion { get; }

    /// <summary>Lautstärke mit Keyframes (Lautstärke-Hüllkurve).</summary>
    public AnimatedValueViewModel VolumeKeys { get; }

    public bool IsNested => Clip.IsNested;

    public string NestedText => Clip.NestedTracks is { } tracks
        ? $"Verschachtelter Clip mit {tracks.Count} Spur(en) und {tracks.Sum(t => t.Clips.Count)} Clips."
        : string.Empty;

    // ------------------------------------------------------------ Geschwindigkeitsrampe

    public IReadOnlyList<string> RampShapes { get; } = ["Normal → Zeitlupe", "Zeitlupe → Normal", "Normal → Zeitlupe → Normal"];

    public IReadOnlyList<string> RampSpeeds { get; } = ["25 %", "40 %", "50 %"];

    [ObservableProperty]
    private int _rampShape;

    [ObservableProperty]
    private int _rampSpeed;

    [RelayCommand]
    private void ApplyRamp()
    {
        var slow = RampSpeed switch { 1 => 0.4, 2 => 0.5, _ => 0.25 };
        if (!_editor.ApplySpeedRamp(ClipId, (SpeedRampShape)RampShape, slow))
        {
            LastError = "Rampe nicht möglich: Der Clip muss mit normaler Geschwindigkeit laufen (und darf nicht verschachtelt sein).";
        }
    }

    public Guid ClipId { get; }

    public bool Exists => _editor.Project.FindClip(ClipId) is not null;

    private TimelineClip Clip => _editor.Project.FindClip(ClipId)?.Clip ?? new TimelineClip();

    private TimelineTrack? Track => _editor.Project.FindClip(ClipId)?.Track;

    private FrameRate Rate => _editor.Project.Settings.FrameRate;

    private bool DropFrame => _editor.Project.Settings.DropFrameTimecode;

    public MediaItem? Media => _editor.Project.FindMedia(Clip.MediaId);

    public string Name => Clip.Name;

    public string TrackName => Track?.Name ?? string.Empty;

    public bool IsVideoClip => Track?.Kind == TrackKind.Video;

    public string StartText => Timecode.FromFrames(Clip.StartFrame, Rate, DropFrame);

    public string EndText => Timecode.FromFrames(Clip.EndFrame, Rate, DropFrame);

    public string LengthText => Timecode.FromFrames(Clip.LengthFrames, Rate, DropFrame);

    public string SourceText =>
        $"{Timecode.FormatDuration(TimeSpan.FromSeconds(Clip.SourceIn))} – {Timecode.FormatDuration(TimeSpan.FromSeconds(Clip.SourceOut(Rate)))}";

    public bool HasAudio => Clip.HasAudio;

    public IReadOnlyList<SpeedChoice> SpeedChoices { get; } =
    [
        new(0.1, "10 % (Zeitlupe)"),
        new(0.2, "20 % (Zeitlupe)"),
        new(0.25, "25 % (Zeitlupe)"),
        new(0.5, "50 % (Zeitlupe)"),
        new(1, "100 % (normal)"),
        new(2, "200 % (Zeitraffer)"),
        new(4, "400 % (Zeitraffer)"),
        new(8, "800 % (Zeitraffer)"),
    ];

    public SpeedChoice? SelectedSpeed
    {
        get => SpeedChoices.FirstOrDefault(s => Math.Abs(s.Speed - Clip.Speed) < 1e-6)
            ?? new SpeedChoice(Clip.Speed, $"{Clip.Speed * 100:0.#} %");
        set
        {
            if (value is not null && Math.Abs(value.Speed - Clip.Speed) > 1e-6 && !_editor.SetSpeed(ClipId, value.Speed))
            {
                LastError = "Dahinter ist nicht genug Platz für die neue Länge.";
            }
        }
    }

    /// <summary>Quelle mit hoher Bildrate (Zeitlupen-Material der R6 Mark II).</summary>
    public bool CanConform => Media?.Detection.IsHighFrameRate == true;

    public string ConformText => Media?.First.Video is { } v
        ? $"Als Zeitlupe abspielen ({v.FrameRate} fps → {Rate} fps)"
        : string.Empty;

    [RelayCommand]
    private void Conform()
    {
        if (Media?.First.Video is { } video && !_editor.ConformToProjectRate(ClipId, video.FrameRate.Value))
        {
            LastError = "Dahinter ist nicht genug Platz für die Zeitlupe.";
        }
    }

    public double VolumeDb
    {
        get => Clip.VolumeDb;
        set => _editor.UpdateClip(ClipId, "Lautstärke", c => c.VolumeDb = Math.Round(value, 1), mergeSimilar: true);
    }

    public bool AudioMuted
    {
        get => Clip.AudioMuted;
        set => _editor.UpdateClip(ClipId, value ? "Ton aus" : "Ton an", c => c.AudioMuted = value);
    }

    public double FadeInSeconds
    {
        get => Math.Round(Rate.FramesToSeconds(Clip.FadeInFrames), 2);
        set => _editor.UpdateClip(ClipId, "Einblenden", c => c.FadeInFrames = Rate.SecondsToFrames(Math.Max(0, value)), mergeSimilar: true);
    }

    public double FadeOutSeconds
    {
        get => Math.Round(Rate.FramesToSeconds(Clip.FadeOutFrames), 2);
        set => _editor.UpdateClip(ClipId, "Ausblenden", c => c.FadeOutFrames = Rate.SecondsToFrames(Math.Max(0, value)), mergeSimilar: true);
    }

    [ObservableProperty]
    private string? _lastError;

    public void Refresh()
    {
        OnPropertyChanged(string.Empty);
        Title?.Refresh();
        Color.Refresh();
        RefreshMotion();
    }

    /// <summary>Werte am Abspielkopf neu anzeigen (Keyframes).</summary>
    public void RefreshMotion()
    {
        foreach (var value in Motion)
        {
            value.Refresh();
        }

        VolumeKeys.Refresh();
    }
}
