using System.Windows;
using MovieWorker.App.ViewModels;

namespace MovieWorker.App.Views;

public partial class ProjectSettingsWindow : Window
{
    public ProjectSettingsWindow(ProjectSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
