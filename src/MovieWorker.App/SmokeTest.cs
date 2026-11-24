using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MovieWorker.App.Services;
using MovieWorker.App.ViewModels;
using MovieWorker.App.Views;
using MovieWorker.Core.Camera;
using MovieWorker.Core.Settings;

namespace MovieWorker.App;

/// <summary>
/// Automatischer Selbsttest unter Windows (GitHub Actions): startet die echte Oberfläche, importiert
/// Testclips, schneidet, speichert, lädt und öffnet alle Fenster. Jede Prüfung wird protokolliert;
/// bei einem Fehler endet das Programm mit Code 1.
/// Aufruf: MovieWorker.exe --smoke-test &lt;Ordner mit Testclips&gt; &lt;Protokolldatei&gt;
/// </summary>
public static class SmokeTest
{
    public static async Task<int> RunAsync(string mediaFolder, string logFile)
    {
        var outputFolder = Path.GetDirectoryName(Path.GetFullPath(logFile))!;
        Directory.CreateDirectory(outputFolder);
        await using var log = new StreamWriter(logFile) { AutoFlush = true };
        void Log(string text) => log.WriteLine(text);

        var context = new SmokeContext(mediaFolder, outputFolder, Log);
        var uiErrors = new List<string>();
        Application.Current.DispatcherUnhandledException += (_, e) =>
        {
            uiErrors.Add(e.Exception.ToString());
            Log("FEHLER (Oberfläche): " + e.Exception.Message);
            e.Handled = true;
        };
        try
        {
            await RunStepsAsync(context);
            context.Check(uiErrors.Count == 0, "Keine unbehandelten Fehler in der Oberfläche");
            Log("ERGEBNIS: Alle Prüfungen bestanden.");
            return 0;
        }
        catch (Exception ex)
        {
            Log($"FEHLER: {ex.GetType().Name}: {ex.Message}");
            Log(ex.StackTrace ?? string.Empty);
            return 1;
        }
        finally
        {
            foreach (Window window in Application.Current.Windows)
            {
                window.Close();
            }
        }
    }

    private sealed class SmokeContext(string mediaFolder, string outputFolder, Action<string> log)
    {
        public string MediaFolder { get; } = mediaFolder;

        public string OutputFolder { get; } = outputFolder;

        public Action<string> Log { get; } = log;

        public string Temp { get; } = Path.Combine(Path.GetTempPath(), "MovieWorkerSmoke-" + Guid.NewGuid().ToString("N")[..8]);

        public void Check(bool condition, string description)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Prüfung fehlgeschlagen: " + description);
            }

