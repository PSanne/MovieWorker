using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MovieWorker.App.ViewModels;
using MovieWorker.Core.Effects;

namespace MovieWorker.App.Controls;

/// <summary>
/// Knoten-Editor für die Effektkette eines Clips: Quelle → (Farbe) → Effekte … → Ausgabe.
/// Klick wählt einen Effekt, Ziehen ändert die Reihenfolge, Doppelklick schaltet ihn an oder aus.
/// </summary>
public sealed class EffectNodeEditor : FrameworkElement
{
    private const double NodeHeight = 40;
    private const double Gap = 18;
    private const double Pad = 6;

    private static readonly Brush NodeBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF4, 0xF2, 0xFD)));
    private static readonly Brush FixedBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xEC, 0xEC, 0xEC)));
    private static readonly Brush ColorNodeBrush = Freeze(new LinearGradientBrush(Color.FromRgb(0xFF, 0xE0, 0xC0), Color.FromRgb(0xC8, 0xE0, 0xFF), 0));
    private static readonly Brush KeyStripe = Freeze(new SolidColorBrush(Color.FromRgb(0x00, 0xB1, 0x40)));
    private static readonly Brush FxStripe = Freeze(new SolidColorBrush(Color.FromRgb(0x66, 0x31, 0xD7)));
    private static readonly Brush TextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)));
    private static readonly Brush DimBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A)));
    private static readonly Pen BorderPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)), 1));
    private static readonly Pen SelectedPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x66, 0x31, 0xD7)), 2));
    private static readonly Pen DisabledPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)), 1) { DashStyle = DashStyles.Dash });
    private static readonly Pen LinkPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)), 1.5));
    private static readonly Pen InsertPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x66, 0x31, 0xD7)), 3));

    private EffectsViewModel? _vm;
    private Guid? _dragId;
    private Point _dragStart;
    private int _dropIndex = -1;

    public EffectNodeEditor()
    {
        DataContextChanged += (_, _) => Attach(DataContext as EffectsViewModel);
        Focusable = true;
        Cursor = Cursors.Hand;
    }

    /// <summary>Ein anderer Effekt wurde gewählt.</summary>
    public event EventHandler? SelectionChanged;

    private void Attach(EffectsViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.ChainChanged -= OnChainChanged;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        _vm = vm;
        if (_vm is not null)
        {
            _vm.ChainChanged += OnChainChanged;
            _vm.PropertyChanged += OnVmPropertyChanged;
        }

        OnChainChanged(this, EventArgs.Empty);
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EffectsViewModel.SelectedEffectId) or "" or null)
        {
            InvalidateVisual();
        }
    }

    private void OnChainChanged(object? sender, EventArgs e)
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    private IReadOnlyList<(string Title, string Detail, VideoEffect? Effect, Brush Fill)> Nodes()
    {
        var nodes = new List<(string, string, VideoEffect?, Brush)> { ("Quelle", "Kamerabild", null, FixedBrush) };
        if (_vm is null)
        {
            return nodes;
        }

        if (_vm.HasColor)
        {
            nodes.Add(("Farbe", "Korrektur aus dem Reiter Farbe", null, ColorNodeBrush));
        }

        nodes.AddRange(_vm.Chain.Select(e => (e.Name, e.Summary, (VideoEffect?)e, NodeBrush)));
        nodes.Add(("Ausgabe", _vm.Chain.Any(e => e.Enabled && e.IsKey) ? "mit Transparenz (Key)" : "Bild", null, FixedBrush));
        return nodes;
    }

    private int FirstEffectIndex => _vm?.HasColor == true ? 2 : 1;

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = Nodes().Count;
        var width = double.IsInfinity(availableSize.Width) ? 260 : availableSize.Width;
        return new Size(width, Pad * 2 + count * NodeHeight + (count - 1) * Gap);
    }

    private Rect NodeRect(int index) =>
        new(Pad + 14, Pad + index * (NodeHeight + Gap), Math.Max(60, ActualWidth - 2 * Pad - 28), NodeHeight);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.White, null, new Rect(RenderSize));
        var nodes = Nodes();
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (var i = 0; i < nodes.Count; i++)
        {
            var rect = NodeRect(i);
            if (i > 0)
            {
                // Verbindung mit Pfeil
                var from = new Point(rect.Left + rect.Width / 2, rect.Top - Gap);
                var to = new Point(from.X, rect.Top - 2);
                dc.DrawLine(LinkPen, from, to);
                var arrow = new StreamGeometry();
                using (var g = arrow.Open())
                {
                    g.BeginFigure(new Point(to.X - 4, to.Y - 5), true, true);
                    g.LineTo(new Point(to.X + 4, to.Y - 5), true, false);
                    g.LineTo(to, true, false);
                }

                arrow.Freeze();
                dc.DrawGeometry(LinkPen.Brush, null, arrow);
            }

            var (title, detail, effect, fill) = nodes[i];
            var selected = effect is not null && effect.Id == _vm?.SelectedEffectId;
            var pen = selected ? SelectedPen : effect is { Enabled: false } ? DisabledPen : BorderPen;
            dc.DrawRoundedRectangle(fill, pen, rect, 6, 6);
            if (effect is not null)
            {
                dc.DrawRoundedRectangle(effect.IsKey ? KeyStripe : FxStripe, null, new Rect(rect.Left, rect.Top, 5, rect.Height), 2, 2);
            }

            var textBrush = effect is { Enabled: false } ? DimBrush : TextBrush;
            var number = effect is null ? string.Empty : $"{i - FirstEffectIndex + 1}. ";
            var head = new FormattedText(number + title + (effect is { Enabled: false } ? "  (aus)" : string.Empty), CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 12.5, textBrush, dpi)
            {
                MaxTextWidth = Math.Max(10, rect.Width - 18), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis,
            };
            dc.DrawText(head, new Point(rect.Left + 10, rect.Top + 4));
            var sub = new FormattedText(detail, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10.5, DimBrush, dpi)
            {
                MaxTextWidth = Math.Max(10, rect.Width - 18), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis,
            };
            dc.DrawText(sub, new Point(rect.Left + 10, rect.Top + 21));
        }

        if (_dragId is not null && _dropIndex >= 0)
        {
            var y = Pad + (_dropIndex + FirstEffectIndex) * (NodeHeight + Gap) - Gap / 2;
            dc.DrawLine(InsertPen, new Point(Pad, y), new Point(ActualWidth - Pad, y));
        }
    }

    private int HitNode(Point p)
    {
        var nodes = Nodes();
        for (var i = 0; i < nodes.Count; i++)
        {
            if (NodeRect(i).Contains(p))
            {
                return i;
            }
        }

        return -1;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (_vm is null)
        {
            return;
        }

        var index = HitNode(e.GetPosition(this));
        if (index < 0 || Nodes()[index].Effect is not { } effect)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            _vm.ToggleEnabled(effect.Id);
            e.Handled = true;
            return;
        }

        _vm.SelectedEffectId = effect.Id;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        _dragId = effect.Id;
        _dragStart = e.GetPosition(this);
        _dropIndex = -1;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragId is null || _vm is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var p = e.GetPosition(this);
        if (Math.Abs(p.Y - _dragStart.Y) < 6)
        {
            return;
        }

        var slot = (int)Math.Round((p.Y - Pad + Gap / 2) / (NodeHeight + Gap)) - FirstEffectIndex;
        _dropIndex = Math.Clamp(slot, 0, _vm.Chain.Count);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragId is { } id && _vm is not null && _dropIndex >= 0)
        {
            var current = _vm.Chain.ToList().FindIndex(x => x.Id == id);
            var target = _dropIndex > current ? _dropIndex - 1 : _dropIndex;
            if (current >= 0 && target != current)
            {
                _vm.MoveTo(id, target);
            }
        }

        _dragId = null;
        _dropIndex = -1;
        ReleaseMouseCapture();
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_vm?.Selected is not { } selected)
        {
            return;
        }

        var index = _vm.Chain.ToList().FindIndex(x => x.Id == selected.Id);
        switch (e.Key)
        {
            case Key.Up when Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) || Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                _vm.MoveTo(selected.Id, index - 1);
                e.Handled = true;
                break;
            case Key.Down when Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) || Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                _vm.MoveTo(selected.Id, index + 1);
                e.Handled = true;
                break;
            case Key.Up when index > 0:
                _vm.SelectedEffectId = _vm.Chain[index - 1].Id;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case Key.Down when index < _vm.Chain.Count - 1:
                _vm.SelectedEffectId = _vm.Chain[index + 1].Id;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
        }
    }

    private static T Freeze<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
