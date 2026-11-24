using System.Windows;
using Microsoft.Win32;
using MovieWorker.App.ViewModels;
using MovieWorker.App.Views;

namespace MovieWorker.App.Services;

public sealed class WpfDialogService : IDialogService
{
    private static Window? Owner => Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? Application.Current?.MainWindow;

    public IReadOnlyList<string> OpenFiles(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, Multiselect = true };
        return dialog.ShowDialog(Owner) == true ? dialog.FileNames : [];
    }

    public string? OpenFile(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? SaveFile(string title, string filter, string defaultName, string? initialFolder = null)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = defaultName };
        if (!string.IsNullOrEmpty(initialFolder) && System.IO.Directory.Exists(initialFolder))
        {
            dialog.InitialDirectory = initialFolder;
        }

        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? PickFolder(string title, string? initialFolder = null)
    {
        var dialog = new OpenFolderDialog { Title = title };
        if (!string.IsNullOrEmpty(initialFolder) && System.IO.Directory.Exists(initialFolder))
        {
            dialog.InitialDirectory = initialFolder;
        }

        return dialog.ShowDialog(Owner) == true ? dialog.FolderName : null;
    }

    public void Info(string message, string title = "MovieWorker") =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void Error(string message, string title = "MovieWorker") =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public bool Confirm(string message, string title = "MovieWorker") =>
        Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public DialogAnswer AskYesNoCancel(string message, string title = "MovieWorker") =>
        Show(message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
        {
            MessageBoxResult.Yes => DialogAnswer.Yes,
            MessageBoxResult.No => DialogAnswer.No,
            _ => DialogAnswer.Cancel,
        };

    public bool ShowDialog(object viewModel)
    {
        Window window = viewModel switch
        {
            ImportCardViewModel vm => new ImportCardWindow(vm),
            ProjectSettingsViewModel vm => new ProjectSettingsWindow(vm),
            ExportDialogViewModel vm => new ExportWindow(vm),
            _ => throw new ArgumentException($"Kein Fenster für {viewModel.GetType().Name}"),
        };
        window.Owner = Owner;
        return window.ShowDialog() == true;
    }

    private static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image) =>
        Owner is { } owner
            ? MessageBox.Show(owner, message, title, buttons, image)
            : MessageBox.Show(message, title, buttons, image);
}
