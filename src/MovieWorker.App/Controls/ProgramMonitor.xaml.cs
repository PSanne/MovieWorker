using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MovieWorker.App.ViewModels;
using MovieWorker.Core.Preview;
using MovieWorker.Core.Projects;
using MovieWorker.Core.Time;
using MovieWorker.Core.Timeline;

namespace MovieWorker.App.Controls;

/// <summary>
/// Programmmonitor: spielt die Timeline ab. Beim Abspielen laufen Proxys bzw. Originale im Windows-Player
/// (die Uhr gibt den Takt vor, der Player wird nachgeführt); im Stillstand zeigt er ein exaktes Standbild
/// über ffmpeg – mit LUT bzw. HDR-Umrechnung wie im Export. J/K/L wie in Profi-Programmen.
/// </summary>
public partial class ProgramMonitor : UserControl
{
    private static readonly double[] ShuttleSteps = [1, 2, 4, 8];

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(20) };
    private readonly DispatcherTimer _stillDebounce = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private readonly Stopwatch _clock = new();
    private readonly HashSet<string> _failedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, (MediaPlayer Player, string? Path)> _audioPlayers = [];
    private MainViewModel? _vm;
    private long _startFrame;
    private double _speed = 1;
    private long _segmentStart;
    private long _segmentEnd = -1;
    private string? _videoPath;
    private PlaybackSource? _currentVideo;
    private bool _stillMode;
    private bool _updatingPlayhead;
    private DateTime _lastStillRequest;
    private CancellationTokenSource? _stillCts;
    private bool _stillBusy;

    public ProgramMonitor()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Tick();
        _stillDebounce.Tick += (_, _) =>
        {
            _stillDebounce.Stop();
            _ = RenderStillAsync();
        };
    }

    public bool IsPlaying { get; private set; }

    /// <summary>Ein exaktes Standbild wurde angezeigt (z. B. für die Messinstrumente).</summary>
    public event EventHandler? StillRendered;

    /// <summary>Anzahl fertig gerenderter Standbilder (für den Selbsttest).</summary>
    public int StillsRendered { get; private set; }

    public ImageSource? CurrentStill => Still.Source;

    /// <summary>Wiedergabe zeigt Standbilder (kein Windows-Decoder für die Datei).</summary>
    public bool IsStillMode => _stillMode;

    /// <summary>Anzahl der gerade angezeigten Titelzeilen (für den Selbsttest).</summary>
    public int TitleOverlayCount => TitleOverlay.Children.Count;

    /// <summary>Hinweistext im Monitor (z. B. Fehlermeldung der Vorschau) – für den Selbsttest.</summary>
    public string MessageText => Message.Text;

    public void Attach(MainViewModel vm)
    {
        _vm = vm;
        vm.PropertyChanged += OnViewModelChanged;
        vm.ProjectChanged += (_, _) =>
        {
            _segmentEnd = -1; // Timeline geändert → beim nächsten Takt neu planen
            if (!IsPlaying)
            {
                RequestStill();
            }
        };
        UpdateTexts();
        RequestStill();
    }

    // ------------------------------------------------------------------ Steuerung

    public void TogglePlay()
    {
        if (IsPlaying)
        {
            Pause();
        }
        else
        {
            Play(1);
        }
    }

    /// <summary>J (−1) und L (+1): jedes weitere Drücken verdoppelt das Tempo; K hält an.</summary>
    public void Shuttle(int direction)
    {
        if (direction == 0)
        {
            Pause();
            return;
        }

        var current = IsPlaying && Math.Sign(_speed) == direction ? Math.Abs(_speed) : 0;
        var next = ShuttleSteps.FirstOrDefault(s => s > current, ShuttleSteps[^1]);
        Play(direction * next);
    }

    public void Play(double speed)
    {
        if (_vm is null || _vm.Editor.Duration == 0)
        {
            return;
        }

        if (speed > 0 && _vm.PlayheadFrame >= _vm.Editor.Duration)
        {
            _vm.PlayheadFrame = 0;
        }

        _speed = speed;
        _startFrame = _vm.PlayheadFrame;
        _clock.Restart();
        _segmentEnd = -1;
        IsPlaying = true;
        _timer.Start();
        UpdateTexts();
        Tick();
    }

    public void Pause()
    {
        if (!IsPlaying)
        {
            return;
        }

        IsPlaying = false;
        _timer.Stop();
        _clock.Stop();
        Video.Pause();
        foreach (var (player, _) in _audioPlayers.Values)
        {
            player.Pause();
        }

        UpdateTexts();
        UpdateTitleOverlay(_vm!.Project, _vm.PlayheadFrame, visible: false);
        RequestStill();
    }

    private void OnPlay(object sender, RoutedEventArgs e) => TogglePlay();

    private void OnReverse(object sender, RoutedEventArgs e) => Shuttle(-1);

    private void OnForward(object sender, RoutedEventArgs e) => Shuttle(1);

    private void OnStart(object sender, RoutedEventArgs e)
    {
        Pause();
        _vm?.GoToStart();
    }

    private void OnEnd(object sender, RoutedEventArgs e)
    {
        Pause();
        _vm?.GoToEnd();
    }

    // ------------------------------------------------------------------ Wiedergabe

    private void Tick()
    {
        if (_vm is null)
        {
            return;
        }

        var project = _vm.Project;
        var rate = project.Settings.FrameRate;
        var duration = _vm.Editor.Duration;
        var frame = _startFrame + (long)Math.Round(_clock.Elapsed.TotalSeconds * rate.Value * _speed);
        if (frame >= duration || frame <= 0 && _speed < 0)
        {
            SetPlayhead(Math.Clamp(frame, 0, duration));
            Pause();
            return;
        }

        SetPlayhead(frame);
        UpdateTitleOverlay(project, frame, visible: true);

        // Rückwärts oder sehr schnell: Standbilder statt Player.
        var useStills = _speed < 0 || _speed > 2;
        if (useStills)
        {
            if (!_stillMode)
            {
                _stillMode = true;
                Video.Pause();
                Video.Visibility = Visibility.Hidden;
                PauseAudio();
            }

            RequestStillThrottled();
            return;
        }

        if (frame >= _segmentEnd || frame < _segmentStart)
        {
            LoadSegment(project, frame);
        }
        else if (_stillMode)
        {
            RequestStillThrottled();
        }
        else
        {
            CorrectDrift(frame, rate);
        }
    }

    private void SetPlayhead(long frame)
    {
        _updatingPlayhead = true;
        _vm!.PlayheadFrame = frame;
        _updatingPlayhead = false;
        UpdateTexts();
    }

    private void LoadSegment(MovieProject project, long frame)
    {
        _segmentStart = frame;
        _segmentEnd = PlaybackPlanner.NextChange(project, frame);
        _currentVideo = PlaybackPlanner.VideoAt(project, frame);
        _stillMode = false;

        if (_currentVideo is null)
        {
            Video.Pause();
            Video.Visibility = Visibility.Hidden;
            Still.Source = null;
            Message.Text = string.Empty;
        }
        else
        {
            var (path, seconds) = PlayablePath(_currentVideo);
            if (_failedPaths.Contains(path))
            {
                _stillMode = true;
                Video.Visibility = Visibility.Hidden;
                RequestStillThrottled();
            }
            else
            {
                if (!string.Equals(_videoPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    _videoPath = path;
                    Video.Source = new Uri(path);
                }

                Video.Position = TimeSpan.FromSeconds(seconds);
                Video.SpeedRatio = Math.Max(0.05, _currentVideo.Clip.Speed * _speed);
                Video.Volume = Math.Min(1, _currentVideo.Volume);
                Video.Balance = _currentVideo.Balance;
                Video.IsMuted = _currentVideo.Volume <= 0 || Math.Abs(_currentVideo.Clip.Speed - 1) > 1e-6;
                Video.Play();
                Video.Visibility = Visibility.Visible;
                Still.Visibility = Visibility.Collapsed;
                Message.Text = string.Empty;
            }
        }

        // Tonspuren (Musik, Sprache …)
        var audible = PlaybackPlanner.AudioAt(project, frame).Where(s => s.Track.Kind == TrackKind.Audio).ToList();
        foreach (var track in project.AudioTracks)
        {
            var source = audible.FirstOrDefault(s => s.Track.Id == track.Id);
            if (!_audioPlayers.TryGetValue(track.Id, out var entry))
            {
                entry = (new MediaPlayer(), null);
            }

            if (source is null || source.Volume <= 0)
            {
                entry.Player.Pause();
            }
            else
            {
                var (path, seconds) = PlayablePath(source);
                if (!string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase))
                {
                    entry.Player.Open(new Uri(path));
                    entry = (entry.Player, path);
                }

                entry.Player.Position = TimeSpan.FromSeconds(seconds);
                entry.Player.Volume = Math.Min(1, source.Volume);
                entry.Player.Balance = source.Balance;
                entry.Player.SpeedRatio = Math.Max(0.05, source.Clip.Speed * _speed);
                entry.Player.Play();
            }

            _audioPlayers[track.Id] = entry;
        }
    }

    /// <summary>Proxy (falls vorhanden) oder die passende Originaldatei mit der Zeit darin.</summary>
    private static (string Path, double Seconds) PlayablePath(PlaybackSource source) =>
        source.Media is { HasProxy: true } && MediaItem.UseProxies
            ? (source.PreviewPath, source.SourceSeconds)
            : (source.OriginalPath, source.OriginalSeconds);

    private void CorrectDrift(long frame, FrameRate rate)
    {
        if (_currentVideo is not { } video || !Video.NaturalDuration.HasTimeSpan)
        {
            return;
        }

        var sourceNow = video.Clip.SourceTimeAt(frame, rate);
        var usesProxy = video.Media is { HasProxy: true } && MediaItem.UseProxies;
        var expected = usesProxy ? sourceNow : video.OriginalSeconds + (sourceNow - video.SourceSeconds);
        if (Math.Abs(Video.Position.TotalSeconds - expected) > 0.3)
        {
            Video.Position = TimeSpan.FromSeconds(expected);
        }
    }

    private void PauseAudio()
    {
        foreach (var (player, _) in _audioPlayers.Values)
        {
            player.Pause();
        }
    }

    private void OnMediaOpened(object sender, RoutedEventArgs e)
    {
        if (IsPlaying && _vm is not null)
        {
            CorrectDrift(_vm.PlayheadFrame, _vm.Project.Settings.FrameRate);
        }
    }

    private void OnMediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        // Windows kann die Datei nicht abspielen (z. B. HEVC 4:2:2 ohne Proxy): Standbilder statt Video.
        if (_videoPath is not null)
        {
            _failedPaths.Add(_videoPath);
        }

        _videoPath = null;
        Video.Visibility = Visibility.Hidden;
        _stillMode = true;
        if (_vm is not null)
        {
            _vm.StatusText = "Diese Aufnahme kann Windows nicht direkt abspielen – bitte einen Proxy erstellen (Medienpool → Proxy).";
        }
    }

    // ------------------------------------------------------------------ Standbild

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.PlayheadFrame))
        {
            UpdateTexts();
            if (!IsPlaying && !_updatingPlayhead)
            {
                RequestStill();
            }
            else if (IsPlaying && !_updatingPlayhead)
            {
                // Abspielkopf während der Wiedergabe von Hand versetzt: ab dort weiterspielen.
                _startFrame = _vm!.PlayheadFrame;
                _clock.Restart();
                _segmentEnd = -1;
            }
        }
    }

    public void RequestStill()
    {
        _stillDebounce.Stop();
        _stillDebounce.Start();
    }

    private void RequestStillThrottled()
    {
        if (_stillBusy || (DateTime.UtcNow - _lastStillRequest).TotalMilliseconds < 200)
        {
            return;
        }

        _lastStillRequest = DateTime.UtcNow;
        _ = RenderStillAsync();
    }

    /// <summary>Wartet, bis das aktuelle Standbild fertig ist (für den Selbsttest).</summary>
    public async Task<bool> RenderStillNowAsync()
    {
        _stillDebounce.Stop();
        return await RenderStillAsync();
    }

    private async Task<bool> RenderStillAsync()
    {
        if (_vm is null)
        {
            return false;
        }

        _stillCts?.Cancel();
        _stillCts = new CancellationTokenSource();
        var token = _stillCts.Token;
        var project = _vm.Project;
        if (!project.AllClips.Any())
        {
            Still.Source = null;
            Message.Text = "Die Timeline ist leer.";
            return false;
        }

        _stillBusy = true;
        try
        {
            // Exaktes Bild über dieselbe Kette wie der Export: alle Spuren, Bild-in-Bild, Mischmodi, Titel, Farbe.
            var dpi = VisualTreeHelper.GetDpi(this).DpiScaleY;
            var height = (int)Math.Clamp(Screen.ActualHeight * dpi, 180, 1080) / 2 * 2;
            var job = MovieWorker.Core.Export.ExportBuilder.BuildStill(project, _vm.PlayheadFrame, height);
            var png = await _vm.Tools.Require().RunForBytesAsync(job.Arguments, token);
            var image = Services.ThumbnailService.Decode(png);
            if (token.IsCancellationRequested || (IsPlaying && !_stillMode))
            {
                return false;
            }

            Still.Source = image;
            Still.Visibility = Visibility.Visible;
            Video.Visibility = Visibility.Hidden;
            Message.Text = image is null ? "Kein Bild." : string.Empty;
            StillsRendered++;
            StillRendered?.Invoke(this, EventArgs.Empty);
            return image is not null;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Message.Text = "Vorschau nicht möglich: " + ex.Message;
            return false;
        }
        finally
        {
            _stillBusy = false;
        }
    }

    // ------------------------------------------------------------------ Titel beim Abspielen

    /// <summary>
    /// Zeigt Titel während der Wiedergabe als Ebene über dem Video (Schrift, Größe, Lage, Kasten, Ein-/Ausblenden).
    /// Im Stillstand zeigt das ffmpeg-Standbild die Titel exakt wie im Export – dann ist die Ebene aus.
    /// </summary>
    private void UpdateTitleOverlay(MovieProject project, long frame, bool visible)
    {
        TitleOverlay.Children.Clear();
        if (!visible || _stillMode)
        {
            return;
        }

        var rate = project.Settings.FrameRate;
        var aspect = (double)project.Settings.Width / project.Settings.Height;
        var w = Screen.ActualWidth;
        var h = Screen.ActualHeight;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        // Bildfläche des Videos im Monitor (Stretch=Uniform)
        var (vw, vh) = w / h > aspect ? (h * aspect, h) : (w, w / aspect);
        var left = (w - vw) / 2;
        var top = (h - vh) / 2;
        var t = rate.FramesToSeconds(frame);
        foreach (var clip in project.TitleTracks.Where(tr => !tr.Muted).SelectMany(tr => tr.Clips)
                     .Where(c => c.Title is not null && c.Contains(frame)))
        {
            var spec = clip.Title!;
            var start = rate.FramesToSeconds(clip.StartFrame);
            var end = rate.FramesToSeconds(clip.EndFrame);
            var anim = Math.Clamp(spec.AnimationSeconds, 0.05, Math.Max(0.05, (end - start) / 2));
            var pIn = spec.AnimationIn == TitleAnimation.None ? 1 : Math.Clamp((t - start) / anim, 0, 1);
            var pOut = spec.AnimationOut == TitleAnimation.None ? 1 : Math.Clamp((end - t) / anim, 0, 1);
            var lines = spec.Lines;
            var sizes = lines.Select((_, i) => vh * spec.SizePercent / 100 * (i == 0 ? 1 : spec.SecondaryScale)).ToList();
            var block = sizes.Sum(s => s * 1.3);
            var y = spec.Style == TitleStyle.Credits
                ? vh - (t - start) / Math.Max(0.1, end - start) * (vh + block)
                : spec.PositionY / 100 * vh - block / 2 + (spec.AnimationIn == TitleAnimation.SlideUp ? (1 - pIn) * vh * 0.08 : 0);
            var dx = spec.AnimationIn == TitleAnimation.SlideLeft ? -(1 - pIn) * vw * 0.12 : 0;
            var typed = spec.AnimationIn == TitleAnimation.Typewriter
                ? (int)Math.Floor(Math.Clamp((t - start) / Math.Max(0.4, lines.Sum(l => l.Length) * 0.06), 0, 1) * lines.Sum(l => l.Length))
                : int.MaxValue;
            for (var i = 0; i < lines.Count; i++)
            {
                var text = typed == int.MaxValue ? lines[i] : lines[i][..Math.Clamp(typed, 0, lines[i].Length)];
                typed = typed == int.MaxValue ? typed : Math.Max(0, typed - lines[i].Length);
                var block1 = new TextBlock
                {
                    Text = text,
                    FontFamily = new FontFamily(spec.FontFamily),
                    FontWeight = spec.Bold ? FontWeights.Bold : FontWeights.Normal,
                    FontSize = Math.Max(4, sizes[i] * 0.75),
                    Foreground = ToBrush(spec.Color, Brushes.White),
                    Background = spec.Box ? ToBrush(spec.BoxColor, Brushes.Black, spec.BoxOpacity) : null,
                    Padding = spec.Box ? new Thickness(sizes[i] * 0.25, 0, sizes[i] * 0.25, 0) : default,
                    Opacity = pIn * pOut,
                };
                block1.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var x = spec.PositionX / 100 * vw + dx - spec.Align switch
                {
                    TitleAlign.Left => 0,
                    TitleAlign.Right => block1.DesiredSize.Width,
                    _ => block1.DesiredSize.Width / 2,
                };
                Canvas.SetLeft(block1, left + x);
                Canvas.SetTop(block1, top + y + (sizes[i] * 1.3 - block1.DesiredSize.Height) / 2);
                TitleOverlay.Children.Add(block1);
                y += sizes[i] * 1.3;
            }
        }
    }

    /// <summary>Zeigt den Umriss einer Farbmaske (gestrichelt) über dem Bild – oder blendet ihn aus.</summary>
    public void ShowMask(MovieWorker.Core.Color.ColorMask? mask, long frameInClip)
    {
        MaskOverlay.Children.Clear();
        if (mask is null || _vm is null)
        {
            return;
        }

        var settings = _vm.Project.Settings;
        var aspect = (double)settings.Width / settings.Height;
        var (w, h) = (Screen.ActualWidth, Screen.ActualHeight);
        if (w <= 0 || h <= 0)
        {
            return;
        }

        var (vw, vh) = w / h > aspect ? (h * aspect, h) : (w, w / aspect);
        var left = (w - vw) / 2;
        var top = (h - vh) / 2;
        var (cx, cy) = mask.CenterAt(frameInClip);
        System.Windows.Shapes.Shape shape = mask.Shape == MovieWorker.Core.Color.MaskShape.Ellipse
            ? new System.Windows.Shapes.Ellipse()
            : new System.Windows.Shapes.Rectangle();
        shape.Width = Math.Max(2, mask.Width * vw);
        shape.Height = Math.Max(2, mask.Height * vh);
        shape.Stroke = Brushes.Yellow;
        shape.StrokeThickness = 1.5;
        shape.StrokeDashArray = [4, 3];
        Canvas.SetLeft(shape, left + cx * vw - shape.Width / 2);
        Canvas.SetTop(shape, top + cy * vh - shape.Height / 2);
        MaskOverlay.Children.Add(shape);
    }

    private static Brush ToBrush(string color, Brush fallback, double opacity = 1)
    {
        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)) { Opacity = opacity };
            brush.Freeze();
            return brush;
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    private void UpdateTexts()
    {
        if (_vm is null)
        {
            return;
        }

        var settings = _vm.Project.Settings;
        TimecodeText.Text = Timecode.FromFrames(_vm.PlayheadFrame, settings.FrameRate, settings.DropFrameTimecode);
        PlayButton.Content = IsPlaying ? "❚❚" : "▶";
        ShuttleText.Text = IsPlaying && Math.Abs(_speed - 1) > 1e-6 ? $"{_speed:+0;-0}×" : string.Empty;
    }
}
