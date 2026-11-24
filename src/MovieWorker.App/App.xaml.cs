using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using MovieWorker.App.Services;
using MovieWorker.App.ViewModels;

namespace MovieWorker.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Zahlen und Zeiten in der Oberfläche im Format des Systems anzeigen (z. B. Komma statt Punkt).
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

        // Selbsttest für die automatische Prüfung auf GitHub (ohne Benutzer, ohne Dialoge).
        if (e.Args.Length >= 3 && e.Args[0] == "--smoke-test")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var exitCode = await SmokeTest.RunAsync(e.Args[1], e.Args[2]);

            // Hart beenden: Der Exit-Code des Selbsttests soll nicht von Aufräumarbeiten abhängen.
            Environment.Exit(exitCode);
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var settings = new SettingsService();
        var tools = new MediaToolsService(settings);
        var viewModel = new MainViewModel(new WpfDialogService(), settings, tools, new ThumbnailService(tools));
        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();

        // Projekt aus der Kommandozeile öffnen (z. B. Doppelklick auf eine .mvproj-Datei).
        if (e.Args.Length > 0 && File.Exists(e.Args[0]))
        {
            if (e.Args[0].EndsWith(".mvproj", StringComparison.OrdinalIgnoreCase))
            {
                await viewModel.OpenProjectFileAsync(e.Args[0]);
            }
            else
            {
                await viewModel.ImportPathsAsync(e.Args);
            }
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Ein unerwarteter Fehler ist aufgetreten:\n\n{e.Exception.Message}\n\nDein Projekt ist davon nicht betroffen – bitte speichere zur Sicherheit.",
            "MovieWorker", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
