using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MovieWorker.Core.Media;
using MovieWorker.Core.Time;
using MovieWorker.Core.Timeline;

namespace MovieWorker.App.Controls;

public sealed class ClipMoveEventArgs(Guid clipId, long newStart, Guid trackId) : EventArgs
{
    public Guid ClipId { get; } = clipId;
    public long NewStart { get; } = newStart;
    public Guid TrackId { get; } = trackId;
}

public sealed class ClipTrimEventArgs(Guid clipId, bool atStart, long delta) : EventArgs
{
    public Guid ClipId { get; } = clipId;
    public bool AtStart { get; } = atStart;
    public long Delta { get; } = delta;
}

public sealed class ClipBodyEditEventArgs(Guid clipId, long delta) : EventArgs
{
    public Guid ClipId { get; } = clipId;
    public long Delta { get; } = delta;
}

public sealed class MediaDropEventArgs(Guid mediaId, Guid? trackId, long frame) : EventArgs
{
    public Guid MediaId { get; } = mediaId;
    public Guid? TrackId { get; } = trackId;
    public long Frame { get; } = frame;
}

/// <summary>
/// Die Spuransicht: Zeitlineal mit Timecode, Video- und Tonspuren, Clips mit Vorschaubild und Wellenform,
/// Abspielkopf und In/Out-Marken. Clips lassen sich verschieben (auch auf andere Spuren) und an den Rändern
/// trimmen; dabei rasten sie an Schnittpunkten und am Abspielkopf ein.
/// </summary>
public sealed class TimelineView : FrameworkElement
{
    public const string MediaDragFormat = "MovieWorker.MediaId";
    public const double RulerHeight = 30;
    public const double HeaderWidth = 120;
    private double VideoTrackHeight => Compact ? 26 : 70;
    private double AudioTrackHeight => Compact ? 18 : 52;
    private double TitleTrackHeight => Compact ? 16 : 34;
    private const double EdgeGrab = 7;
    private const double SnapPixels = 10;

