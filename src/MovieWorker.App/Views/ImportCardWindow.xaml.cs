using System.ComponentModel;
using System.Windows;
using MovieWorker.App.ViewModels;

namespace MovieWorker.App.Views;

public partial class ImportCardWindow : Window
{
    private readonly ImportCardViewModel _viewModel;

    public ImportCardWindow(ImportCardViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.Finished += (_, ok) => DialogResult = ok;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Während des Kopierens nicht einfach schließen – erst abbrechen.
        if (_viewModel.IsBusy)
        {
            e.Cancel = true;
            _viewModel.CancelCommand.Execute(null);
        }

        base.OnClosing(e);
    }
}
