using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using MovieWorker.App.ViewModels;
using MovieWorker.Core.Time;

namespace MovieWorker.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _allowClose;
    private CancellationTokenSource? _previewCts;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.ProjectChanged += (_, _) => RefreshTimelineList();
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.MediaItems.CollectionChanged += (_, _) => UpdateEmptyHint();
        Drop += OnDrop;
        DragOver += OnDragOver;
        UpdateEmptyHint();
        RefreshTimelineList();
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
            Close();
        }
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    private void OnRecentClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.MenuItem { DataContext: string path })
        {
            _viewModel.OpenRecentCommand.Execute(path);
        }
    }

    private void OnMediaDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.AddSelectedToTimelineCommand.CanExecute(null))
        {
            _viewModel.AddSelectedToTimelineCommand.Execute(null);
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
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
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedMedia))
        {
            _ = ShowPreviewAsync();
        }
    }

    private async Task ShowPreviewAsync()
    {
        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        var token = _previewCts.Token;
        PreviewImage.Source = null;
        var media = _viewModel.SelectedMedia;
        PreviewHint.Text = media is null ? "Wähle eine Aufnahme im Medienpool." : media.IsAudioOnly ? "♪ Audiodatei" : "Vorschau wird geladen …";
        PreviewHint.Visibility = Visibility.Visible;
        if (media is null || media.IsAudioOnly)
        {
            return;
        }

        try
        {
            var seconds = Math.Min(1.0, media.Model.Duration.TotalSeconds / 2);
            var image = await _viewModel.Thumbnails.RenderAsync(media.Model.PreviewPath, seconds, 540, null, token);
            if (!token.IsCancellationRequested)
            {
                PreviewImage.Source = image;
                PreviewHint.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
                PreviewHint.Text = image is null ? "Keine Vorschau möglich." : string.Empty;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            PreviewHint.Text = "Keine Vorschau möglich.";
        }
    }

    private void UpdateEmptyHint() =>
        EmptyPoolHint.Visibility = _viewModel.MediaItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void RefreshTimelineList()
    {
        var project = _viewModel.Project;
        var rate = project.Settings.FrameRate;
        SimpleTimeline.ItemsSource = project.Tracks
            .SelectMany(t => t.OrderedClips.Select(c =>
                $"{t.Name}   {Timecode.FromFrames(c.StartFrame, rate)} – {Timecode.FromFrames(c.EndFrame, rate)}   {c.Name}"))
            .ToList();
    }
}
