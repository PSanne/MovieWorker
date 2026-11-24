using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MovieWorker.App.Services;
using MovieWorker.Core.Camera;

namespace MovieWorker.App.ViewModels;

public sealed record CardFile(string Path, string Name, string SizeText, long Size);

/// <summary>
/// Dialog „Speicherkarte importieren“: Karte wählen, Zielordner, kopieren mit Prüfsumme.
/// Nach „OK“ stehen in <see cref="ResultFiles"/> die Dateien, die in den Medienpool sollen.
/// </summary>
public sealed partial class ImportCardViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly SettingsService _settings;
    private CancellationTokenSource? _cts;

    public ImportCardViewModel(IDialogService dialogs, SettingsService settings)
    {
        _dialogs = dialogs;
        _settings = settings;
        DestinationFolder = settings.Settings.ImportFolder
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "MovieWorker");
        CopyFiles = settings.Settings.ImportCopyFiles;
        VerifyChecksums = settings.Settings.ImportVerifyChecksums;
        foreach (var drive in DetectCards())
        {
            Cards.Add(drive);
        }

        SourceFolder = Cards.FirstOrDefault();
    }

    public ObservableCollection<string> Cards { get; } = [];

    public ObservableCollection<CardFile> Files { get; } = [];

    public IReadOnlyList<string> ResultFiles { get; private set; } = [];

    [ObservableProperty]
    private string? _sourceFolder;

    [ObservableProperty]
    private string _destinationFolder;

    [ObservableProperty]
    private string _cameraFolder = "Kamera";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DestinationEnabled))]
    private bool _copyFiles;

    [ObservableProperty]
    private bool _verifyChecksums;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>Wird ausgelöst, wenn der Import fertig ist (true) oder abgebrochen wurde (false).</summary>
    public event EventHandler<bool>? Finished;

    public bool IsIdle => !IsBusy;

    public bool DestinationEnabled => CopyFiles;

    public string SummaryText => Files.Count == 0
        ? "Keine Videos gefunden."
        : $"{Files.Count} Videos · {MediaItemViewModel.FormatSize(Files.Sum(f => f.Size))}";

    partial void OnSourceFolderChanged(string? value) => ScanSource();

    [RelayCommand]
    private void BrowseSource()
    {
        var folder = _dialogs.PickFolder("Speicherkarte oder DCIM-Ordner wählen", SourceFolder);
        if (folder is not null)
        {
            if (!Cards.Contains(folder))
            {
                Cards.Add(folder);
            }

            SourceFolder = folder;
        }
    }

    [RelayCommand]
    private void BrowseDestination()
    {
        var folder = _dialogs.PickFolder("Zielordner für die Videos wählen", DestinationFolder);
        if (folder is not null)
        {
            DestinationFolder = folder;
        }
    }

    [RelayCommand]
    private void RefreshCards()
    {
        Cards.Clear();
        foreach (var card in DetectCards())
        {
            Cards.Add(card);
        }

        SourceFolder = Cards.FirstOrDefault();
    }

    private bool CanStart() => !IsBusy && Files.Count > 0;

    [RelayCommand(CanExecute = nameof(CanStart))]
    public async Task StartAsync()
    {
        _settings.Settings.ImportCopyFiles = CopyFiles;
        _settings.Settings.ImportVerifyChecksums = VerifyChecksums;
        if (!CopyFiles)
        {
            ResultFiles = Files.Select(f => f.Path).ToList();
            _settings.Save();
            Finished?.Invoke(this, true);
            return;
        }

        _settings.Settings.ImportFolder = DestinationFolder;
        _settings.Save();
        IsBusy = true;
        _cts = new CancellationTokenSource();
        try
        {
            var plan = CardImporter.Plan(Files.Select(f => f.Path), DestinationFolder, DateTime.Today, CameraFolder);
            var progress = new Progress<CopyProgress>(p =>
            {
                Progress = p.BytesTotal == 0 ? 1 : (double)p.BytesDone / p.BytesTotal;
                StatusText = $"{p.Stage}: {p.FileName} ({p.FileIndex + 1}/{p.FileCount})";
            });
            await CardImporter.ExecuteAsync(plan, VerifyChecksums, progress, _cts.Token);
            ResultFiles = plan.Select(p => p.Destination).ToList();
            var skipped = plan.Count(p => p.AlreadyPresent);
            StatusText = skipped > 0 ? $"Fertig – {skipped} Datei(en) waren schon vorhanden." : "Fertig.";
            Finished?.Invoke(this, true);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Abgebrochen.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = "Fehler: " + ex.Message;
            _dialogs.Error(ex.Message, "Import fehlgeschlagen");
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (IsBusy)
        {
            _cts?.Cancel();
        }
        else
        {
            Finished?.Invoke(this, false);
        }
    }

    private void ScanSource()
    {
        Files.Clear();
        if (!string.IsNullOrEmpty(SourceFolder) && Directory.Exists(SourceFolder))
        {
            try
            {
                foreach (var path in CardScanner.Scan(SourceFolder))
                {
                    var size = new FileInfo(path).Length;
                    Files.Add(new CardFile(path, Path.GetFileName(path), MediaItemViewModel.FormatSize(size), size));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                StatusText = "Ordner kann nicht gelesen werden: " + ex.Message;
            }

            CameraFolder = GuessCameraFolder(Files.Select(f => f.Path).ToList());
        }

        OnPropertyChanged(nameof(SummaryText));
        StartCommand.NotifyCanExecuteChanged();
    }

    private static string GuessCameraFolder(IReadOnlyList<string> files)
    {
        if (files.Count == 0)
        {
            return "Kamera";
        }

        var extension = Path.GetExtension(files[0]).ToUpperInvariant();
        return extension switch
        {
            ".MOV" => "EOS 70D",
            ".MP4" => "EOS R6 Mark II",
            _ => "Kamera",
        };
    }

    private static IEnumerable<string> DetectCards()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            bool ready;
            try
            {
                ready = drive.IsReady && drive.DriveType is DriveType.Removable or DriveType.Fixed;
            }
            catch (IOException)
            {
                continue;
            }

            if (ready && CardScanner.LooksLikeCameraCard(drive.RootDirectory.FullName))
            {
                yield return drive.RootDirectory.FullName;
            }
        }
    }
}
