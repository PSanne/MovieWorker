using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MovieWorker.Core.Color;
using MovieWorker.Core.Effects;
using MovieWorker.Core.Timeline;

namespace MovieWorker.App.ViewModels;

/// <summary>Reiter „Effekte“: Effektkette des Clips (Knoten-Editor) und Einstellungen des gewählten Effekts.</summary>
public sealed partial class EffectsViewModel(TimelineEditor editor, Guid clipId) : ObservableObject
{
    public Guid ClipId { get; } = clipId;

    private TimelineClip? Clip => editor.Project.FindClip(ClipId)?.Clip;

    public IReadOnlyList<VideoEffect> Chain => Clip?.Effects ?? [];

    /// <summary>Steht vor den Effekten eine Farbkorrektur (für die Anzeige im Knoten-Editor)?</summary>
    public bool HasColor => Clip is { } c && (!c.Grade.IsNeutral || c.Nodes.Count > 0 || c.LutPath is not null);

    public static IReadOnlyList<LabeledValue<EffectKind>> Kinds { get; } =
        VideoEffect.All.Select(k => new LabeledValue<EffectKind>(k, VideoEffect.DisplayName(k))).ToList();

    [ObservableProperty]
    private LabeledValue<EffectKind> _kindToAdd = Kinds[0];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Selected), nameof(HasSelection), nameof(IsKey), nameof(IsChromaKey), nameof(IsLumaKey), nameof(HasMask))]
    private Guid? _selectedEffectId;

    public VideoEffect? Selected => Chain.FirstOrDefault(e => e.Id == SelectedEffectId);

    public bool HasSelection => Selected is not null;

    public bool IsKey => Selected?.IsKey == true;

    public bool IsChromaKey => Selected?.Kind == EffectKind.ChromaKey;

    public bool IsLumaKey => Selected?.Kind == EffectKind.LumaKey;

    public string SelectedTitle => Selected is { } e ? $"{Chain.ToList().IndexOf(e) + 1}. {e.Name}" : string.Empty;

    [RelayCommand]
    private void Add()
    {
        if (editor.AddEffect(ClipId, KindToAdd.Value) is { } id)
        {
            SelectedEffectId = id;
            Refresh();
        }
    }

    /// <summary>Effekt direkt hinzufügen (Menü, Tests).</summary>
    public Guid? AddKind(EffectKind kind)
    {
        KindToAdd = Kinds.First(k => k.Value == kind);
        Add();
        return SelectedEffectId;
    }

    [RelayCommand]
    private void Remove()
    {
        if (Selected is { } e && editor.RemoveEffect(ClipId, e.Id))
        {
            SelectedEffectId = Chain.LastOrDefault()?.Id;
            Refresh();
        }
    }

    [RelayCommand]
    private void MoveUp() => Move(-1);

    [RelayCommand]
    private void MoveDown() => Move(1);

    private void Move(int delta)
    {
        if (Selected is { } e)
        {
            MoveTo(e.Id, Chain.ToList().IndexOf(e) + delta);
        }
    }

    /// <summary>Verschieben per Ziehen im Knoten-Editor.</summary>
    public void MoveTo(Guid effectId, int index)
    {
        if (editor.MoveEffect(ClipId, effectId, index))
        {
            SelectedEffectId = effectId;
            Refresh();
        }
    }

    public void ToggleEnabled(Guid effectId)
    {
        editor.UpdateEffect(ClipId, effectId, "Effekt an/aus", e => e.Enabled = !e.Enabled);
        Refresh();
    }

    private void Change(string description, Action<VideoEffect> change)
    {
        if (SelectedEffectId is { } id)
        {
            editor.UpdateEffect(ClipId, id, description, change);
        }
    }

    public bool Enabled
    {
        get => Selected?.Enabled ?? false;
        set => Change("Effekt an/aus", e => e.Enabled = value);
    }

    public double Amount
    {
        get => Selected?.Amount ?? 0;
        set => Change("Effektstärke", e => e.Amount = Math.Round(Math.Clamp(value, 0, 1), 3));
    }

    public string AmountLabel => IsKey ? "Toleranz" : "Stärke";

    public double Softness
    {
        get => Selected?.Softness ?? 0;
        set => Change("Weiche Kante", e => e.Softness = Math.Round(Math.Clamp(value, 0, 1), 3));
    }

    public double Spill
    {
        get => Selected?.Spill ?? 0;
        set => Change("Spill-Unterdrückung", e => e.Spill = Math.Round(Math.Clamp(value, 0, 1), 3));
    }

    public string KeyColor
    {
        get => Selected?.KeyColor ?? VideoEffect.GreenScreen;
        set => Change("Key-Farbe", e => e.KeyColor = VideoEffect.NormalizeColor(value));
    }

    public bool KeyDark
    {
        get => Selected?.KeyDark ?? true;
        set => Change("Luma-Key", e => e.KeyDark = value);
    }

    [RelayCommand]
    private void SetKeyColor(string? color) => KeyColor = color ?? VideoEffect.GreenScreen;

    // ---------- Maske ----------
    public ColorMask? Mask => Selected?.Mask;

    public bool HasMask
    {
        get => Mask is not null;
        set => Change(value ? "Effektmaske" : "Effektmaske entfernen", e => e.Mask = value ? e.Mask ?? new ColorMask { Width = 0.3, Height = 0.4 } : null);
    }

    public LabeledValue<MaskShape?> MaskShape
    {
        get => ColorViewModel.MaskShapes.First(m => m.Value == (Mask?.Shape ?? Core.Color.MaskShape.Ellipse));
        set => ChangeMask("Maskenform", m => m.Shape = value.Value ?? Core.Color.MaskShape.Ellipse);
    }

    private void ChangeMask(string description, Action<ColorMask> change) =>
        Change(description, e =>
        {
            if (e.Mask is { } mask)
            {
                change(mask);
            }
        });

    public double MaskCenterX { get => Mask?.CenterX ?? 0.5; set => ChangeMask("Maske verschieben", m => m.CenterX = Math.Round(value, 3)); }

    public double MaskCenterY { get => Mask?.CenterY ?? 0.5; set => ChangeMask("Maske verschieben", m => m.CenterY = Math.Round(value, 3)); }

    public double MaskWidth { get => Mask?.Width ?? 0.3; set => ChangeMask("Maskengröße", m => m.Width = Math.Round(value, 3)); }

    public double MaskHeight { get => Mask?.Height ?? 0.4; set => ChangeMask("Maskengröße", m => m.Height = Math.Round(value, 3)); }

    public double MaskFeather { get => Mask?.Feather ?? 0.3; set => ChangeMask("Weiche Kante", m => m.Feather = Math.Round(value, 3)); }

    public bool MaskInvert { get => Mask?.Invert ?? false; set => ChangeMask("Maske umkehren", m => m.Invert = value); }

    public bool IsTracked => Mask?.TrackX is { Keys.Count: > 0 };

    public string TrackText => IsTracked ? $"Verfolgt ({Mask!.TrackX!.Keys.Count} Punkte)" : "Nicht verfolgt";

    [RelayCommand]
    private void ClearTracking() => ChangeMask("Verfolgung löschen", m =>
    {
        m.TrackX = null;
        m.TrackY = null;
    });

    public string MaskCheckText => IsKey ? "Nur innerhalb der Maske sichtbar (Garbage Matte)" : "Nur innerhalb einer Maske wirken";

    public string MaskHint => IsKey
        ? "Bei Keys gilt die Maske als „Garbage Matte“: Nur was in der Maske liegt, bleibt sichtbar (z. B. Stative und Lampen am Rand weg)."
        : "Der Effekt wirkt nur in der Maske – z. B. ein Gesicht oder Kennzeichen verpixeln. „Verfolgen ▶“ lässt die Maske dem Objekt folgen.";

    public void Refresh()
    {
        if (SelectedEffectId is { } id && Chain.All(e => e.Id != id))
        {
            SelectedEffectId = null;
        }

        SelectedEffectId ??= Chain.FirstOrDefault()?.Id;
        OnPropertyChanged(string.Empty);
        ChainChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Die Kette hat sich geändert (Knoten-Editor neu zeichnen).</summary>
    public event EventHandler? ChainChanged;
}
