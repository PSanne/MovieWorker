using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MovieWorker.App.ViewModels;
using MovieWorker.Core.Projects;
using MovieWorker.Core.Time;
using MovieWorker.Core.Timeline;

namespace MovieWorker.App.Controls;

/// <summary>
/// Quellmonitor: zeigt die im Medienpool gewählte Aufnahme – oder als „Quellband“ alle Aufnahmen hintereinander –,
/// spielt ab, setzt In-/Out-Punkte und schneidet per Einfügen, Überschreiben, Ersetzen, Oben oder Anhängen.
/// Intern ist auch eine einzelne Aufnahme ein Band mit nur einem Abschnitt.
/// </summary>
public partial class SourceMonitor : UserControl
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly DispatcherTimer _stillDebounce = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private readonly Stopwatch _clock = new();
    private readonly HashSet<string> _failedPaths = new(StringComparer.OrdinalIgnoreCase);
    private MainViewModel? _vm;
    private SourceTape _tape = SourceTape.Build([]);
    private TapeSegment? _segment;
    private bool _playing;
    private bool _useStills;
    private bool _settingSlider;
    private double _clockStart;
    private string? _videoPath;
    private CancellationTokenSource? _stillCts;

    public SourceMonitor()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Tick();
        _stillDebounce.Tick += (_, _) =>
        {
            _stillDebounce.Stop();
            _ = RenderStillAsync();
        };
        SizeChanged += (_, _) => UpdateRange();
    }

    /// <summary>Position innerhalb der aktuellen Aufnahme (Sekunden).</summary>
    public double PositionSeconds => _tape.At(Position.Value)?.LocalSeconds ?? 0;

    /// <summary>Aktuelle Aufnahme unter dem Quell-Abspielkopf.</summary>
    public MediaItemViewModel? CurrentItem =>
        _segment is { } segment ? _vm?.MediaItems.FirstOrDefault(m => m.Model.Id == segment.Media.Id) : null;

    public bool IsTapeMode => TapeToggle.IsChecked == true;

    public void Attach(MainViewModel vm)
    {
        _vm = vm;
        vm.PropertyChanged += OnViewModelChanged;
        vm.MediaView.CollectionChanged += (_, _) =>
        {
            if (IsTapeMode)
            {
                RebuildTape(keepPosition: true);
            }
        };
        RebuildTape(keepPosition: false);
    }

    public void SetTapeMode(bool on)
    {
        if (TapeToggle.IsChecked == on && IsLoaded)
        {
            return;
        }

        TapeToggle.IsChecked = on;
        RebuildTape(keepPosition: true);
    }

    public void TogglePlay()
    {
        if (_segment is null)
        {
            return;
        }

        if (_playing)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    /// <summary>Springt innerhalb der aktuellen Aufnahme an eine Stelle.</summary>
    public void SetPosition(double localSeconds)
    {
        if (_segment is { } segment)
        {
            Position.Value = Math.Clamp(segment.Start + localSeconds, 0, Position.Maximum);
        }
    }

    public void MarkIn()
    {
        if (CurrentItem is { } item)
        {
            item.SourceIn = PositionSeconds;
            if (item.SourceOut is { } o && o <= PositionSeconds)
            {
                item.SourceOut = null;
            }

            UpdateRange();
        }
    }

    public void MarkOut()
    {
        if (CurrentItem is { } item)
        {
            item.SourceOut = PositionSeconds;
            if (item.SourceIn is { } i && i >= PositionSeconds)
            {
                item.SourceIn = null;
            }

            UpdateRange();
        }
    }

    /// <summary>Schneidet den Ausschnitt der aktuellen Aufnahme nach Modus in die Timeline.</summary>
    public TimelineClip? Edit(EditMode mode)
    {
        if (_vm is null || CurrentItem is not { } item)
        {
            return null;
        }

        Pause();
        return _vm.PlaceFromSource(mode, item.Model, item.SourceIn ?? 0, item.SourceOut);
    }

    // ------------------------------------------------------------------ Band aufbauen

    private void RebuildTape(bool keepPosition)
    {
        if (_vm is null)
        {
            return;
        }

        var current = CurrentItem;
        var local = PositionSeconds;
        Pause();
        if (IsTapeMode)
        {
            _tape = SourceTape.Build(_vm.MediaView.Cast<MediaItemViewModel>().Where(m => !m.IsAudioOnly).Select(m => m.Model));
        }
        else
        {
            _tape = SourceTape.Build(_vm.SelectedMedia is { } selected ? [selected.Model] : []);
            current = _vm.SelectedMedia;
            local = current?.SourceIn ?? 0;
            keepPosition = true;
        }

        _settingSlider = true;
        Position.Maximum = Math.Max(0.1, _tape.Duration);
        Position.Value = keepPosition && current is not null && _tape.TapeTime(current.Model, local) is { } t ? t : 0;
        _settingSlider = false;
        _segment = null;
        OnPositionMoved();
        if (_tape.Segments.Count == 0)
        {
            Still.Source = null;
            Video.Visibility = Visibility.Hidden;
            TitleText.Text = IsTapeMode ? "Quellband (leer)" : "Quelle";
            Message.Text = IsTapeMode ? "Keine Videoaufnahmen im Medienpool (Filter prüfen)." : "Wähle eine Aufnahme im Medienpool.";
        }
    }

    // ------------------------------------------------------------------ Wiedergabe

    private void Play()
    {
        if (Position.Value >= Position.Maximum - 0.05)
        {
            Position.Value = 0;
        }

        _playing = true;
        _clockStart = Position.Value;
        _clock.Restart();
        _videoPath = null;
        LoadSegmentPlayer();
        _timer.Start();
        UpdateTexts();
    }

    private void Pause()
    {
        if (!_playing)
        {
            return;
        }

        _playing = false;
        _timer.Stop();
        Video.Pause();
        UpdateTexts();
        RequestStill();
    }

    private void Tick()
    {
        var tapeTime = _clockStart + _clock.Elapsed.TotalSeconds;
        var stopAt = CurrentItem?.SourceOut is { } o && _segment is { } seg ? seg.Start + o : (double?)null;
        if (stopAt is { } stop && tapeTime >= stop && _clockStart < stop)
        {
            SetSlider(stop);
            Pause();
            return;
        }

        if (tapeTime >= Position.Maximum)
        {
            SetSlider(Position.Maximum);
            Pause();
            return;
        }

        var previous = _segment;
        SetSlider(tapeTime);
        if (_segment != previous)
        {
            LoadSegmentPlayer();
        }
        else if (_useStills)
        {
            RequestStill();
        }
        else if (Video.NaturalDuration.HasTimeSpan && Math.Abs(Video.Position.TotalSeconds - PlayerSeconds()) > 0.3)
        {
            Video.Position = TimeSpan.FromSeconds(PlayerSeconds());
        }
    }

    private void SetSlider(double tapeTime)
    {
        _settingSlider = true;
        Position.Value = tapeTime;
        _settingSlider = false;
        OnPositionMoved();
    }

    /// <summary>Startet den Player für die Aufnahme unter dem Abspielkopf (Proxy, wenn vorhanden).</summary>
    private void LoadSegmentPlayer()
    {
        if (_segment is not { } segment)
        {
            return;
        }

        var media = segment.Media;
        var path = media.HasProxy && MediaItem.UseProxies ? media.ProxyPath! : media.Parts.Count == 1 ? media.PrimaryPath : null;
        _useStills = path is null || _failedPaths.Contains(path);
        if (_useStills)
        {
            Video.Pause();
            Video.Visibility = Visibility.Hidden;
            RequestStill();
            return;
        }

        if (!string.Equals(_videoPath, path, StringComparison.OrdinalIgnoreCase))
        {
            _videoPath = path;
            Video.Source = new Uri(path!);
        }

        Video.Position = TimeSpan.FromSeconds(PlayerSeconds());
        Video.Play();
        Video.Visibility = media.IsAudioOnly ? Visibility.Hidden : Visibility.Visible;
        Still.Visibility = Visibility.Collapsed;
    }

    private double PlayerSeconds() => PositionSeconds;

    private void OnMediaOpened(object sender, RoutedEventArgs e)
    {
        if (_playing && !_useStills)
        {
            Video.Position = TimeSpan.FromSeconds(PlayerSeconds());
        }
    }

    private void OnMediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        // Ohne passenden Windows-Decoder (z. B. HEVC 4:2:2): Standbilder im Takt der Uhr.
        if (_videoPath is not null)
        {
            _failedPaths.Add(_videoPath);
        }

        _videoPath = null;
        Video.Visibility = Visibility.Hidden;
        _useStills = true;
    }

    private void OnMediaEnded(object sender, RoutedEventArgs e)
    {
        if (!IsTapeMode)
        {
            Pause();
        }
    }

    // ------------------------------------------------------------------ Position & Standbild

    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_settingSlider)
        {
            return;
        }

        var previous = _segment;
        OnPositionMoved();
        if (_playing)
        {
            _clockStart = Position.Value;
            _clock.Restart();
            if (_segment != previous)
            {
                LoadSegmentPlayer();
            }
            else if (!_useStills)
            {
                Video.Position = TimeSpan.FromSeconds(PlayerSeconds());
            }
        }
        else
        {
            RequestStill();
        }
    }

    private void OnPositionMoved()
    {
        var at = _tape.At(Position.Value);
        var segment = at?.Segment;
        if (segment != _segment)
        {
            _segment = segment;
            if (segment is not null)
            {
                var index = _tape.Segments.ToList().IndexOf(segment) + 1;
                TitleText.Text = IsTapeMode
                    ? $"Quellband {index}/{_tape.Segments.Count}: {segment.Media.Name}"
                    : "Quelle: " + segment.Media.Name;
                Message.Text = segment.Media.IsAudioOnly ? "♪ " + segment.Media.Name : string.Empty;
            }

            UpdateRange();
        }

        UpdateTexts();
    }

    private void RequestStill()
    {
        _stillDebounce.Stop();
        _stillDebounce.Start();
    }

    private async Task RenderStillAsync()
    {
        if (_vm is null || _segment is not { } segment || segment.Media.IsAudioOnly)
        {
            return;
        }

        _stillCts?.Cancel();
        _stillCts = new CancellationTokenSource();
        var token = _stillCts.Token;
        var (path, seconds) = PartAt(segment.Media, PositionSeconds);
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this).DpiScaleY;
            var height = (int)Math.Clamp(Screen.ActualHeight * dpi, 180, 1080);
            var image = await _vm.Thumbnails.RenderAsync(path, seconds, height, null, token);
            if (token.IsCancellationRequested || (_playing && !_useStills))
            {
                return;
            }

            Still.Source = image;
            Still.Visibility = Visibility.Visible;
            Video.Visibility = Visibility.Hidden;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Message.Text = "Vorschau nicht möglich: " + ex.Message;
        }
    }

    private static (string Path, double Seconds) PartAt(MediaItem media, double seconds)
    {
        double start = 0;
        for (var i = 0; i < media.Parts.Count; i++)
        {
            var length = media.Parts[i].Duration.TotalSeconds;
            if (seconds < start + length || i == media.Parts.Count - 1)
            {
                return (media.Parts[i].Path, seconds - start);
            }

            start += length;
        }

        return (media.PrimaryPath, seconds);
    }

    // ------------------------------------------------------------------ Bedienung

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.SelectedMedia) || _vm?.SelectedMedia is not { } selected)
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedMedia) && !IsTapeMode)
            {
                RebuildTape(keepPosition: false);
            }

            return;
        }

        if (IsTapeMode && _tape.TapeTime(selected.Model, selected.SourceIn ?? 0) is { } t)
        {
            // Im Quellband zur gewählten Aufnahme springen.
            Position.Value = t;
        }
        else if (!IsTapeMode)
        {
            RebuildTape(keepPosition: false);
        }
    }

    private void OnTapeToggle(object sender, RoutedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.SourceTapeMode = TapeToggle.IsChecked == true;
        }

        RebuildTape(keepPosition: true);
    }

    private void OnPlay(object sender, RoutedEventArgs e) => TogglePlay();

    private void OnMarkIn(object sender, RoutedEventArgs e) => MarkIn();

    private void OnMarkOut(object sender, RoutedEventArgs e) => MarkOut();

    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (CurrentItem is { } item)
        {
            item.SourceIn = null;
            item.SourceOut = null;
            UpdateRange();
        }
    }

    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<EditMode>(tag, out var mode))
        {
            Edit(mode);
        }
    }

    private void UpdateTexts()
    {
        var local = PositionSeconds;
        TimecodeText.Text = Timecode.FormatDuration(TimeSpan.FromSeconds(local)) + "." + ((int)(local * 10 % 10)).ToString();
        PlayButton.Content = _playing ? "❚❚" : "▶";
    }

    private void UpdateRange()
    {
        if (_segment is not { } segment || CurrentItem is not { } item || Position.ActualWidth <= 0 || Position.Maximum <= 0)
        {
            RangeBar.Width = 0;
            RangeText.Text = string.Empty;
            return;
        }

        var start = item.SourceIn ?? 0;
        var end = item.SourceOut ?? segment.Duration;
        var scale = Position.ActualWidth / Position.Maximum;
        Canvas.SetLeft(RangeBar, (segment.Start + start) * scale);
        RangeBar.Width = Math.Max(0, (end - start) * scale);
        RangeText.Text = item.SourceIn is null && item.SourceOut is null
            ? string.Empty
            : $"Ausschnitt: {Timecode.FormatDuration(TimeSpan.FromSeconds(end - start))}";
    }
}
