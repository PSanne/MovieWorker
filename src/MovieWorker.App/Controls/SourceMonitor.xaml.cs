using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MovieWorker.App.ViewModels;
using MovieWorker.Core.Projects;
using MovieWorker.Core.Time;

namespace MovieWorker.App.Controls;

/// <summary>
/// Quellmonitor: zeigt die im Medienpool gewählte Aufnahme, spielt sie ab und setzt In-/Out-Punkte.
/// Der gewählte Ausschnitt wird ans Ende der Timeline oder am Abspielkopf eingefügt.
/// </summary>
public partial class SourceMonitor : UserControl
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly DispatcherTimer _stillDebounce = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private readonly Stopwatch _clock = new();
    private MainViewModel? _vm;
    private MediaItemViewModel? _item;
    private bool _playing;
    private bool _useStills;
    private bool _settingSlider;
    private double _clockStart;
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

    public double PositionSeconds => Position.Value;

    public void Attach(MainViewModel vm)
    {
        _vm = vm;
        vm.PropertyChanged += OnViewModelChanged;
        Load(vm.SelectedMedia);
    }

    public void TogglePlay()
    {
        if (_item is null)
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

    public void SetPosition(double seconds) => Position.Value = Math.Clamp(seconds, 0, Position.Maximum);

    public void MarkIn()
    {
        if (_item is not null)
        {
            _item.SourceIn = Position.Value;
            if (_item.SourceOut is { } o && o <= Position.Value)
            {
                _item.SourceOut = null;
            }

            UpdateRange();
        }
    }

    public void MarkOut()
    {
        if (_item is not null)
        {
            _item.SourceOut = Position.Value;
            if (_item.SourceIn is { } i && i >= Position.Value)
            {
                _item.SourceIn = null;
            }

            UpdateRange();
        }
    }

    private void Load(MediaItemViewModel? item)
    {
        Pause();
        _item = item;
        _useStills = false;
        Video.Source = null;
        Video.Visibility = Visibility.Hidden;
        Still.Source = null;
        if (item is null)
        {
            TitleText.Text = "Quelle";
            Message.Text = "Wähle eine Aufnahme im Medienpool.";
            Position.Maximum = 1;
            UpdateRange();
            return;
        }

        TitleText.Text = "Quelle: " + item.Name;
        Message.Text = item.IsAudioOnly ? "♪ " + item.Name : string.Empty;
        _settingSlider = true;
        Position.Maximum = Math.Max(0.1, item.Model.Duration.TotalSeconds);
        Position.Value = item.SourceIn ?? 0;
        _settingSlider = false;
        UpdateTexts();
        UpdateRange();
        if (!item.IsAudioOnly)
        {
            _ = RenderStillAsync();
        }
    }

    private void Play()
    {
        if (_item is null)
        {
            return;
        }

        if (Position.Value >= Position.Maximum - 0.05)
        {
            Position.Value = _item.SourceIn ?? 0;
        }

        _playing = true;
        _clockStart = Position.Value;
        _clock.Restart();
        var canPlay = _item.Model.HasProxy && MediaItem.UseProxies || _item.Model.Parts.Count == 1;
        _useStills = !canPlay;
        if (!_useStills)
        {
            var path = _item.Model.HasProxy && MediaItem.UseProxies ? _item.Model.ProxyPath! : _item.Model.PrimaryPath;
            if (Video.Source?.LocalPath != new Uri(path).LocalPath)
            {
                Video.Source = new Uri(path);
            }

            Video.Position = TimeSpan.FromSeconds(Position.Value);
            Video.Play();
            Video.Visibility = _item.IsAudioOnly ? Visibility.Hidden : Visibility.Visible;
            Still.Visibility = Visibility.Collapsed;
        }

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
        if (_item is { IsAudioOnly: false })
        {
            _ = RenderStillAsync();
        }
    }

    private void Tick()
    {
        var seconds = _clockStart + _clock.Elapsed.TotalSeconds;
        if (!_useStills && Video.NaturalDuration.HasTimeSpan)
        {
            seconds = Video.Position.TotalSeconds;
        }

        var stop = _item?.SourceOut is { } o && seconds >= o && _clockStart < o;
        if (seconds >= Position.Maximum || stop)
        {
            seconds = stop ? _item!.SourceOut!.Value : Position.Maximum;
            _settingSlider = true;
            Position.Value = seconds;
            _settingSlider = false;
            Pause();
            return;
        }

        _settingSlider = true;
        Position.Value = seconds;
        _settingSlider = false;
        UpdateTexts();
        if (_useStills)
        {
            _stillDebounce.Stop();
            _stillDebounce.Start();
        }
    }

    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateTexts();
        if (_settingSlider)
        {
            return;
        }

        if (_playing)
        {
            _clockStart = Position.Value;
            _clock.Restart();
            if (!_useStills)
            {
                Video.Position = TimeSpan.FromSeconds(Position.Value);
            }
        }
        else if (_item is { IsAudioOnly: false })
        {
            _stillDebounce.Stop();
            _stillDebounce.Start();
        }
    }

    private async Task RenderStillAsync()
    {
        if (_vm is null || _item is null || _item.IsAudioOnly)
        {
            return;
        }

        _stillCts?.Cancel();
        _stillCts = new CancellationTokenSource();
        var token = _stillCts.Token;
        var (path, seconds) = PartAt(_item.Model, Position.Value);
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

    private void OnMediaOpened(object sender, RoutedEventArgs e)
    {
        if (_playing)
        {
            Video.Position = TimeSpan.FromSeconds(Position.Value);
        }
    }

    private void OnMediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        // Ohne passenden Windows-Decoder (z. B. HEVC 4:2:2): Standbilder im Takt der Uhr.
        Video.Visibility = Visibility.Hidden;
        _useStills = true;
        _clockStart = Position.Value;
        _clock.Restart();
        Message.Text = _item?.IsAudioOnly == true ? Message.Text : string.Empty;
    }

    private void OnMediaEnded(object sender, RoutedEventArgs e) => Pause();

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedMedia))
        {
            Load(_vm!.SelectedMedia);
        }
    }

    private void OnPlay(object sender, RoutedEventArgs e) => TogglePlay();

    private void OnMarkIn(object sender, RoutedEventArgs e) => MarkIn();

    private void OnMarkOut(object sender, RoutedEventArgs e) => MarkOut();

    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (_item is not null)
        {
            _item.SourceIn = null;
            _item.SourceOut = null;
            UpdateRange();
        }
    }

    private void OnAppend(object sender, RoutedEventArgs e)
    {
        if (_vm is not null && _item is not null)
        {
            _vm.AddToTimeline(_item.Model, _item.SourceIn ?? 0, _item.SourceOut);
        }
    }

    private void OnInsert(object sender, RoutedEventArgs e)
    {
        if (_vm is not null && _item is not null)
        {
            _vm.InsertRangeAtPlayhead(_item.Model, _item.SourceIn ?? 0, _item.SourceOut);
        }
    }

    private void UpdateTexts()
    {
        TimecodeText.Text = Timecode.FormatDuration(TimeSpan.FromSeconds(Position.Value)) + "." + ((int)(Position.Value * 10 % 10)).ToString();
        PlayButton.Content = _playing ? "❚❚" : "▶";
    }

    private void UpdateRange()
    {
        if (_item is null || Position.ActualWidth <= 0)
        {
            RangeBar.Width = 0;
            RangeText.Text = string.Empty;
            return;
        }

        var start = _item.SourceIn ?? 0;
        var end = _item.SourceOut ?? Position.Maximum;
        var scale = Position.ActualWidth / Position.Maximum;
        Canvas.SetLeft(RangeBar, start * scale);
        RangeBar.Width = Math.Max(0, (end - start) * scale);
        RangeText.Text = _item.SourceIn is null && _item.SourceOut is null
            ? string.Empty
            : $"Ausschnitt: {Timecode.FormatDuration(TimeSpan.FromSeconds(end - start))}";
    }
}
