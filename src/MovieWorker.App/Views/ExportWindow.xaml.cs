using System.Windows;
using MovieWorker.App.ViewModels;

namespace MovieWorker.App.Views;

public partial class ExportWindow : Window
{
    public ExportWindow(ExportDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Finished += (_, ok) => DialogResult = ok;
    }
}
