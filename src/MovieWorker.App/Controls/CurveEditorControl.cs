using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MovieWorker.Core.Color;

namespace MovieWorker.App.Controls;

/// <summary>
/// Kurven-Editor: Klick setzt einen Punkt, Ziehen verschiebt ihn, Doppelklick auf einen Punkt löscht ihn.
/// Die Punktliste wird nach jeder Änderung als neue Liste zurückgeschrieben (Bindung TwoWay).
/// </summary>
public sealed class CurveEditorControl : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(nameof(Points), typeof(List<CurvePoint>),
        typeof(CurveEditorControl), new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty CurveBrushProperty = DependencyProperty.Register(nameof(CurveBrush), typeof(Brush),
        typeof(CurveEditorControl), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Pen GridPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)), 1));
    private static readonly Pen DiagonalPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), 1) { DashStyle = DashStyles.Dash });
    private int _dragIndex = -1;

    public CurveEditorControl()
    {
        Height = 150;
        Cursor = Cursors.Cross;
        ToolTip = "Klick: Punkt setzen · Ziehen: verschieben · Doppelklick auf Punkt: löschen";
    }

    public List<CurvePoint>? Points
    {
        get => (List<CurvePoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public Brush CurveBrush
    {
        get => (Brush)GetValue(CurveBrushProperty);
        set => SetValue(CurveBrushProperty, value);
    }

    private Rect Area => new(6, 6, Math.Max(10, ActualWidth - 12), Math.Max(10, ActualHeight - 12));

    protected override void OnRender(DrawingContext dc)
    {
        var area = Area;
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C)), null, new Rect(0, 0, ActualWidth, ActualHeight));
        for (var i = 1; i < 4; i++)
        {
            dc.DrawLine(GridPen, new Point(area.Left + area.Width * i / 4, area.Top), new Point(area.Left + area.Width * i / 4, area.Bottom));
            dc.DrawLine(GridPen, new Point(area.Left, area.Top + area.Height * i / 4), new Point(area.Right, area.Top + area.Height * i / 4));
        }

        dc.DrawLine(DiagonalPen, new Point(area.Left, area.Bottom), new Point(area.Right, area.Top));
        var points = Points ?? [];
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            for (var i = 0; i <= 128; i++)
            {
                var x = i / 128.0;
                var p = ToScreen(x, ColorCurves.Evaluate(points, x));
                if (i == 0)
                {
                    g.BeginFigure(p, false, false);
                }
                else
                {
                    g.LineTo(p, true, false);
                }
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, new Pen(CurveBrush, 2), geometry);
        foreach (var point in points)
        {
            dc.DrawEllipse(Brushes.White, new Pen(Brushes.Black, 1), ToScreen(point.X, point.Y), 4, 4);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var p = e.GetPosition(this);
        var points = (Points ?? []).Select(c => new CurvePoint { X = c.X, Y = c.Y }).ToList();
        var hit = points.FindIndex(c => (ToScreen(c.X, c.Y) - p).Length < 8);
        if (e.ClickCount == 2 && hit >= 0)
        {
            points.RemoveAt(hit);
            Points = points;
            e.Handled = true;
            return;
        }

        if (hit < 0)
        {
            var (x, y) = FromScreen(p);
            points.Add(new CurvePoint { X = x, Y = y });
            hit = points.Count - 1;
            Points = points;
        }

        _dragIndex = hit;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragIndex < 0 || e.LeftButton != MouseButtonState.Pressed || Points is null || _dragIndex >= Points.Count)
        {
            return;
        }

        var (x, y) = FromScreen(e.GetPosition(this));
        var points = Points.Select(c => new CurvePoint { X = c.X, Y = c.Y }).ToList();
        points[_dragIndex] = new CurvePoint { X = x, Y = y };
        Points = points;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragIndex = -1;
        ReleaseMouseCapture();
    }

    private Point ToScreen(double x, double y) => new(Area.Left + x * Area.Width, Area.Bottom - y * Area.Height);

    private (double X, double Y) FromScreen(Point p) =>
        (Math.Round(Math.Clamp((p.X - Area.Left) / Area.Width, 0, 1), 3), Math.Round(Math.Clamp((Area.Bottom - p.Y) / Area.Height, 0, 1), 3));

    private static T Freeze<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