    public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register(
        nameof(Project), typeof(MovieProject), typeof(TimelineView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty PlayheadProperty = DependencyProperty.Register(
        nameof(Playhead), typeof(long), typeof(TimelineView),
        new FrameworkPropertyMetadata(0L, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty SelectedClipIdProperty = DependencyProperty.Register(
        nameof(SelectedClipId), typeof(Guid?), typeof(TimelineView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(
        nameof(Compact), typeof(bool), typeof(TimelineView),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty TrimModeProperty = DependencyProperty.Register(
        nameof(TrimMode), typeof(TrimMode), typeof(TimelineView), new FrameworkPropertyMetadata(TrimMode.Normal));

    public static readonly DependencyProperty SnappingProperty = DependencyProperty.Register(
        nameof(Snapping), typeof(bool), typeof(TimelineView), new FrameworkPropertyMetadata(true));

    private static readonly Brush Background = Freeze(new SolidColorBrush(Color.FromRgb(0x1E, 0x1F, 0x22)));
    private static readonly Brush RowBrushA = Freeze(new SolidColorBrush(Color.FromRgb(0x26, 0x27, 0x2B)));
    private static readonly Brush RowBrushB = Freeze(new SolidColorBrush(Color.FromRgb(0x2B, 0x2C, 0x31)));
    private static readonly Brush HeaderBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x33, 0x35, 0x3A)));
    private static readonly Brush RulerBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2F, 0x31, 0x36)));
    private static readonly Brush TextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)));
    private static readonly Brush DimTextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x9A, 0x9C, 0xA3)));
    private static readonly Brush VideoClipBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x3B, 0x6F, 0xB6)));
    private static readonly Brush AudioClipBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2F, 0x8F, 0x5B)));
    private static readonly Brush NestedClipBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x7A, 0x55, 0xC2)));
    private static readonly Brush[] MulticamBrushes =
    [
        Freeze(new SolidColorBrush(Color.FromRgb(0x3B, 0x6F, 0xB6))),
        Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0x8C, 0x8C))),
        Freeze(new SolidColorBrush(Color.FromRgb(0x9C, 0x4F, 0x9C))),
        Freeze(new SolidColorBrush(Color.FromRgb(0xA8, 0x6B, 0x2D))),
    ];

    private static readonly Brush TitleClipBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xC9, 0x6A, 0x1E)));
    private static readonly Brush TransitionBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush KeyframeBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x4A)));
    private static readonly Brush WaveBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xC0, 0xDD, 0xF5, 0xE6)));
    private static readonly Brush MarkBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x30, 0x7F, 0xB8, 0xFF)));
    private static readonly Brush AdrBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF0, 0x8C, 0x00)));
    private static readonly Brush MarkEdgeBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x7F, 0xB8, 0xFF)));
    private static readonly Brush FadeBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)));
    private static readonly Brush BadgeBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xC0, 0x10, 0x10, 0x10)));
    private static readonly Brush MuteBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xC8, 0x3C, 0x3C)));
    private static readonly Pen ClipPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0, 0, 0)), 1));
    private static readonly Pen SelectedPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x2E)), 2));
    private static readonly Pen GhostPen = Freeze(new Pen(Brushes.White, 1.5) { DashStyle = DashStyles.Dash });
    private static readonly Pen TickPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x80, 0x82, 0x88)), 1));
    private static readonly Pen PlayheadPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x45)), 1.5));
    private static readonly Pen SnapPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x40)), 1));
    private static readonly Pen RowPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x18, 0x19, 0x1B)), 1));
    private static readonly Typeface UiFont = new("Segoe UI");

    private DragMode _drag = DragMode.None;
    private Point _dragStartPoint;
    private TimelineClip? _dragClip;
    private TimelineTrack? _dragTrack;
    private long _grabOffset;
    private long _ghostStart;
    private TimelineTrack? _ghostTrack;
    private long _trimDelta;
    private long? _snapLine;
    private (Guid MediaId, long Length, long Frame, TimelineTrack? Track)? _dropGhost;

    public TimelineView()
    {
        Focusable = true;
        AllowDrop = true;
        ClipToBounds = true;
        FocusVisualStyle = null;
    }

    private enum DragMode
    {
        None,
        Scrub,
        Move,
        TrimStart,
        TrimEnd,
        Pending,
        BodyEdit,
    }

    public event EventHandler<ClipMoveEventArgs>? ClipMoveRequested;

    public event EventHandler<ClipTrimEventArgs>? ClipTrimRequested;

    /// <summary>Slip bzw. Slide: Clip im entsprechenden Modus gezogen (Bilder nach rechts positiv).</summary>
    public event EventHandler<ClipBodyEditEventArgs>? ClipBodyEditRequested;

    public event EventHandler<MediaDropEventArgs>? MediaDropped;

    public event EventHandler<Guid>? TrackMuteToggled;

    public event EventHandler<Guid>? ClipDoubleClicked;

    /// <summary>Zoom oder Scrollposition hat sich geändert (für die Bildlaufleiste).</summary>
    public event EventHandler? ViewChanged;

    public MovieProject? Project
    {
        get => (MovieProject?)GetValue(ProjectProperty);
        set => SetValue(ProjectProperty, value);
    }

    public long Playhead
    {
        get => (long)GetValue(PlayheadProperty);
        set => SetValue(PlayheadProperty, value);
    }

    public Guid? SelectedClipId
    {
        get => (Guid?)GetValue(SelectedClipIdProperty);
        set => SetValue(SelectedClipIdProperty, value);
    }

    /// <summary>Kompakte Übersicht (Doppel-Timeline): niedrige Spuren, ohne Vorschaubilder und Wellenformen.</summary>
    public bool Compact
    {
        get => (bool)GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    /// <summary>Normal, Ripple, Roll (Ränder ziehen) oder Slip, Slide (Clip ziehen).</summary>
    public TrimMode TrimMode
    {
        get => (TrimMode)GetValue(TrimModeProperty);
        set => SetValue(TrimModeProperty, value);
    }

    public bool Snapping
    {
        get => (bool)GetValue(SnappingProperty);
        set => SetValue(SnappingProperty, value);
    }

    public TimelineViewport Viewport { get; } = new();

    /// <summary>Liefert ein Vorschaubild für den Anfang eines Clips (oder null, solange es noch lädt).</summary>
    public Func<TimelineClip, ImageSource?>? ThumbnailProvider { get; set; }

    /// <summary>Liefert die Wellenform der Quelle eines Clips (oder null, solange sie noch lädt).</summary>
    public Func<TimelineClip, WaveformData?>? WaveformProvider { get; set; }

    private FrameRate Rate => Project?.Settings.FrameRate ?? FrameRate.Fps25;

    private double ContentWidth => Math.Max(1, ActualWidth - HeaderWidth);

    // ------------------------------------------------------------------ Zoom & Scroll

    public void ZoomBy(double factor, double? anchorX = null)
    {
        Viewport.Width = ContentWidth;
        Viewport.ZoomAround(anchorX ?? Viewport.FrameToX(Playhead), factor);
        OnViewChanged();
    }

    public void ZoomToFit()
    {
        Viewport.Width = ContentWidth;
        Viewport.Fit(Math.Max(Project?.DurationFrames ?? 0, Rate.NominalFps * 10));
        OnViewChanged();
    }

    public void ScrollTo(double frame)
    {
        Viewport.ScrollFrame = frame;
        OnViewChanged();
    }

    /// <summary>Hält den Abspielkopf beim Abspielen im Bild.</summary>
    public void FollowPlayhead()
    {
        Viewport.Width = ContentWidth;
        if (Viewport.EnsureVisible(Playhead))
        {
            OnViewChanged();
        }
    }

    private void OnViewChanged()
    {
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------------ Layout

    private readonly record struct TrackRow(TimelineTrack Track, double Top, double Height);

    private List<TrackRow> Rows()
    {
        var rows = new List<TrackRow>();
        if (Project is null)
        {
            return rows;
        }

        var y = RulerHeight;
        foreach (var track in Project.TitleTracks.Concat(Project.VideoTracks.Reverse()).Concat(Project.AudioTracks))
        {
            var height = track.Kind switch
            {
                TrackKind.Video => VideoTrackHeight,
                TrackKind.Title => TitleTrackHeight,
                _ => AudioTrackHeight,
            };
            rows.Add(new TrackRow(track, y, height));
            y += height;
        }

        return rows;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var rows = Rows();
        var height = rows.Count == 0 ? RulerHeight + 100 : rows[^1].Top + rows[^1].Height + 20;
        return new Size(double.IsInfinity(availableSize.Width) ? 800 : availableSize.Width, height);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Viewport.Width = ContentWidth;
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private double X(double frame) => HeaderWidth + Viewport.FrameToX(frame);

    private long FrameAt(double x) => Math.Max(0, (long)Math.Round(Viewport.XToFrame(x - HeaderWidth)));

    private TrackRow? RowAt(double y)
    {
        foreach (var row in Rows())
        {
            if (y >= row.Top && y < row.Top + row.Height)
            {
                return row;
            }
        }

        return null;
    }

    // ------------------------------------------------------------------ Zeichnen

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        dc.DrawRectangle(Background, null, new Rect(0, 0, width, height));
        if (Project is not { } project)
        {
            return;
        }

        Viewport.Width = ContentWidth;
        var rows = Rows();
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var contentRect = new Rect(HeaderWidth, 0, Math.Max(0, width - HeaderWidth), height);

        // Spurhintergründe
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            dc.DrawRectangle(i % 2 == 0 ? RowBrushA : RowBrushB, null, new Rect(HeaderWidth, row.Top, contentRect.Width, row.Height));
            dc.DrawLine(RowPen, new Point(0, row.Top + row.Height), new Point(width, row.Top + row.Height));
        }

        dc.PushClip(new RectangleGeometry(contentRect));

        // In/Out-Bereich
        if (project.MarkIn is { } markIn)
        {
            var end = project.MarkOut ?? project.DurationFrames;
            dc.DrawRectangle(MarkBrush, null, new Rect(X(markIn), 0, Math.Max(1, X(end) - X(markIn)), height));
        }

        // ADR-Szenen: orangefarbener Balken am oberen Rand
        foreach (var cue in project.AdrCues)
        {
            dc.DrawRectangle(AdrBrush, null, new Rect(X(cue.StartFrame), 0, Math.Max(2, X(cue.EndFrame) - X(cue.StartFrame)), 4));
        }

        // Clips
        foreach (var row in rows)
        {
            foreach (var clip in row.Track.Clips)
            {
                if (_drag == DragMode.Move && _dragClip?.Id == clip.Id)
                {
                    DrawClip(dc, row, clip, clip.StartFrame, clip.LengthFrames, dpi, ghostSource: true);
                    continue;
                }

                var start = clip.StartFrame;
                var length = clip.LengthFrames;
                if (_dragClip?.Id == clip.Id && _drag is DragMode.TrimStart)
                {
                    start += _trimDelta;
                    length -= _trimDelta;
                }
                else if (_dragClip?.Id == clip.Id && _drag is DragMode.TrimEnd)
                {
                    length += _trimDelta;
                }

                DrawClip(dc, row, clip, start, length, dpi, ghostSource: false);
            }
        }

        // Geister beim Ziehen
        if (_drag == DragMode.Move && _dragClip is { } moving && _ghostTrack is { } ghostTrack
            && rows.FirstOrDefault(r => r.Track.Id == ghostTrack.Id) is { Track: not null } ghostRow)
        {
            DrawClip(dc, ghostRow, moving, _ghostStart, moving.LengthFrames, dpi, ghostSource: false);
            dc.DrawRectangle(null, GhostPen, ClipRect(ghostRow, _ghostStart, moving.LengthFrames));
        }

        if (_dropGhost is { } drop && drop.Track is { } dropTrack
            && rows.FirstOrDefault(r => r.Track.Id == dropTrack.Id) is { Track: not null } dropRow)
        {
            dc.DrawRectangle(null, GhostPen, ClipRect(dropRow, drop.Frame, drop.Length));
        }

        if (_drag == DragMode.BodyEdit && _dragClip is { } edited)
        {
            var label = $"{(TrimMode == TrimMode.Slip ? "Slip" : "Slide")} {_trimDelta:+0;-0;0} Bilder";
            var row = rows.FirstOrDefault(r => r.Track.Clips.Any(c => c.Id == edited.Id));
            if (row.Track is not null)
            {
                var r = ClipRect(row, edited.StartFrame + (TrimMode == TrimMode.Slide ? _trimDelta : 0), edited.LengthFrames);
                dc.DrawRectangle(null, GhostPen, r);
                DrawText(dc, label, new Point(r.Left + 4, r.Bottom - 18), 11, TextBrush, dpi, 200, background: BadgeBrush);
            }
        }

        if (_snapLine is { } snap)
        {
            dc.DrawLine(SnapPen, new Point(X(snap), RulerHeight), new Point(X(snap), height));
        }

        dc.Pop();

        DrawRuler(dc, width, dpi);
        DrawHeaders(dc, rows, dpi);

        // Abspielkopf
        var px = X(Playhead);
        if (px >= HeaderWidth && px <= width)
        {
            dc.DrawLine(PlayheadPen, new Point(px, 0), new Point(px, height));
            var head = new StreamGeometry();
            using (var g = head.Open())
            {
                g.BeginFigure(new Point(px - 6, 0), true, true);
                g.LineTo(new Point(px + 6, 0), true, false);
                g.LineTo(new Point(px, 10), true, false);
            }

            head.Freeze();
            dc.DrawGeometry(PlayheadPen.Brush, null, head);
        }
    }

    private Rect ClipRect(TrackRow row, long start, long length)
    {
        var x1 = X(start);
        var x2 = X(start + length);
        return new Rect(x1, row.Top + 3, Math.Max(2, x2 - x1), row.Height - 6);
    }

    private void DrawClip(DrawingContext dc, TrackRow row, TimelineClip clip, long start, long length, double dpi, bool ghostSource)
    {
        var rect = ClipRect(row, start, length);
        if (rect.Right < HeaderWidth || rect.Left > ActualWidth)
        {
            return;
        }

        var isVideo = row.Track.Kind == TrackKind.Video;
        dc.PushOpacity(ghostSource ? 0.35 : row.Track.Muted ? 0.45 : 1);
        var fill = clip.IsTitle ? TitleClipBrush : clip.IsNested ? NestedClipBrush : clip.IsMulticam ? MulticamBrushes[clip.ActiveAngle % MulticamBrushes.Length]
            : isVideo ? VideoClipBrush : AudioClipBrush;
        dc.DrawRoundedRectangle(fill, ClipPen, rect, 3, 3);
        dc.PushClip(new RectangleGeometry(rect));

        // Vorschaubild am Clipanfang
        var textLeft = rect.Left + 4;
        if (!Compact && isVideo && clip.HasVideo && rect.Width > 30 && ThumbnailProvider?.Invoke(clip) is { } thumb && thumb.Height > 0)
        {
            var h = rect.Height - 4;
            var w = h * thumb.Width / thumb.Height;
            dc.DrawImage(thumb, new Rect(rect.Left + 2, rect.Top + 2, w, h));
            textLeft = rect.Left + w + 6;
            if (rect.Width > w * 3)
            {
                dc.DrawImage(thumb, new Rect(rect.Right - w - 2, rect.Top + 2, w, h));
            }
        }

        // Wellenform (Ton der Quelle, passend zum Ausschnitt)
        if (!Compact && clip.HasAudio && WaveformProvider?.Invoke(clip) is { } wave)
        {
            var waveTop = isVideo ? rect.Top + rect.Height * 0.62 : rect.Top + 14;
            var waveHeight = isVideo ? rect.Height * 0.36 : rect.Height - 16;
            DrawWaveform(dc, wave, clip, start, rect, waveTop, waveHeight, clip.AudioMuted);
        }

        // Blenden
        if (clip.FadeInFrames > 0)
        {
            DrawFade(dc, rect.Left, X(start + clip.FadeInFrames), rect, fadeIn: true);
        }

        if (clip.FadeOutFrames > 0)
        {
            DrawFade(dc, X(start + length - clip.FadeOutFrames), rect.Right, rect, fadeIn: false);
        }

        // Übergang am Clipanfang: Fliege über dem Schnitt
        if (clip.TransitionIn is { } transition && !Compact)
        {
            var tw = Math.Max(6, transition.DurationFrames * Viewport.PixelsPerFrame);
            var bow = new StreamGeometry();
            using (var g = bow.Open())
            {
                g.BeginFigure(new Point(rect.Left, rect.Top), true, true);
                g.LineTo(new Point(rect.Left + tw, rect.Bottom), true, false);
                g.LineTo(new Point(rect.Left + tw, rect.Top), true, false);
                g.LineTo(new Point(rect.Left, rect.Bottom), true, false);
            }

            bow.Freeze();
            dc.PushOpacity(0.45);
            dc.DrawGeometry(TransitionBrush, null, bow);
            dc.Pop();
        }

        // Name und Kennzeichen
        var label = clip.IsTitle ? "T  " + clip.Name
            : clip.IsMulticam ? $"🎥 {clip.ActiveAngle + 1}: {clip.MulticamAngles![Math.Clamp(clip.ActiveAngle, 0, clip.MulticamAngles.Count - 1)].Name}"
            : clip.Name;
        if (Math.Abs(clip.Speed - 1) > 1e-6)
        {
            label += $"  ×{clip.Speed.ToString("0.###", CultureInfo.CurrentCulture)}";
        }

        if (rect.Width > 40)
        {
            DrawText(dc, label, new Point(textLeft, rect.Top + 1), Compact ? 9 : 11, TextBrush, dpi, rect.Right - textLeft - 4,
                background: Compact ? null : BadgeBrush);
        }

        var badge = clip.IsNested ? "▣ verschachtelt" : clip.ColorTransform switch
        {
            ColorTransform.Lut => "LUT",
            ColorTransform.HdrToSdr => "HDR→SDR",
            _ => null,
        };

        // Keyframes als kleine Rauten am unteren Rand
        if (!Compact && clip.Keyframes.Count > 0)
        {
            foreach (var frame in clip.Keyframes.SelectMany(k => k.Keys).Select(k => k.Frame).Distinct())
            {
                var kx = X(start + frame);
                if (kx < rect.Left || kx > rect.Right)
                {
                    continue;
                }

                var diamond = new StreamGeometry();
                using (var g = diamond.Open())
                {
                    g.BeginFigure(new Point(kx, rect.Bottom - 10), true, true);
                    g.LineTo(new Point(kx + 4, rect.Bottom - 6), true, false);
                    g.LineTo(new Point(kx, rect.Bottom - 2), true, false);
                    g.LineTo(new Point(kx - 4, rect.Bottom - 6), true, false);
                }

                diamond.Freeze();
                dc.DrawGeometry(KeyframeBrush, null, diamond);
            }
        }
        if (badge is not null && rect.Width > 90 && !Compact)
        {
            DrawText(dc, badge, new Point(textLeft, rect.Top + 16), 9, TextBrush, dpi, 80, background: BadgeBrush);
        }

        dc.Pop();
        if (SelectedClipId == clip.Id && !ghostSource)
        {
            dc.DrawRoundedRectangle(null, SelectedPen, rect, 3, 3);
        }

        dc.Pop();
    }

    private void DrawWaveform(DrawingContext dc, WaveformData wave, TimelineClip clip, long start, Rect rect, double top, double height,
        bool muted)
    {
        var mid = top + height / 2;
        var left = Math.Max(rect.Left, HeaderWidth);
        var right = Math.Min(rect.Right, ActualWidth);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            for (var x = left; x < right; x += 2)
            {
                var frameA = Viewport.XToFrame(x - HeaderWidth);
                var frameB = Viewport.XToFrame(x + 2 - HeaderWidth);
                var tA = clip.SourceIn + Rate.FramesToSeconds((long)(frameA - start)) * clip.Speed;
                var tB = clip.SourceIn + Rate.FramesToSeconds((long)Math.Ceiling(frameB - start)) * clip.Speed;
                var peak = wave.PeakBetween(tA, Math.Max(tB, tA + 0.01));
                var h = Math.Max(0.5, peak * height / 2);
                g.BeginFigure(new Point(x, mid - h), false, false);
                g.LineTo(new Point(x, mid + h), true, false);
            }
        }

        geometry.Freeze();
        dc.PushOpacity(muted ? 0.35 : 1);
        dc.DrawGeometry(null, new Pen(WaveBrush, 1.5), geometry);
        dc.Pop();
    }

    private static void DrawFade(DrawingContext dc, double x1, double x2, Rect rect, bool fadeIn)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            if (fadeIn)
            {
                g.BeginFigure(new Point(x1, rect.Top), true, true);
                g.LineTo(new Point(x2, rect.Top), true, false);
                g.LineTo(new Point(x1, rect.Bottom), true, false);
            }
            else
            {
                g.BeginFigure(new Point(x1, rect.Top), true, true);
                g.LineTo(new Point(x2, rect.Top), true, false);
                g.LineTo(new Point(x2, rect.Bottom), true, false);
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(FadeBrush, null, geometry);
    }

    private void DrawRuler(DrawingContext dc, double width, double dpi)
    {
        dc.DrawRectangle(RulerBrush, null, new Rect(0, 0, width, RulerHeight));
        dc.PushClip(new RectangleGeometry(new Rect(HeaderWidth, 0, Math.Max(0, width - HeaderWidth), RulerHeight)));
        var project = Project!;
        var step = Viewport.RulerStep(Rate);
        var minor = step >= 5 ? step / 5 : 1;
        var first = (long)Math.Floor(Viewport.ScrollFrame / minor) * minor;
        var last = Viewport.XToFrame(ContentWidth);
        for (var f = first; f <= last; f += minor)
        {
            var x = X(f);
            var major = f % step == 0;
            dc.DrawLine(TickPen, new Point(x, major ? 12 : 22), new Point(x, RulerHeight));
            if (major)
            {
                var label = step < Rate.NominalFps
                    ? Timecode.FromFrames(f, Rate, project.Settings.DropFrameTimecode)
                    : Timecode.FromFrames(f, Rate, project.Settings.DropFrameTimecode)[..8];
                DrawText(dc, label, new Point(x + 3, 1), 10, DimTextBrush, dpi, 200);
            }
        }

        if (project.MarkIn is { } markIn)
        {
            dc.DrawRectangle(MarkEdgeBrush, null, new Rect(X(markIn), RulerHeight - 4, Math.Max(2, X(project.MarkOut ?? project.DurationFrames) - X(markIn)), 4));
        }

        dc.Pop();
        DrawText(dc, Timecode.FromFrames(Playhead, Rate, project.Settings.DropFrameTimecode), new Point(8, 6), 12, TextBrush, dpi, HeaderWidth - 10);
    }

    private void DrawHeaders(DrawingContext dc, List<TrackRow> rows, double dpi)
    {
        foreach (var row in rows)
        {
            var rect = new Rect(0, row.Top, HeaderWidth, row.Height);
            dc.DrawRectangle(HeaderBrush, null, rect);
            if (Compact)
            {
                DrawText(dc, row.Track.Name, new Point(8, row.Top + 2), 10, TextBrush, dpi, HeaderWidth - 12);
                continue;
            }

            DrawText(dc, row.Track.Name, new Point(8, row.Top + 6), 12, TextBrush, dpi, HeaderWidth - 40);
            if (row.Track.Kind != TrackKind.Title)
            {
                DrawText(dc, row.Track.Kind == TrackKind.Video ? "Video + Kameraton" : "Ton", new Point(8, row.Top + 24), 9, DimTextBrush, dpi, HeaderWidth - 12);
            }
            var mute = MuteRect(row);
            dc.DrawRoundedRectangle(row.Track.Muted ? MuteBrush : RulerBrush, ClipPen, mute, 3, 3);
            DrawText(dc, "M", new Point(mute.Left + 5, mute.Top + 2), 11, TextBrush, dpi, 20);
        }
    }

    private static Rect MuteRect(TrackRow row) => new(HeaderWidth - 28, row.Top + 6, 20, 18);

    private void DrawText(DrawingContext dc, string text, Point origin, double size, Brush brush, double dpi, double maxWidth,
        Brush? background = null)
    {
        if (maxWidth < 8)
        {
            return;
        }

        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, UiFont, size, brush, dpi)
        {
            MaxTextWidth = maxWidth,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        if (background is not null)
        {
            dc.DrawRoundedRectangle(background, null, new Rect(origin.X - 2, origin.Y, formatted.Width + 4, formatted.Height), 2, 2);
        }

        dc.DrawText(formatted, origin);
    }

    // ------------------------------------------------------------------ Maus

    private (TrackRow Row, TimelineClip Clip)? HitClip(Point p)
    {
        if (RowAt(p.Y) is not { } row)
        {
            return null;
        }

        foreach (var clip in row.Track.Clips)
        {
            var rect = ClipRect(row, clip.StartFrame, clip.LengthFrames);
            rect.Inflate(EdgeGrab / 2, 0);
            if (rect.Contains(p))
            {
                return (row, clip);
            }
        }

        return null;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var p = e.GetPosition(this);
        _dragStartPoint = p;
        if (Project is null)
        {
            return;
        }

        if (p.Y < RulerHeight && p.X >= HeaderWidth)
        {
            _drag = DragMode.Scrub;
            Playhead = FrameAt(p.X);
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (p.X < HeaderWidth)
        {
            if (!Compact && RowAt(p.Y) is { } headerRow && MuteRect(headerRow).Contains(p))
            {
                TrackMuteToggled?.Invoke(this, headerRow.Track.Id);
            }

            e.Handled = true;
            return;
        }

        if (HitClip(p) is { } hit)
        {
            SelectedClipId = hit.Clip.Id;
            if (e.ClickCount == 2)
            {
                ClipDoubleClicked?.Invoke(this, hit.Clip.Id);
                e.Handled = true;
                return;
            }

            _dragClip = hit.Clip;
            _dragTrack = hit.Row.Track;
            var left = X(hit.Clip.StartFrame);
            var right = X(hit.Clip.EndFrame);
            _drag = Math.Abs(p.X - left) <= EdgeGrab && right - left > EdgeGrab * 3 ? DragMode.TrimStart
                : Math.Abs(p.X - right) <= EdgeGrab ? DragMode.TrimEnd
                : DragMode.Pending;
            _grabOffset = FrameAt(p.X) - hit.Clip.StartFrame;
            _ghostStart = hit.Clip.StartFrame;
            _ghostTrack = hit.Row.Track;
            _trimDelta = 0;
            CaptureMouse();
        }
        else
        {
            SelectedClipId = null;
            Playhead = FrameAt(p.X);
            _drag = DragMode.Scrub;
            CaptureMouse();
        }

        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        if (Project is null)
        {
            return;
        }

        switch (_drag)
        {
            case DragMode.Scrub:
                Playhead = FrameAt(p.X);
                AutoScroll(p.X);
                return;
            case DragMode.Pending:
                if (Math.Abs(p.X - _dragStartPoint.X) > 3 || Math.Abs(p.Y - _dragStartPoint.Y) > 3)
                {
                    _drag = TrimMode is TrimMode.Slip or TrimMode.Slide ? DragMode.BodyEdit : DragMode.Move;
                }

                return;
            case DragMode.BodyEdit:
                _trimDelta = FrameAt(p.X) - FrameAt(_dragStartPoint.X);
                InvalidateVisual();
                return;
            case DragMode.Move when _dragClip is { } clip:
            {
                var start = Math.Max(0, FrameAt(p.X) - _grabOffset);
                _snapLine = null;
                if (Snapping)
                {
                    var points = SnapPoints(clip.Id);
                    var snapped = TimelineNavigation.SnapClipStart(start, clip.LengthFrames, points, SnapPixels / Viewport.PixelsPerFrame);
                    if (snapped != start)
                    {
                        _snapLine = points.Contains(snapped) ? snapped : snapped + clip.LengthFrames;
                    }

                    start = Math.Max(0, snapped);
                }

                _ghostStart = start;
                if (RowAt(p.Y) is { } row && row.Track.Kind == _dragTrack!.Kind)
                {
                    _ghostTrack = row.Track;
                }

                AutoScroll(p.X);
                InvalidateVisual();
                return;
            }

            case DragMode.TrimStart or DragMode.TrimEnd when _dragClip is { } clip:
            {
                var frame = FrameAt(p.X);
                _snapLine = null;
                if (Snapping)
                {
                    var snapped = TimelineNavigation.Snap(frame, SnapPoints(clip.Id), SnapPixels / Viewport.PixelsPerFrame, out var didSnap);
                    if (didSnap)
                    {
                        _snapLine = snapped;
                        frame = snapped;
                    }
                }

                _trimDelta = _drag == DragMode.TrimStart
                    ? Math.Min(frame - clip.StartFrame, clip.LengthFrames - 1)
                    : Math.Max(frame - clip.EndFrame, 1 - clip.LengthFrames);
                InvalidateVisual();
                return;
            }
        }

        // Mauszeiger an Clipkanten
        Cursor = p.X > HeaderWidth && HitClip(p) is { } hit
            && (Math.Abs(p.X - X(hit.Clip.StartFrame)) <= EdgeGrab || Math.Abs(p.X - X(hit.Clip.EndFrame)) <= EdgeGrab)
            ? Cursors.SizeWE
            : null;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var mode = _drag;
        var clip = _dragClip;
        _drag = DragMode.None;
        _snapLine = null;
        ReleaseMouseCapture();
        if (clip is not null)
        {
            if (mode == DragMode.Move && _ghostTrack is { } track && (_ghostStart != clip.StartFrame || track.Id != _dragTrack?.Id))
            {
                ClipMoveRequested?.Invoke(this, new ClipMoveEventArgs(clip.Id, _ghostStart, track.Id));
            }
            else if (mode is DragMode.TrimStart or DragMode.TrimEnd && _trimDelta != 0)
            {
                ClipTrimRequested?.Invoke(this, new ClipTrimEventArgs(clip.Id, mode == DragMode.TrimStart, _trimDelta));
            }
            else if (mode == DragMode.BodyEdit && _trimDelta != 0)
            {
                // Slip: nach rechts ziehen zeigt früheres Material (der Inhalt wandert mit der Maus).
                ClipBodyEditRequested?.Invoke(this, new ClipBodyEditEventArgs(clip.Id, TrimMode == TrimMode.Slip ? -_trimDelta : _trimDelta));
            }
        }

        _dragClip = null;
        _dragTrack = null;
        _trimDelta = 0;
        InvalidateVisual();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_drag != DragMode.None && Mouse.LeftButton == MouseButtonState.Released)
        {
            return;
        }

        if (_drag is not DragMode.None)
        {
            _drag = DragMode.None;
            _dragClip = null;
            _snapLine = null;
            InvalidateVisual();
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        var p = e.GetPosition(this);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ZoomBy(e.Delta > 0 ? 1.25 : 0.8, Math.Max(0, p.X - HeaderWidth));
            e.Handled = true;
        }
        else if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            ScrollTo(Viewport.ScrollFrame - e.Delta / 120.0 * 60 / Viewport.PixelsPerFrame);
            e.Handled = true;
        }
    }

    private void AutoScroll(double x)
    {
        if (x > ActualWidth - 20)
        {
            ScrollTo(Viewport.ScrollFrame + 15 / Viewport.PixelsPerFrame);
        }
        else if (x < HeaderWidth + 10 && Viewport.ScrollFrame > 0)
        {
            ScrollTo(Viewport.ScrollFrame - 15 / Viewport.PixelsPerFrame);
        }
    }

    private List<long> SnapPoints(Guid excludeClip)
    {
        var points = TimelineNavigation.EditPoints(Project!, excludeClip).ToList();
        points.Add(Playhead);
        if (Project!.MarkIn is { } i)
        {
            points.Add(i);
        }

        if (Project.MarkOut is { } o)
        {
            points.Add(o);
        }

        return points;
    }

    // ------------------------------------------------------------------ Drag & Drop aus dem Medienpool

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        if (e.Data.GetData(MediaDragFormat) is string id && Guid.TryParse(id, out var mediaId) && Project?.FindMedia(mediaId) is { } media)
        {
            var p = e.GetPosition(this);
            var row = RowAt(p.Y);
            var wantedKind = media.IsAudioOnly ? TrackKind.Audio : TrackKind.Video;
            var track = row is { } r && r.Track.Kind == wantedKind ? r.Track : null;
            var length = Rate.SecondsToFrames(media.Duration.TotalSeconds);
            var frame = FrameAt(p.X);
            if (Snapping)
            {
                frame = TimelineNavigation.SnapClipStart(frame, length, SnapPoints(Guid.Empty), SnapPixels / Viewport.PixelsPerFrame);
            }

            _dropGhost = (mediaId, length, Math.Max(0, frame), track);
            e.Effects = DragDropEffects.Copy;
            InvalidateVisual();
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        _dropGhost = null;
        InvalidateVisual();
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        if (_dropGhost is { } ghost)
        {
            MediaDropped?.Invoke(this, new MediaDropEventArgs(ghost.MediaId, ghost.Track?.Id, ghost.Frame));
            e.Handled = true;
        }

        _dropGhost = null;
        InvalidateVisual();
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
