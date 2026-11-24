using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using MovieWorker.Core.Color;
using MovieWorker.Core.Timeline;

namespace MovieWorker.App.ViewModels;

/// <summary>Ein Farbrad im Inspektor (Lift, Gamma oder Gain).</summary>
public sealed class WheelViewModel(TimelineEditor editor, Guid clipId, string label, Func<ColorGrade, ColorWheel> select) : ObservableObject
{
    public string Label { get; } = label;

    private ColorWheel Wheel => editor.Project.FindClip(clipId) is (_, { } clip) ? select(clip.Grade) : new ColorWheel();

    public double X
    {
        get
        {
            var (hue, amount) = Wheel.ToPolar();
            return Math.Round(amount * Math.Cos(hue * Math.PI / 180), 3);
        }
        set => SetPoint(value, Y);
    }

    public double Y
    {
        get
        {
            var (hue, amount) = Wheel.ToPolar();
            return Math.Round(-amount * Math.Sin(hue * Math.PI / 180), 3);
        }
        set => SetPoint(X, value);
    }

    public double Master
    {
        get => Wheel.Master;
        set => editor.UpdateClip(clipId, Label, c => select(c.Grade).Master = Math.Clamp(value, -1, 1), mergeSimilar: true);
    }

    private void SetPoint(double x, double y)
    {
        var amount = Math.Min(1, Math.Sqrt(x * x + y * y));
        var hue = Math.Atan2(-y, x) * 180 / Math.PI;
        editor.UpdateClip(clipId, Label, c => select(c.Grade).SetFromPolar(hue, amount * 0.5), mergeSimilar: true);
    }

    public void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>Reiter „Farbe“: Eingangs-Umrechnung, Grundkorrektur, Farbräder.</summary>
public sealed partial class ColorViewModel : ObservableObject
{
    private readonly TimelineEditor _editor;
    private readonly Func<IReadOnlyList<string>> _lutFiles;

    public ColorViewModel(TimelineEditor editor, Guid clipId, Func<IReadOnlyList<string>> lutFiles)
    {
        _editor = editor;
        ClipId = clipId;
        _lutFiles = lutFiles;
        Lift = new WheelViewModel(editor, clipId, "Lift (Schatten)", g => g.Lift);
        Gamma = new WheelViewModel(editor, clipId, "Gamma (Mitten)", g => g.Gamma);
        Gain = new WheelViewModel(editor, clipId, "Gain (Lichter)", g => g.Gain);
    }

    public Guid ClipId { get; }

    private TimelineClip Clip => _editor.Project.FindClip(ClipId)?.Clip ?? new TimelineClip();

    private void Change(string description, Action<ColorGrade> change, bool merge = true) =>
        _editor.UpdateClip(ClipId, description, c => change(c.Grade), merge);

    public WheelViewModel Lift { get; }

    public WheelViewModel Gamma { get; }

    public WheelViewModel Gain { get; }

    public IReadOnlyList<WheelViewModel> Wheels => [Lift, Gamma, Gain];

    public static IReadOnlyList<LabeledValue<ColorTransform>> TransformChoices { get; } =
    [
        new(ColorTransform.None, "Keine (Rec.709)"),
        new(ColorTransform.Lut, "LUT (z. B. Canon Log 3 → BT.709)"),
        new(ColorTransform.HdrToSdr, "HDR → SDR (Tone-Mapping)"),
    ];

    public static IReadOnlyList<LabeledValue<ToneMapping>> ToneMapChoices { get; } =
    [
        new(ToneMapping.Hable, "Filmisch (Hable)"),
        new(ToneMapping.Mobius, "Natürlich (Mobius)"),
        new(ToneMapping.Reinhard, "Flach (Reinhard)"),
        new(ToneMapping.Clip, "Hart abschneiden"),
    ];

    public LabeledValue<ColorTransform> Transform
    {
        get => TransformChoices.First(t => t.Value == Clip.ColorTransform);
        set => _editor.UpdateClip(ClipId, "Eingangs-Umrechnung", c => c.ColorTransform = value.Value);
    }

    public bool UsesLut => Clip.ColorTransform == ColorTransform.Lut;

    public bool UsesToneMap => Clip.ColorTransform == ColorTransform.HdrToSdr;

    public IReadOnlyList<LabeledValue<string>> Luts
    {
        get
        {
            var files = _lutFiles().ToList();
            if (Clip.LutPath is { } current && !files.Contains(current, StringComparer.OrdinalIgnoreCase))
            {
                files.Insert(0, current);
            }

            return files.Select(f => new LabeledValue<string>(f, Path.GetFileNameWithoutExtension(f))).ToList();
        }
    }

    public LabeledValue<string>? Lut
    {
        get => Luts.FirstOrDefault(l => string.Equals(l.Value, Clip.LutPath, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value is not null)
            {
                SetLut(value.Value);
            }
        }
    }

    public void SetLut(string path) =>
        _editor.UpdateClip(ClipId, "LUT", c =>
        {
            c.ColorTransform = ColorTransform.Lut;
            c.LutPath = path;
        });

    public LabeledValue<ToneMapping> ToneMap
    {
        get => ToneMapChoices.First(t => t.Value == Clip.Grade.ToneMap);
        set => Change("Tone-Mapping", g => g.ToneMap = value.Value, merge: false);
    }

    public double Exposure
    {
        get => Clip.Grade.Exposure;
        set => Change("Belichtung", g => g.Exposure = Math.Round(Math.Clamp(value, -3, 3), 2));
    }

    public double Contrast
    {
        get => Clip.Grade.Contrast;
        set => Change("Kontrast", g => g.Contrast = Math.Round(Math.Clamp(value, -100, 100), 1));
    }

    public double Saturation
    {
        get => Clip.Grade.Saturation;
        set => Change("Sättigung", g => g.Saturation = Math.Round(Math.Clamp(value, 0, 200), 1));
    }

    public double Temperature
    {
        get => Clip.Grade.Temperature;
        set => Change("Farbtemperatur", g => g.Temperature = Math.Round(Math.Clamp(value, -100, 100), 1));
    }

    public double Tint
    {
        get => Clip.Grade.Tint;
        set => Change("Tönung", g => g.Tint = Math.Round(Math.Clamp(value, -100, 100), 1));
    }

    public bool IsMatched =>
        Math.Abs(Clip.Grade.MatchGainR - 1) > 1e-6 || Math.Abs(Clip.Grade.MatchGainG - 1) > 1e-6 || Math.Abs(Clip.Grade.MatchGainB - 1) > 1e-6
        || Math.Abs(Clip.Grade.MatchOffsetR) > 1e-6 || Math.Abs(Clip.Grade.MatchOffsetG) > 1e-6 || Math.Abs(Clip.Grade.MatchOffsetB) > 1e-6;

    public void Refresh()
    {
        OnPropertyChanged(string.Empty);
        Lift.Refresh();
        Gamma.Refresh();
        Gain.Refresh();
    }
}