            Log("OK: " + description);
        }
    }

    private static async Task RunStepsAsync(SmokeContext c)
    {
        Directory.CreateDirectory(c.Temp);
        var dialogs = new ScriptedDialogService(c.Log);
        var settings = new SettingsService(new SettingsStore(Path.Combine(c.Temp, "settings.json")));
        var tools = new MediaToolsService(settings);
        c.Check(tools.IsAvailable, "FFmpeg gefunden (" + tools.StatusText + ")");

        var vm = new MainViewModel(dialogs, settings, tools, new ThumbnailService(tools));
        var window = new MainWindow(vm);
        Application.Current.MainWindow = window;
        window.Show();
        await Settle();
        c.Check(window.IsVisible, "Hauptfenster geöffnet");

        // ---- Import
        var files = Directory.GetFiles(c.MediaFolder, "*", SearchOption.AllDirectories);
        var imported = await vm.ImportPathsAsync(files);
        c.Check(imported.Count >= 3, $"{imported.Count} Aufnahmen importiert");
        var by70D = imported.FirstOrDefault(m => m.Detection.Camera == CanonCamera.Eos70D);
        var byR6 = imported.FirstOrDefault(m => m.Detection.Camera == CanonCamera.EosR6MarkII);
        c.Check(by70D is not null, "EOS-70D-Clip erkannt");
        c.Check(byR6 is not null, "EOS-R6-Mark-II-Clip erkannt");
        c.Check(imported.Any(m => m.IsAudioOnly), "Musikdatei als Audio erkannt");

        // ---- Medienpool & Inspektor
        vm.SelectedMedia = byR6;
        await Settle(1500);
        byR6!.SelectedCamera = CameraChoice.All.First(x => x.Camera == CanonCamera.Eos70D);
        c.Check(byR6.Detection.Camera == CanonCamera.Eos70D, "Kamera von Hand umgestellt");
        byR6.SelectedCamera = CameraChoice.All.First(x => x.Camera is null);

        // ---- Timeline
        foreach (var item in imported)
        {
            vm.AddToTimeline(item.Model);
        }

        c.Check(vm.Project.VideoTracks.First().Clips.Count == imported.Count(m => !m.IsAudioOnly), "Videoclips in V1");
        c.Check(vm.Project.AudioTracks.First().Clips.Count == 1, "Musik in A1");
        var firstVideo = imported.First(m => !m.IsAudioOnly).Model.First.Video!;
        c.Check(vm.Project.Settings.Width == firstVideo.Width, "Projekteinstellungen vom ersten Clip übernommen");
        var clipCount = vm.Project.AllClips.Count();
        vm.Undo();
        c.Check(vm.Project.AllClips.Count() == clipCount - 1, "Rückgängig");
        vm.Redo();
        c.Check(vm.Project.AllClips.Count() == clipCount, "Wiederholen");
        c.Check(vm.Project.Media.Count == imported.Count, "Medienpool bleibt bei Rückgängig erhalten");

        // ---- Speichern & Laden
        var projectFile = Path.Combine(c.Temp, "Test.mvproj");
        c.Check(await vm.SaveToAsync(projectFile), "Projekt gespeichert");
        await vm.NewProjectAsync();
        c.Check(!vm.Project.AllClips.Any() && vm.MediaItems.Count == 0, "Neues leeres Projekt");
        await vm.OpenProjectFileAsync(projectFile);
        c.Check(vm.Project.AllClips.Count() == clipCount && vm.MediaItems.Count == imported.Count, "Projekt wieder geladen");
        c.Check(settings.Settings.RecentProjects.Contains(projectFile), "Zuletzt geöffnet gemerkt");

        // ---- Karten-Import (kopieren + Prüfsumme)
        var plan = CardImporter.Plan(CardScanner.Scan(c.MediaFolder), Path.Combine(c.Temp, "Import"), DateTime.Today, "EOS 70D");
        var results = await CardImporter.ExecuteAsync(plan, verify: true);
        c.Check(results.Count > 0 && results.All(r => r.Verified && File.Exists(r.Operation.Destination)), "Karten-Import mit Prüfsumme");

        // ---- Dialoge laden (XAML-Prüfung)
        await ShowAndClose(new ImportCardWindow(new ImportCardViewModel(dialogs, settings)), c);
        await ShowAndClose(new ProjectSettingsWindow(new ProjectSettingsViewModel(vm.Project.Settings)), c);

        // ---- Vorschaubilder
        await Settle(3000);
        c.Check(vm.MediaItems.Any(m => m.Thumbnail is not null), "Vorschaubilder erzeugt");

        SaveScreenshot(window, Path.Combine(c.OutputFolder, "hauptfenster.png"));
        c.Log("Bildschirmfoto gespeichert.");
    }

    private static async Task ShowAndClose(Window window, SmokeContext c)
    {
        window.Owner = Application.Current.MainWindow;
        window.Show();
        await Settle();
        SaveScreenshot(window, Path.Combine(c.OutputFolder, window.GetType().Name + ".png"));
        window.Close();
        c.Log("OK: Fenster " + window.GetType().Name + " geladen");
    }

    /// <summary>Lässt die Oberfläche arbeiten (Layout, Bindungen, Hintergrundaufgaben).</summary>
    private static async Task Settle(int milliseconds = 300)
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(milliseconds);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private static void SaveScreenshot(FrameworkElement element, string path)
    {
        var width = (int)Math.Max(1, element.ActualWidth);
        var height = (int)Math.Max(1, element.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
