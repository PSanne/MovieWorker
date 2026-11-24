using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MovieWorker.App.Controls;
using MovieWorker.App.ViewModels;
using MovieWorker.Core.Timeline;

namespace MovieWorker.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _allowClose;
    private Point _mediaDragStart;
    private bool _firstLayout = true;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.ProjectChanged += (_, _) => OnProjectChanged();
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.MediaItems.CollectionChanged += (_, _) => UpdateEmptyHint();
        Drop += OnDrop;
        DragOver += OnDragOver;
        PreviewKeyDown += OnPreviewKeyDown;

        Timeline.ThumbnailProvider = ThumbnailFor;
        Timeline.WaveformProvider = WaveformFor;
        Timeline.ClipMoveRequested += (_, e) => _viewModel.MoveClip(e.ClipId, e.NewStart, e.TrackId);
        Timeline.ClipTrimRequested += (_, e) => _viewModel.TrimClip(e.ClipId, e.AtStart, e.Delta);
        Timeline.ClipBodyEditRequested += (_, e) => _viewModel.TrimEdit(_viewModel.TrimMode, e.ClipId, false, e.Delta);
        Timeline.MediaDropped += (_, e) => _viewModel.InsertMediaAt(e.MediaId, e.TrackId, e.Frame);
        Timeline.TrackMuteToggled += (_, id) => _viewModel.ToggleTrackMute(id);
        Timeline.ClipDoubleClicked += (_, _) => ClipTab.IsSelected = true;
        Timeline.ViewChanged += (_, _) => SyncScrollBar();
        OverviewTimeline.ClipMoveRequested += (_, e) => _viewModel.MoveClip(e.ClipId, e.NewStart, e.TrackId);
        OverviewTimeline.ClipTrimRequested += (_, e) => _viewModel.TrimClip(e.ClipId, e.AtStart, e.Delta);
        OverviewTimeline.MediaDropped += (_, e) => _viewModel.InsertMediaAt(e.MediaId, e.TrackId, e.Frame);
        OverviewTimeline.SizeChanged += (_, _) => OverviewTimeline.ZoomToFit();
        Loaded += (_, _) => SyncScrollBar();
        SourceMonitor.Attach(viewModel);
        ProgramMonitor.StillRendered += (_, _) =>
        {
            if (ColorTab.IsSelected)
            {
                _ = _viewModel.UpdateScopesAsync();
            }
        };
        viewModel.ExportQueue.Jobs.CollectionChanged += (_, _) =>
        {
            EmptyQueueHint.Visibility = viewModel.ExportQueue.Jobs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (viewModel.ExportQueue.Jobs.Count > 0)
            {
                ExportTab.IsSelected = true;
            }
        };
        ProgramMonitor.Attach(viewModel);

        UpdateEmptyHint();
    }

    public TimelineView TimelineControl => Timeline;

    public ProgramMonitor ProgramMonitorControl => ProgramMonitor;

    public SourceMonitor SourceMonitorControl => SourceMonitor;

    private void OnPlayPause(object sender, RoutedEventArgs e) => ProgramMonitor.TogglePlay();

    private void OnEditMenu(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<EditMode>(tag, out var mode))
        {
            SourceMonitor.Edit(mode);
        }
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_allowClose || !_viewModel.IsDirty)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        if (await _viewModel.ConfirmDiscardAsync())
        {
            _allowClose = true;

            // Nicht innerhalb des laufenden Schließvorgangs erneut schließen.
            _ = Dispatcher.BeginInvoke(Close);
        }
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    private void OnRecentClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is MenuItem { DataContext: string path })
        {
            _viewModel.OpenRecentCommand.Execute(path);
        }
    }

    // ------------------------------------------------------------------ Tastatur

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // In Eingabefeldern gelten die normalen Tasten.
        if (Keyboard.FocusedElement is TextBox or ComboBox or ComboBoxItem or Slider or PasswordBox)
        {
            return;
        }

        var listFocused = Keyboard.FocusedElement is ListBox or ListBoxItem;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var handled = true;
        var sourceFocused = SourceMonitor.IsKeyboardFocusWithin;
        switch (e.Key)
        {
            case Key.Space:
                if (sourceFocused) SourceMonitor.TogglePlay(); else ProgramMonitor.TogglePlay();
                break;
            case >= Key.D1 and <= Key.D9 when !ctrl && !shift:
                _viewModel.SwitchAngle(e.Key - Key.D1);
                break;
            case >= Key.NumPad1 and <= Key.NumPad9 when !ctrl:
                _viewModel.SwitchAngle(e.Key - Key.NumPad1);
                break;
            case Key.F9:
                SourceMonitor.Edit(EditMode.Insert);
                break;
            case Key.F10:
                SourceMonitor.Edit(EditMode.Overwrite);
                break;
            case Key.F11:
                SourceMonitor.Edit(EditMode.Replace);
                break;
            case Key.F12:
                SourceMonitor.Edit(shift ? EditMode.Append : EditMode.PlaceOnTop);
                break;
            case Key.System when e.SystemKey == Key.F10:
                SourceMonitor.Edit(EditMode.Overwrite);
                break;
            case Key.J when !ctrl:
                ProgramMonitor.Shuttle(-1);
                break;
            case Key.K when !ctrl:
                ProgramMonitor.Shuttle(0);
                break;
            case Key.L when !ctrl:
                ProgramMonitor.Shuttle(1);
                break;
            case Key.Left when !listFocused:
                if (shift) _viewModel.StepSeconds(-1); else _viewModel.StepFrames(-1);
                break;
            case Key.Right when !listFocused:
                if (shift) _viewModel.StepSeconds(1); else _viewModel.StepFrames(1);
                break;
            case Key.Up when !listFocused:
                _viewModel.PreviousEdit();
                break;
            case Key.Down when !listFocused:
                _viewModel.NextEdit();
                break;
            case Key.Home when !listFocused:
                _viewModel.GoToStart();
                break;
            case Key.End when !listFocused:
                _viewModel.GoToEnd();
                break;
            case Key.B or Key.K when ctrl:
                _viewModel.SplitAtPlayhead();
                break;
            case Key.Delete or Key.Back when !listFocused:
                if (shift) _viewModel.RippleDeleteSelectedClip(); else _viewModel.DeleteSelectedClip();
                break;
            case Key.I when !ctrl:
                if (sourceFocused) SourceMonitor.MarkIn(); else _viewModel.MarkIn();
                break;
            case Key.O when !ctrl:
                if (sourceFocused) SourceMonitor.MarkOut(); else _viewModel.MarkOut();
                break;
            case Key.X when ctrl && shift:
                _viewModel.ClearMarks();
                break;
            case Key.A when !ctrl && !shift:
                _viewModel.TrimMode = TrimMode.Normal;
                break;
            case Key.B when !ctrl && !shift:
                _viewModel.TrimMode = TrimMode.Ripple;
                break;
            case Key.R when !ctrl && !shift:
                _viewModel.TrimMode = TrimMode.Roll;
                break;
            case Key.Y when !ctrl && !shift:
                _viewModel.TrimMode = TrimMode.Slip;
                break;
            case Key.U when !ctrl && !shift:
                _viewModel.TrimMode = TrimMode.Slide;
                break;
            case Key.N when !ctrl:
                _viewModel.Snapping = !_viewModel.Snapping;
                _viewModel.StatusText = _viewModel.Snapping ? "Einrasten an." : "Einrasten aus.";
                break;
            case Key.OemPlus or Key.Add when !ctrl:
                Timeline.ZoomBy(1.5);
                break;
            case Key.OemMinus or Key.Subtract when !ctrl:
                Timeline.ZoomBy(1 / 1.5);
                break;
            case Key.Z when shift && !ctrl:
                Timeline.ZoomToFit();
                break;
            default:
                handled = false;
                break;
        }

        if (handled)
        {
            e.Handled = true;
            if (e.Key is not (Key.Space or Key.J or Key.K or Key.L or (>= Key.D1 and <= Key.D9) or (>= Key.NumPad1 and <= Key.NumPad9))
                && ProgramMonitor.IsPlaying)
            {
                ProgramMonitor.Pause();
            }

            Timeline.FollowPlayhead();
        }
    }

    // ------------------------------------------------------------------ Timeline

    private void OnProjectChanged()
    {
        Timeline.InvalidateMeasure();
        Timeline.InvalidateVisual();
        OverviewTimeline.InvalidateMeasure();
        if (_viewModel.ShowOverview)
        {
            OverviewTimeline.ZoomToFit();
        }
        if (_firstLayout && _viewModel.Project.AllClips.Any())
        {
            _firstLayout = false;
            Dispatcher.BeginInvoke(() => Timeline.ZoomToFit());
        }

        if (!_viewModel.Project.AllClips.Any())
        {
            _firstLayout = true;
        }

        SyncScrollBar();
    }

    private void SyncScrollBar()
    {
        var viewport = Timeline.Viewport;
        var total = Math.Max(_viewModel.Project.DurationFrames, 1) * 1.1;
        TimelineScroll.Minimum = 0;
        TimelineScroll.Maximum = Math.Max(0, total - viewport.VisibleFrames);
        TimelineScroll.ViewportSize = viewport.VisibleFrames;
        TimelineScroll.LargeChange = viewport.VisibleFrames * 0.8;
        TimelineScroll.SmallChange = viewport.VisibleFrames * 0.05;
        TimelineScroll.Value = Math.Min(viewport.ScrollFrame, TimelineScroll.Maximum);
    }

    private void OnTimelineScroll(object sender, ScrollEventArgs e) => Timeline.ScrollTo(e.NewValue);

    private void OnZoomIn(object sender, RoutedEventArgs e) => Timeline.ZoomBy(1.5);

    private void OnZoomOut(object sender, RoutedEventArgs e) => Timeline.ZoomBy(1 / 1.5);

    private void OnZoomFit(object sender, RoutedEventArgs e) => Timeline.ZoomToFit();

    private ImageSource? ThumbnailFor(TimelineClip clip)
    {
        if (clip.IsMulticam)
        {
            var angle = clip.MulticamAngles![Math.Clamp(clip.ActiveAngle, 0, clip.MulticamAngles.Count - 1)];
            clip = new TimelineClip { MediaId = angle.MediaId, SourcePaths = angle.SourcePaths, SourceIn = clip.SourceIn + angle.OffsetSeconds };
        }

        var media = _viewModel.Project.FindMedia(clip.MediaId);
        var path = media?.PreviewPath ?? clip.PrimaryPath;
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        // Für den Vorschaubild-Zwischenspeicher auf halbe Sekunden runden.
        var seconds = Math.Round(clip.SourceIn * 2) / 2 + 0.1;
        var task = _viewModel.Thumbnails.GetAsync(path, seconds, 64);
        if (task.IsCompletedSuccessfully)
        {
            return task.Result;
        }

        task.ContinueWith(_ => Timeline.InvalidateVisual(), TaskScheduler.FromCurrentSynchronizationContext());
        return null;
    }

    private Core.Media.WaveformData? WaveformFor(TimelineClip clip) =>
        _viewModel.Waveforms.TryGet(clip.SourcePaths, () => Timeline.InvalidateVisual());

    // ------------------------------------------------------------------ Medienpool

    private void OnMediaDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.AddSelectedToTimelineCommand.CanExecute(null))
        {
            _viewModel.AddSelectedToTimelineCommand.Execute(null);
        }
    }

    private void OnMediaMouseDown(object sender, MouseButtonEventArgs e) => _mediaDragStart = e.GetPosition(null);

    private void OnMediaSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        _viewModel.SelectedMediaItems = MediaList.SelectedItems.OfType<MediaItemViewModel>().ToList();

    private void OnMediaMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _viewModel.SelectedMedia is not { } media)
        {
            return;
        }

        var delta = e.GetPosition(null) - _mediaDragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var data = new DataObject(TimelineView.MediaDragFormat, media.Model.Id.ToString());
        DragDrop.DoDragDrop(MediaList, data, DragDropEffects.Copy);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Handled || e.Data.GetData(DataFormats.FileDrop) is not string[] files)
        {
            return;
        }

        var projects = files.Where(f => f.EndsWith(".mvproj", StringComparison.OrdinalIgnoreCase)).ToList();
        if (projects.Count == 1 && files.Length == 1)
        {
            if (await _viewModel.ConfirmDiscardAsync())
            {
                await _viewModel.OpenProjectFileAsync(projects[0]);
            }

            return;
        }

        await _viewModel.ImportPathsAsync(files);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.SelectedClip) when _viewModel.SelectedClip is not null:
                if (!ColorTab.IsSelected)
                {
                    ClipTab.IsSelected = true;
                }

                break;
            case nameof(MainViewModel.PlayheadFrame):
                Timeline.FollowPlayhead();
                break;
            case nameof(MainViewModel.SourceTapeMode):
                SourceMonitor.SetTapeMode(_viewModel.SourceTapeMode);
                break;
            case nameof(MainViewModel.ShowOverview) when _viewModel.ShowOverview:
                Dispatcher.BeginInvoke(() => OverviewTimeline.ZoomToFit());
                break;
        }
    }

    private void UpdateEmptyHint() =>
        EmptyPoolHint.Visibility = _viewModel.MediaItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
}
