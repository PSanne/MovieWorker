using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using MovieWorker.Core.Color;
using MovieWorker.Core.Timeline;

namespace MovieWorker.App.ViewModels;

/// <summary>Ein Farbrad im Inspektor (Lift, Gamma oder Gain).</summary>
public sealed class WheelViewModel(TimelineEditor editor, Guid clipId, string label, Func<TimelineClip, ColorWheel> select) : ObservableObject
{
    public string Label { get; } = label;

    private ColorWheel Wheel => editor.Project.FindClip(clipId) is (_, { } clip) ? select(clip) : new ColorWheel();

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
        set => editor.UpdateClip(clipId, Label, c => select(c).Master = Math.Clamp(value, -1, 1), mergeSimilar: true);
    }

    private void SetPoint(double x, double y)
    {
        var amount = Math.Min(1, Math.Sqrt(x * x + y * y));
        var hue = Math.Atan2(-y, x) * 180 / Math.PI;
        editor.UpdateClip(clipId, Label, c => select(c).SetFromPolar(hue, amount * 0.5), mergeSimilar: true);
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
        Lift = new WheelViewModel(editor, clipId, "Lift (Schatten)", c => GradeOf(c).Lift);
        Gamma = new WheelViewModel(editor, clipId, "Gamma (Mitten)", c => GradeOf(c).Gamma);
        Gain = new WheelViewModel(editor, clipId, "Gain (Lichter)", c => GradeOf(c).Gain);
    }

    // ------------------------------------------------------------ Korrekturknoten (Sprint 12)

    /// <summary>Bearbeiteter Knoten: −1 = Grundkorrektur, sonst Index in den weiteren Knoten.</summary>
    [ObservableProperty]
    private int _selectedNodeIndex = -1;

    partial void OnSelectedNodeIndexChanged(int value) => Refresh();

    private ColorGrade GradeOf(TimelineClip clip) =>
        SelectedNodeIndex >= 0 && SelectedNodeIndex < clip.Nodes.Count ? clip.Nodes[SelectedNodeIndex].Grade : clip.Grade;

    public IReadOnlyList<LabeledValue<int>> NodeChoices =>
        new[] { new LabeledValue<int>(-1, "1 · Grundkorrektur") }
            .Concat(Clip.Nodes.Select((n, i) => new LabeledValue<int>(i, $"{i + 2} · {n.Name}{(n.Mask is null ? string.Empty : " (Maske)")}{(n.Enabled ? string.Empty : " – aus")}")))
            .ToList();

    public LabeledValue<int> SelectedNode
    {
        get => NodeChoices.FirstOrDefault(n => n.Value == SelectedNodeIndex) ?? NodeChoices[0];
        set
        {
            if (value is not null)
            {
                SelectedNodeIndex = value.Value;
            }
        }
    }

    public bool IsNodeSelected => SelectedNodeIndex >= 0 && SelectedNodeIndex < Clip.Nodes.Count;

    private CorrectionNode? Node => IsNodeSelected ? Clip.Nodes[SelectedNodeIndex] : null;

    public void AddNode(string name, ColorMask? mask)
    {
        var index = Clip.Nodes.Count;
        _editor.UpdateClip(ClipId, "Knoten hinzufügen", c => c.Nodes.Add(new CorrectionNode { Name = name, Mask = mask }));
        SelectedNodeIndex = index;
    }

    public void RemoveSelectedNode()
    {
        if (!IsNodeSelected)
        {
            return;
        }

        var index = SelectedNodeIndex;
        _editor.UpdateClip(ClipId, "Knoten löschen", c => c.Nodes.RemoveAt(index));
        SelectedNodeIndex = -1;
    }

    public bool NodeEnabled
    {
        get => Node?.Enabled ?? true;
        set
        {
            var index = SelectedNodeIndex;
            if (index >= 0)
            {
                _editor.UpdateClip(ClipId, "Knoten ein/aus", c => c.Nodes[index].Enabled = value);
            }
        }
    }

    // ------------------------------------------------------------ Kurven

    public static IReadOnlyList<LabeledValue<CurveChannel>> CurveChannels { get; } =
    [
        new(CurveChannel.Luma, "Helligkeit"),
        new(CurveChannel.Red, "Rot"),
        new(CurveChannel.Green, "Grün"),
        new(CurveChannel.Blue, "Blau"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurvePoints), nameof(CurveBrush))]
    private LabeledValue<CurveChannel> _selectedCurveChannel = CurveChannels[0];

    public System.Windows.Media.Brush CurveBrush => SelectedCurveChannel.Value switch
    {
        CurveChannel.Red => System.Windows.Media.Brushes.IndianRed,
        CurveChannel.Green => System.Windows.Media.Brushes.LightGreen,
        CurveChannel.Blue => System.Windows.Media.Brushes.CornflowerBlue,
        _ => System.Windows.Media.Brushes.White,
    };

    public List<CurvePoint> CurvePoints
    {
        get => GradeOf(Clip).Curves.Points(SelectedCurveChannel.Value).Select(p => new CurvePoint { X = p.X, Y = p.Y }).ToList();
        set
        {
            var channel = SelectedCurveChannel.Value;
            var points = value.Select(p => new CurvePoint { X = p.X, Y = p.Y }).ToList();
            Change("Kurve", g =>
            {
                var list = g.Curves.Points(channel);
                list.Clear();
                list.AddRange(points);
            });
        }
    }

    public void ResetCurves() => Change("Kurven zurücksetzen", g => g.Curves = new ColorCurves(), merge: false);

    // ------------------------------------------------------------ Sekundäre Korrektur

    private SecondaryCorrection Secondary => GradeOf(Clip).Secondary;

    public bool SecondaryEnabled
    {
        get => Secondary.Enabled;
        set => Change("Sekundäre Korrektur", g => g.Secondary.Enabled = value, merge: false);
    }

    public double HueCenter { get => Secondary.HueCenter; set => Change("Farbbereich", g => g.Secondary.HueCenter = Math.Round(value)); }

    public double HueWidth { get => Secondary.HueWidth; set => Change("Farbbereich", g => g.Secondary.HueWidth = Math.Round(value)); }

    public double SaturationMin { get => Secondary.SaturationMin; set => Change("Farbbereich", g => g.Secondary.SaturationMin = Math.Round(value, 2)); }

    public double LumaMin { get => Secondary.LumaMin; set => Change("Farbbereich", g => g.Secondary.LumaMin = Math.Round(value, 2)); }

    public double LumaMax { get => Secondary.LumaMax; set => Change("Farbbereich", g => g.Secondary.LumaMax = Math.Round(value, 2)); }

    public double Softness { get => Secondary.Softness; set => Change("Farbbereich", g => g.Secondary.Softness = Math.Round(value, 2)); }

    public double SecondaryHueShift { get => Secondary.HueShift; set => Change("Farbton verschieben", g => g.Secondary.HueShift = Math.Round(value)); }

    public double SecondarySaturation { get => Secondary.Saturation; set => Change("Sättigung im Bereich", g => g.Secondary.Saturation = Math.Round(value)); }

    public double SecondaryBrightness { get => Secondary.Brightness; set => Change("Helligkeit im Bereich", g => g.Secondary.Brightness = Math.Round(value, 2)); }

    public bool ShowSelection { get => Secondary.ShowSelection; set => Change("Auswahl zeigen", g => g.Secondary.ShowSelection = value, merge: false); }

    // ------------------------------------------------------------ Maske

    public static IReadOnlyList<LabeledValue<MaskShape?>> MaskShapes { get; } =
    [
        new(null, "Keine (ganzes Bild)"),
        new(Core.Color.MaskShape.Ellipse, "Ellipse"),
        new(Core.Color.MaskShape.Rectangle, "Rechteck"),
    ];

    public ColorMask? Mask => Node?.Mask;

    public bool HasMask => Mask is not null;

    public LabeledValue<MaskShape?> SelectedMaskShape
    {
        get => MaskShapes.First(m => m.Value == Mask?.Shape);
        set
        {
            var index = SelectedNodeIndex;
            if (index < 0 || value is null)
            {
                return;
            }

            _editor.UpdateClip(ClipId, "Maske", c =>
            {
                var node = c.Nodes[index];
                if (value.Value is null)
                {
                    node.Mask = null;
                }
                else
                {
                    node.Mask ??= new ColorMask();
                    node.Mask.Shape = value.Value.Value;
                }
            });
        }
    }

    private void ChangeMask(string description, Action<ColorMask> change)
    {
        var index = SelectedNodeIndex;
        if (index >= 0)
        {
            _editor.UpdateClip(ClipId, description, c =>
            {
                if (c.Nodes[index].Mask is { } mask)
                {
                    change(mask);
                }
            }, mergeSimilar: true);
        }
    }

    public double MaskCenterX { get => Mask?.CenterX ?? 0.5; set => ChangeMask("Maske verschieben", m => m.CenterX = Math.Round(value, 3)); }

    public double MaskCenterY { get => Mask?.CenterY ?? 0.5; set => ChangeMask("Maske verschieben", m => m.CenterY = Math.Round(value, 3)); }

    public double MaskWidth { get => Mask?.Width ?? 0.4; set => ChangeMask("Maskengröße", m => m.Width = Math.Round(value, 3)); }

    public double MaskHeight { get => Mask?.Height ?? 0.4; set => ChangeMask("Maskengröße", m => m.Height = Math.Round(value, 3)); }

    public double MaskFeather { get => Mask?.Feather ?? 0.3; set => ChangeMask("Weiche Kante", m => m.Feather = Math.Round(value, 3)); }

    public bool MaskInvert { get => Mask?.Invert ?? false; set => ChangeMask("Maske umkehren", m => m.Invert = value); }

    public bool IsTracked => Mask?.TrackX is { Keys.Count: > 0 };

    public string TrackText => IsTracked ? $"Verfolgt ({Mask!.TrackX!.Keys.Count} Punkte)" : "Nicht verfolgt";

    public Guid ClipId { get; }

    private TimelineClip Clip => _editor.Project.FindClip(ClipId)?.Clip ?? new TimelineClip();

    private void Change(string description, Action<ColorGrade> change, bool merge = true)
    {
        var index = SelectedNodeIndex;
        _editor.UpdateClip(ClipId, description, c => change(index >= 0 && index < c.Nodes.Count ? c.Nodes[index].Grade : c.Grade), merge);
    }

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
        get => ToneMapChoices.First(t => t.Value == GradeOf(Clip).ToneMap);
        set => Change("Tone-Mapping", g => g.ToneMap = value.Value, merge: false);
    }

    public double Exposure
    {
        get => GradeOf(Clip).Exposure;
        set => Change("Belichtung", g => g.Exposure = Math.Round(Math.Clamp(value, -3, 3), 2));
    }

    public double Contrast
    {
        get => GradeOf(Clip).Contrast;
        set => Change("Kontrast", g => g.Contrast = Math.Round(Math.Clamp(value, -100, 100), 1));
    }

    public double Saturation
    {
        get => GradeOf(Clip).Saturation;
        set => Change("Sättigung", g => g.Saturation = Math.Round(Math.Clamp(value, 0, 200), 1));
    }

    public double Temperature
    {
        get => GradeOf(Clip).Temperature;
        set => Change("Farbtemperatur", g => g.Temperature = Math.Round(Math.Clamp(value, -100, 100), 1));
    }

    public double Tint
    {
        get => GradeOf(Clip).Tint;
        set => Change("Tönung", g => g.Tint = Math.Round(Math.Clamp(value, -100, 100), 1));
    }

    public bool IsMatched => !IsNodeSelected && (
        Math.Abs(Clip.Grade.MatchGainR - 1) > 1e-6 || Math.Abs(Clip.Grade.MatchGainG - 1) > 1e-6 || Math.Abs(Clip.Grade.MatchGainB - 1) > 1e-6
        || Math.Abs(Clip.Grade.MatchOffsetR) > 1e-6 || Math.Abs(Clip.Grade.MatchOffsetG) > 1e-6 || Math.Abs(Clip.Grade.MatchOffsetB) > 1e-6);

    public void Refresh()
    {
        OnPropertyChanged(string.Empty);
        Lift.Refresh();
        Gamma.Refresh();
        Gain.Refresh();
    }
}
