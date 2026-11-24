using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using MovieWorker.Core.Camera;
using MovieWorker.Core.Media;
using MovieWorker.Core.Projects;
using MovieWorker.Core.Time;

namespace MovieWorker.App.ViewModels;

/// <summary>Auswahlmöglichkeit für die Kamera eines Clips (Automatisch oder fest).</summary>
public sealed record CameraChoice(CanonCamera? Camera, string Label)
{
    public static IReadOnlyList<CameraChoice> All { get; } =
    [
        new(null, "Automatisch erkennen"),
        new(CanonCamera.EosR6MarkII, "Canon EOS R6 Mark II"),
        new(CanonCamera.Eos70D, "Canon EOS 70D"),
        new(CanonCamera.Unknown, "Andere Kamera"),
    ];

    public override string ToString() => Label;
}

/// <summary>Ein Eintrag im Medienpool, wie ihn die Oberfläche zeigt.</summary>
public sealed partial class MediaItemViewModel : ObservableObject
{
    private readonly Action _changed;

    public MediaItemViewModel(MediaItem model, Action changed)
    {
        Model = model;
        _changed = changed;
    }

    public MediaItem Model { get; }

    public string Name => Model.Name;

    public string Summary => Model.First.Summary;

    public string DurationText => Timecode.FormatDuration(Model.Duration);

    public bool IsAudioOnly => Model.IsAudioOnly;

    public string KindIcon => Model.IsAudioOnly ? "♪" : "▶";

    public CameraDetection Detection => Model.Detection;

    public string CameraLabel => Detection.Camera == CanonCamera.Unknown
        ? (Model.IsAudioOnly ? "Audio" : "Andere Kamera")
        : Detection.CameraName + (Detection.Confidence == DetectionConfidence.Likely ? " (erkannt)" : string.Empty);

    public string TransferLabel => Detection.Transfer switch
    {
        ColorTransfer.CanonLog3 => "Canon Log 3",
        ColorTransfer.HdrPq => "HDR PQ",
        ColorTransfer.HdrHlg => "HDR HLG",
        _ => Model.IsAudioOnly ? string.Empty : "Rec.709",
    };

    public bool HasSpecialTransfer => Detection.Transfer != ColorTransfer.Rec709;

    public bool IsHighFrameRate => Detection.IsHighFrameRate;

    public IReadOnlyList<string> Hints => Detection.Hints;

    public string HintsText => string.Join(Environment.NewLine, Hints.Select(h => "• " + h));

    public bool HasHints => Hints.Count > 0;

    public string PartsText => Model.Parts.Count > 1
        ? $"{Model.Parts.Count} Dateien (an der 4-GB-Grenze geteilt): " + string.Join(", ", Model.Parts.Select(p => p.FileName))
        : Model.First.FileName;

    public string FolderText => System.IO.Path.GetDirectoryName(Model.PrimaryPath) ?? string.Empty;

    public string SizeText => FormatSize(Model.Parts.Sum(p => p.FileSize));

    public string CreatedText => Model.First.CreationTime is { } c ? c.LocalDateTime.ToString("g") : "unbekannt";

    public string ProxyText => Model.HasProxy ? "Proxy vorhanden" : "kein Proxy";

    public IReadOnlyList<string> CameraTips => CanonCameraProfile.For(Detection.Camera)?.EditingTips ?? [];

    public string CameraTipsText => string.Join(Environment.NewLine, CameraTips.Select(t => "• " + t));

    public IReadOnlyList<CameraChoice> CameraChoices => CameraChoice.All;

    public CameraChoice SelectedCamera
    {
        get => CameraChoice.All.First(c => c.Camera == Model.CameraOverride);
        set
        {
            if (value is null || value.Camera == Model.CameraOverride)
            {
                return;
            }

            Model.CameraOverride = value.Camera;
            RefreshAll();
            _changed();
        }
    }

    [ObservableProperty]
    private BitmapSource? _thumbnail;

    [ObservableProperty]
    private bool _isProxyRunning;

    [ObservableProperty]
    private double _proxyProgress;

    public void RefreshAll() => OnPropertyChanged(string.Empty);

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        _ => $"{bytes / 1024.0:0} KB",
    };
}
