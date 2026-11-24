using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace MovieWorker.App.Controls;

/// <summary>
/// Farbrad wie in Profi-Programmen: Punkt in den Farbkreis ziehen verschiebt die Farbe in diese Richtung
/// (Abstand zur Mitte = Stärke). Doppelklick setzt zurück. X/Y liegen im Einheitskreis (rechts = Rot).
/// </summary>
public sealed class ColorWheelControl : FrameworkElement
{
    public static readonly DependencyProperty XProperty = DependencyProperty.Register(nameof(X), typeof(double), typeof(ColorWheelControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty YProperty = DependencyProperty.Register(nameof(Y), typeof(double), typeof(ColorWheelControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    private static readonly Pen RimPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30)), 1));
    private static readonly Pen PuckPen = Freeze(new Pen(Brushes.White, 2));
    private static readonly Pen CrossPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)), 1));
    private bool _dragging;

    public ColorWheelControl()
    {
        Width = 96;
        Height = 96;
        Cursor = Cursors.Hand;
        ToolTip = "Ziehen: Farbe verschieben · Doppelklick: zurücksetzen";
    }

    public double X
    {
        get => (double)GetValue(XProperty);
        set => SetValue(XProperty, value);
    }

    public double Y
    {
        get => (double)GetValue(YProperty);
        set => SetValue(YProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var radius = Math.Min(ActualWidth, ActualHeight) / 2 - 2;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        const int segments = 48;
        for (var i = 0; i < segments; i++)
        {
            var a0 = i * 2 * Math.PI / segments;
            var a1 = (i + 1) * 2 * Math.PI / segments;
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(center, true, true);
                g.LineTo(new Point(center.X + radius * Math.Cos(a0), center.Y - radius * Math.Sin(a0)), true, false);
                g.LineTo(new Point(center.X + radius * Math.Cos(a1), center.Y - radius * Math.Sin(a1)), true, false);
            }

            geometry.Freeze();
            var brush = new RadialGradientBrush(Color.FromRgb(0x80, 0x80, 0x80), Hsv((i + 0.5) * 360.0 / segments))
            {
                Center = center, GradientOrigin = center, RadiusX = radius, RadiusY = radius, MappingMode = BrushMappingMode.Absolute,
            };
            brush.Freeze();
            dc.DrawGeometry(brush, null, geometry);
        }

        dc.DrawEllipse(null, RimPen, center, radius, radius);
        dc.DrawLine(CrossPen, new Point(center.X - radius, center.Y), new Point(center.X + radius, center.Y));
        dc.DrawLine(CrossPen, new Point(center.X, center.Y - radius), new Point(center.X, center.Y + radius));
        var puck = new Point(center.X + X * radius, center.Y + Y * radius);
        dc.DrawEllipse(null, PuckPen, puck, 5, 5);
        dc.DrawEllipse(null, RimPen, puck, 6.5, 6.5);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2)
        {
            X = 0;
            Y = 0;
            e.Handled = true;
            return;
        }

        _dragging = true;
        CaptureMouse();
        Update(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            Update(e.GetPosition(this));
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragging = false;
        ReleaseMouseCapture();
    }

    private void Update(Point p)
    {
        var radius = Math.Min(ActualWidth, ActualHeight) / 2 - 2;
        var x = (p.X - ActualWidth / 2) / radius;
        var y = (p.Y - ActualHeight / 2) / radius;
        var length = Math.Sqrt(x * x + y * y);
        if (length > 1)
        {
            x /= length;
            y /= length;
        }

        // Feinfühlig: Umschalt = halbe Wirkung
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            x *= 0.5;
            y *= 0.5;
        }

        X = Math.Round(x, 3);
        Y = Math.Round(y, 3);
    }

    private static Color Hsv(double hue)
    {
        var h = hue / 60;
        var x = 1 - Math.Abs(h % 2 - 1);
        var (r, g, b) = h switch
        {
            < 1 => (1.0, x, 0.0),
            < 2 => (x, 1.0, 0.0),
            < 3 => (0.0, 1.0, x),
            < 4 => (0.0, x, 1.0),
            < 5 => (x, 0.0, 1.0),
            _ => (1.0, 0.0, x),
        };
        return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    private static T Freeze<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
