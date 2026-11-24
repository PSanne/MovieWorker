using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MovieWorker.App.Services;
using MovieWorker.App.ViewModels;
using MovieWorker.App.Views;
using MovieWorker.Core.Camera;
using MovieWorker.Core.Media;
using MovieWorker.Core.Settings;
using MovieWorker.Core.Timeline;

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
        var log = new StreamWriter(logFile) { AutoFlush = true };
        var logLock = new object();
        void Log(string text)
        {
            lock (logLock)
            {
                try
                {
                    log.WriteLine($"{DateTime.Now:HH:mm:ss} {text}");
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

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
            try
            {
                foreach (Window window in Application.Current.Windows)
                {
                    window.Close();
                }

                await Settle();
            }
            catch (Exception ex)
            {
                Log("Hinweis beim Schließen: " + ex.Message);
            }

            lock (logLock)
            {
                log.Dispose();
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

        settings.Settings.ProxyFolder = Path.Combine(c.Temp, "Proxys");
        settings.Settings.AutoCreateProxies = false; // Proxys erzeugt der Test gezielt weiter unten
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
        c.Check(vm.Project.AudioTracks.First().Clips.Count == imported.Count(m => m.IsAudioOnly), "Musik und Rekorder-Ton in A1");
        var firstVideo = imported.First(m => !m.IsAudioOnly).Model.First.Video!;
        c.Check(vm.Project.Settings.Width == firstVideo.Width, "Projekteinstellungen vom ersten Clip übernommen");
        var clipCount = vm.Project.AllClips.Count();
        vm.Undo();
        c.Check(vm.Project.AllClips.Count() == clipCount - 1, "Rückgängig");
        vm.Redo();
        c.Check(vm.Project.AllClips.Count() == clipCount, "Wiederholen");
        c.Check(vm.Project.Media.Count == imported.Count, "Medienpool bleibt bei Rückgängig erhalten");

        // ---- Timeline-Bearbeitung (wie mit Maus und Tastatur)
        await Settle(800);
        var timeline = window.TimelineControl;
        timeline.ZoomToFit();
        var v1 = vm.Project.VideoTracks.First();
        var first = v1.OrderedClips.First();
        vm.PlayheadFrame = first.StartFrame + first.LengthFrames / 2;
        vm.SplitAtPlayhead();
        c.Check(vm.Project.VideoTracks.First().Clips.Count == v1.Clips.Count + 1, "Teilen am Abspielkopf");
        var second = vm.Project.VideoTracks.First().OrderedClips.ElementAt(1);
        vm.SelectedClipId = second.Id;
        c.Check(vm.SelectedClip is not null, "Clip ausgewählt, Inspektor zeigt Clip");
        vm.SelectedClip!.VolumeDb = -6;
        vm.SelectedClip.VolumeDb = -8;
        c.Check(Math.Abs(vm.Project.FindClip(second.Id)!.Value.Clip.VolumeDb + 8) < 0.01, "Lautstärke im Inspektor");
        vm.SelectedClip.FadeInSeconds = 0.5;
        vm.RippleDeleteSelectedClip();
        c.Check(vm.Project.FindClip(second.Id) is null, "Löschen und Lücke schließen");
        vm.Undo();
        c.Check(vm.Project.FindClip(second.Id) is not null, "Rückgängig nach Löschen");
        var lastClip = vm.Project.VideoTracks.First().OrderedClips.Last();
        vm.MoveClip(lastClip.Id, lastClip.StartFrame + 50, vm.Project.VideoTracks.First().Id);
        c.Check(vm.Project.FindClip(lastClip.Id)!.Value.Clip.StartFrame == lastClip.StartFrame + 50, "Clip verschieben");
        vm.TrimClip(lastClip.Id, atStart: false, -10);
        c.Check(vm.Project.FindClip(lastClip.Id)!.Value.Clip.LengthFrames == lastClip.LengthFrames - 10, "Clip-Ende trimmen");
        vm.PlayheadFrame = 10;
        vm.MarkIn();
        vm.PlayheadFrame = 60;
        vm.MarkOut();
        c.Check(vm.Project.MarkIn == 10 && vm.Project.MarkOut == 60, "In/Out-Marken");
        vm.NextEdit();
        c.Check(vm.PlayheadFrame > 60, "Zum nächsten Schnitt springen");
        var newTrack = vm.Editor.AddTrack(MovieWorker.Core.Timeline.TrackKind.Video);
        c.Check(vm.Project.VideoTracks.Count() == 2, "Videospur hinzugefügt");
        c.Check(vm.InsertMediaAt(by70D!.Model.Id, newTrack.Id, 0), "Aufnahme per Drag & Drop auf V2");
        vm.ToggleTrackMute(vm.Project.AudioTracks.First().Id);
        c.Check(vm.Project.AudioTracks.First().Muted, "Spur stumm schalten");
        timeline.ZoomBy(2);
        timeline.ZoomToFit();
        await Settle(2500);
        var wave = await vm.Waveforms.GetAsync(vm.Project.AudioTracks.First().Clips[0].SourcePaths);
        c.Check(wave is { Peaks.Length: > 0 }, "Wellenform geladen");
        SaveScreenshot(window, Path.Combine(c.OutputFolder, "timeline.png"));
        clipCount = vm.Project.AllClips.Count();

        // ---- Proxys
        var needProxy = vm.MediaItems.Where(m => vm.Proxies.NeedsProxy(m)).ToList();
        c.Check(needProxy.Any(m => m.Detection.Camera == CanonCamera.EosR6MarkII), "R6-HEVC braucht einen Proxy");
        await vm.CreateProxiesAsync(needProxy);
        c.Check(needProxy.All(m => m.Model.HasProxy), $"{needProxy.Count} Proxys erstellt");

        // ---- Programmmonitor: Standbild und Wiedergabe
        var program = window.ProgramMonitorControl;
        vm.PlayheadFrame = 5;
        c.Check(await program.RenderStillNowAsync(), "Standbild im Programmmonitor (mit Farbumrechnung)");
        var before = vm.PlayheadFrame;
        program.Play(1);
        await Settle(1200);
        program.Pause();
        c.Check(vm.PlayheadFrame > before + 5, $"Wiedergabe läuft (Abspielkopf {before} → {vm.PlayheadFrame})");
        program.Shuttle(1);
        program.Shuttle(1);
        await Settle(500);
        program.Shuttle(0);
        c.Check(!program.IsPlaying, "J/K/L-Shuttle hält mit K an");
        SaveScreenshot(window, Path.Combine(c.OutputFolder, "monitore.png"));

        // ---- Quellmonitor: Ausschnitt wählen und einfügen
        vm.SelectedMedia = by70D;
        await Settle(500);
        var source = window.SourceMonitorControl;
        source.SetPosition(1);
        source.MarkIn();
        source.SetPosition(3);
        source.MarkOut();
        c.Check(by70D!.SourceIn == 1 && by70D.SourceOut == 3, "In/Out im Quellmonitor");
        var clipsBefore = vm.Project.AllClips.Count();
        var appended = vm.AddToTimeline(by70D.Model, by70D.SourceIn ?? 0, by70D.SourceOut);
        c.Check(vm.Project.AllClips.Count() == clipsBefore + 1 && appended.LengthFrames == 2 * vm.Project.Settings.FrameRate.NominalFps,
            "Ausschnitt (2 s) ans Ende angehängt");
        source.TogglePlay();
        await Settle(600);
        source.TogglePlay();
        c.Check(source.PositionSeconds > 1, "Quellmonitor spielt ab");
        clipCount = vm.Project.AllClips.Count();

        // ---- Export (Sprint 5): Dialog, Warteschlange, Plattform-Vorlagen, Bereich, Einzelbild
        var exportDialog = new ExportDialogViewModel(vm.Project, vm.ProjectName, dialogs, settings);
        c.Check(exportDialog.Issues.Count > 0 || exportDialog.HasNoIssues, "Prüfung vor dem Export läuft");
        await ShowAndClose(new ExportWindow(exportDialog), c);
        var exportFolder = Path.Combine(c.Temp, "Export");
        vm.PlayheadFrame = 0;
        vm.MarkIn();
        vm.PlayheadFrame = 2 * vm.Project.Settings.FrameRate.NominalFps;
        vm.MarkOut();
        var hdJob = vm.EnqueueExport(MovieWorker.Core.Export.ExportPreset.Find("web-h264-1080")!, Path.Combine(exportFolder, "range.mp4"), useRange: true);
        var verticalJob = vm.EnqueueExport(MovieWorker.Core.Export.ExportPreset.Find("social-vertical")!, Path.Combine(exportFolder, "hoch.mp4"), useRange: true);
        var cancelled = vm.EnqueueExport(MovieWorker.Core.Export.ExportPreset.Find("prores-lt")!, Path.Combine(exportFolder, "abbruch.mov"), useRange: false);
        vm.ExportQueue.Cancel(cancelled);
        await vm.ExportQueue.WaitAllAsync();
        c.Check(hdJob.State == ExportJobState.Done, "Export Full HD (Bereich): " + hdJob.Detail);
        c.Check(verticalJob.State == ExportJobState.Done, "Export Hochformat 9:16: " + verticalJob.Detail);
        c.Check(cancelled.State == ExportJobState.Cancelled && !File.Exists(cancelled.OutputPath), "Wartender Auftrag abgebrochen");
        var hdInfo = await tools.Require().ProbeAsync(hdJob.OutputPath);
        c.Check(hdInfo.Video!.Height == Math.Min(1080, vm.Project.Settings.Height) && Math.Abs(hdInfo.Duration.TotalSeconds - 2) < 0.2,
            $"Full-HD-Datei stimmt ({hdInfo.Video.Width}×{hdInfo.Video.Height}, {hdInfo.Duration.TotalSeconds:0.00} s)");
        var verticalInfo = await tools.Require().ProbeAsync(verticalJob.OutputPath);
        c.Check(verticalInfo.Video!.Width == 1080 && verticalInfo.Video.Height == 1920, "Hochformat-Datei ist 1080×1920");
        vm.PlayheadFrame = 10;
        c.Check(await vm.ExportStillToAsync(Path.Combine(exportFolder, "bild.png")), "Einzelbild exportiert");
        SaveScreenshot(window, Path.Combine(c.OutputFolder, "export.png"));

        // ---- Medien-Arbeitsbereich (Sprint 6): Ordner, Filter, Metadaten, Ton-Synchronisation
        dialogs.Answers.Enqueue("Interviews");
        vm.NewBin();
        var interviews = vm.Bins.First(b => b.Kind == BinKind.Folder);
        c.Check(vm.Bins.Count == 3 && interviews.Name.Contains("Interviews"), "Ordner angelegt");
        vm.MoveMediaToBin(by70D!, interviews.Id);
        vm.SelectedBin = interviews;
        c.Check(vm.MediaView.Cast<object>().Count() == 1, "Ordner-Ansicht zeigt nur einsortierte Aufnahme");
        vm.SelectedBin = vm.Bins[0];
        vm.SearchText = "0042";
        c.Check(vm.MediaView.Cast<MediaItemViewModel>().Single().Detection.Camera == CanonCamera.EosR6MarkII, "Suche nach Name");
        vm.SearchText = string.Empty;
        byR6!.Rating = 4;
        byR6.KeywordsText = "See, Drohne";
        vm.MinRatingFilter = 4;
        c.Check(vm.MediaView.Cast<object>().Count() == 1, "Filter nach Bewertung");
        vm.MinRatingFilter = 0;
        vm.SearchText = "drohne";
        c.Check(vm.MediaView.Cast<object>().Count() == 1, "Suche in Schlagwörtern");
        vm.SearchText = string.Empty;
        vm.KindFilter = 2;
        c.Check(vm.MediaView.Cast<MediaItemViewModel>().All(m => m.IsAudioOnly), "Filter nur Ton");
        vm.KindFilter = 0;
        vm.SortMode = 2;
        c.Check(vm.MediaView.Cast<MediaItemViewModel>().First() == byR6, "Sortierung nach Bewertung");
        vm.SortMode = 0;

        var syncVideo = imported.First(m => m.Name.StartsWith("MVI_0002", StringComparison.OrdinalIgnoreCase));
        var recorder = imported.First(m => m.Name.StartsWith("ZOOM0001", StringComparison.OrdinalIgnoreCase));
        c.Check(await vm.SyncPairAsync(syncVideo, recorder, quiet: true), "Ton-Synchronisation gefunden: " + syncVideo.SyncText);
        c.Check(Math.Abs(syncVideo.Model.SyncedAudio!.OffsetSeconds - 0.75) < 0.02, $"Versatz stimmt (0,75 s erwartet, {syncVideo.Model.SyncedAudio.OffsetSeconds:0.000} s gefunden)");
        var syncedClip = vm.PlaceMedia(syncVideo.Model, 1, 4, null, null)!;
        var externalClip = vm.Project.AllClips.FirstOrDefault(cl => cl.MediaId == recorder.Model.Id && Math.Abs(cl.SourceIn - 1.75) < 0.02);
        c.Check(externalClip is not null && externalClip.StartFrame == syncedClip.StartFrame && syncedClip.AudioMuted,
            "Synchronisierter Ton liegt passend in der Timeline, Kameraton stumm");
        SaveScreenshot(window, Path.Combine(c.OutputFolder, "medienpool.png"));
        clipCount = vm.Project.AllClips.Count();

        // ---- Schnellschnitt (Sprint 7): Einfügemodi, Quellband, Doppel-Timeline
        vm.ShowOverview = true;
        vm.SelectedMedia = byR6;
        await Settle(400);
        var src = window.SourceMonitorControl;
        src.SetPosition(0.5);
        src.MarkIn();
        src.SetPosition(1.5);
        src.MarkOut();
        var rate = vm.Project.Settings.FrameRate;
        var length = rate.SecondsToFrames(1.0);
        vm.SelectedClipId = null;
        vm.PlayheadFrame = 5;
        var durationBefore = vm.Editor.Duration;
        var inserted = src.Edit(EditMode.Insert);
        c.Check(inserted is not null && vm.Editor.Duration == durationBefore + length && vm.PlayheadFrame == 5 + length,
            "F9 Einfügen schiebt alles nach hinten");
        var overwritten = src.Edit(EditMode.Overwrite);
        c.Check(overwritten is not null && vm.Editor.Duration == durationBefore + length, "F10 Überschreiben ändert die Länge nicht");
        var replaced = src.Edit(EditMode.Replace);
        c.Check(replaced is not null && vm.Project.FindClip(replaced.Id)!.Value.Clip.StartFrame == overwritten!.StartFrame
            && vm.Project.FindClip(overwritten.Id) is null, "F11 Ersetzen behält Position");
        vm.SelectedClipId = null;
        vm.PlayheadFrame = 0;
        var onTop = src.Edit(EditMode.PlaceOnTop);
        c.Check(onTop is not null && vm.Project.FindClip(onTop.Id)!.Value.Track.Name != "V1", "F12 Oben platzieren (B-Roll)");
        vm.SelectedClipId = null;
        var appended7 = src.Edit(EditMode.Append);
        c.Check(appended7 is not null && vm.Project.FindClip(appended7.Id)!.Value.Clip.EndFrame == vm.Project.VideoTracks.First().Clips.Max(cl => cl.EndFrame),
            "Umschalt+F12 Anhängen");
        vm.SourceTapeMode = true;
        await Settle(400);
        c.Check(src.IsTapeMode && src.CurrentItem is not null, "Quellband aktiv");
        vm.SelectedMedia = by70D;
        await Settle(300);
        c.Check(src.CurrentItem == by70D, "Quellband springt zur gewählten Aufnahme");
        vm.SourceTapeMode = false;
        SaveScreenshot(window, Path.Combine(c.OutputFolder, "schnellschnitt.png"));
        vm.ShowOverview = false;
        clipCount = vm.Project.AllClips.Count();

        // ---- Profi-Schnitt (Sprint 8): Trimm-Modi, Keyframes, Rampe, Verschachteln
        var left = vm.AddToTimeline(by70D!.Model, 1, 3);
        var middle = vm.AddToTimeline(by70D.Model, 3.5, 4.5);
        var right = vm.AddToTimeline(by70D.Model, 0.5, 1.5);
        Core.Timeline.TimelineClip Live(Core.Timeline.TimelineClip clip) => vm.Project.FindClip(clip.Id)!.Value.Clip;
        var r25 = vm.Project.Settings.FrameRate.NominalFps;
        vm.TrimMode = TrimMode.Roll;
        vm.TrimClip(left.Id, atStart: false, 5);
        c.Check(Live(left).LengthFrames == 2 * r25 + 5 && Live(middle).StartFrame == Live(left).EndFrame, "Roll-Schnitt");
        vm.TrimMode = TrimMode.Slip;
        var middleSource = Live(middle).SourceIn;
        vm.TrimEdit(TrimMode.Slip, middle.Id, false, 10);
        c.Check(Live(middle).SourceIn > middleSource && Live(middle).StartFrame == Live(left).EndFrame, "Slip");
        vm.TrimEdit(TrimMode.Slide, middle.Id, false, 3);
        c.Check(Live(middle).StartFrame == Live(left).EndFrame && Live(right).StartFrame == Live(middle).EndFrame, "Slide");
        var rightBefore = Live(right).StartFrame;
        vm.TrimMode = TrimMode.Ripple;
        vm.TrimClip(middle.Id, atStart: false, -5);
        c.Check(Live(right).StartFrame == rightBefore - 5 && Live(right).StartFrame == Live(middle).EndFrame, "Ripple-Trimmen");
        vm.TrimMode = TrimMode.Normal;

        vm.SelectedClipId = left.Id;
        vm.PlayheadFrame = Live(left).StartFrame;
        var zoom = vm.SelectedClip!.Motion.First(m => m.Property == Core.Timeline.AnimatedProperty.Scale);
        zoom.ToggleKeyCommand.Execute(null);
        vm.PlayheadFrame = Live(left).EndFrame - 1;
        zoom.Value = 140;
        c.Check(Live(left).KeyframesFor(Core.Timeline.AnimatedProperty.Scale)?.Keys.Count == 2, "Zoom-Keyframes (Ken Burns)");
        vm.PlayheadFrame = Live(left).StartFrame + 10;
        c.Check(await program.RenderStillNowAsync(), "Standbild mit Keyframe-Zoom " + program.MessageText);
        var vol = vm.SelectedClip.VolumeKeys;
        vol.ToggleKeyCommand.Execute(null);
        c.Check(Live(left).IsAnimated(Core.Timeline.AnimatedProperty.Volume), "Lautstärke-Keyframe");

        vm.SelectedClipId = right.Id;
        vm.SelectedClip!.RampShape = 0;
        vm.SelectedClip.ApplyRampCommand.Execute(null);
        c.Check(vm.Project.VideoTracks.First().Clips.Count(cl => cl.SourcePaths.SequenceEqual(right.SourcePaths) && cl.Speed < 1) >= 3,
            "Geschwindigkeitsrampe");

        vm.Editor.SetMarks(Live(left).StartFrame, Live(middle).EndFrame);
        dialogs.Answers.Enqueue("Szene A");
        vm.Nest();
        var nestedClip = vm.Project.AllClips.FirstOrDefault(cl => cl.IsNested);
        c.Check(nestedClip is not null && nestedClip.Name == "Szene A", "Verschachteln");
        vm.PlayheadFrame = nestedClip!.StartFrame + 5;
        c.Check(await program.RenderStillNowAsync(), "Vorschau zeigt Inhalt des verschachtelten Clips " + program.MessageText);
        var nestJob = vm.EnqueueExport(MovieWorker.Core.Export.ExportPreset.Find("web-h264-1080")!, Path.Combine(exportFolder, "verschachtelt.mp4"), useRange: true);
        await vm.ExportQueue.WaitAllAsync();
        c.Check(nestJob.State == ExportJobState.Done, "Export mit Keyframes und Verschachtelung: " + nestJob.Detail);
        vm.SelectedClipId = nestedClip.Id;
        vm.Unnest();
        c.Check(!vm.Project.AllClips.Any(cl => cl.IsNested), "Verschachtelung aufgelöst");
        SaveScreenshot(window, Path.Combine(c.OutputFolder, "profischnitt.png"));
        clipCount = vm.Project.AllClips.Count();

        // ---- Übergänge & Titel (Sprint 9)
        vm.SelectedClipId = null;
        var cutClip = vm.Project.VideoTracks.First().OrderedClips
            .First(cl => cl.SourceIn > 0.6 && !cl.IsNested && vm.Project.VideoTracks.First().Clips.Any(p => p.EndFrame == cl.StartFrame));
        vm.PlayheadFrame = cutClip.StartFrame + 2;
        vm.AddDefaultTransition();
        c.Check(vm.Project.FindClip(cutClip.Id)?.Clip.TransitionIn is { Type: Core.Timeline.TransitionType.Crossfade }, "Strg+D: Kreuzblende am Schnitt");
        vm.SelectedClip!.SelectedTransition = ClipViewModel.TransitionChoices.First(t => t.Value == Core.Timeline.TransitionType.CircleOpen);
        c.Check(vm.Project.FindClip(cutClip.Id)!.Value.Clip.TransitionIn!.Type == Core.Timeline.TransitionType.CircleOpen, "Übergangsart geändert");

        vm.PlayheadFrame = cutClip.StartFrame - 10;
        var lowerThird = vm.AddTitle("LowerThird");
        vm.SelectedClip!.Title!.Text = "Max Mustermann\nKamera & Schnitt";
        vm.SelectedClip.Title.Box = true;
        c.Check(vm.Project.FindClip(lowerThird.Id)!.Value.Clip.Title!.Lines.Count == 2, "Bauchbinde mit zwei Zeilen");
        vm.PlayheadFrame = lowerThird.StartFrame + 20;
        c.Check(await program.RenderStillNowAsync(), "Standbild mit Titel " + program.MessageText);
        program.Play(1);
        await Settle(300);
        var overlayLines = program.TitleOverlayCount;
        var stillMode = program.IsStillMode;
        program.Pause();
        c.Check(overlayLines > 0 || stillMode, $"Titel beim Abspielen sichtbar ({overlayLines} Zeilen, Standbild-Modus: {stillMode})");
        vm.PlayheadFrame = vm.Editor.Duration;
        var credits = vm.AddTitle("Credits");
        vm.PlayheadFrame = credits.StartFrame + 50;
        c.Check(await program.RenderStillNowAsync(), "Abspann über Schwarz " + program.MessageText);

        vm.Editor.SetMarks(Math.Max(0, cutClip.StartFrame - 30), cutClip.StartFrame + 40);
        var titleJob = vm.EnqueueExport(MovieWorker.Core.Export.ExportPreset.Find("web-h264-1080")!, Path.Combine(exportFolder, "titel.mp4"), useRange: true);
        await vm.ExportQueue.WaitAllAsync();
        c.Check(titleJob.State == ExportJobState.Done, "Export mit Übergang und Titeln: " + titleJob.Detail);
        SaveScreenshot(window, Path.Combine(c.OutputFolder, "titel.png"));
        clipCount = vm.Project.AllClips.Count();

        // ---- Mehrspur, Multicam & Mischmodi (Sprint 10)
        var camA = imported.First(m => m.Name.StartsWith("MVI_0001", StringComparison.OrdinalIgnoreCase));
        var camB = imported.First(m => m.Name.StartsWith("MVI_0002", StringComparison.OrdinalIgnoreCase));
        vm.SelectedMediaItems = [camA, camB];
        var mc = await vm.CreateMulticamAsync();
        c.Check(mc is not null && vm.Project.FindClip(mc.Id)?.Clip.IsMulticam == true, "Multicam-Clip aus zwei Kameras");
        vm.PlayheadFrame = mc!.StartFrame + 25;
        c.Check(vm.SwitchAngle(1), "Kamerawechsel auf Kamera 2");
        vm.PlayheadFrame = mc.StartFrame + 60;
        program.Play(1);
        await Settle(250);
        var liveSwitch = vm.SwitchAngle(0);
        await Settle(250);
        program.Pause();
        c.Check(liveSwitch && vm.Project.VideoTracks.First().Clips.Count(cl => cl.IsMulticam) >= 3, "Live-Kamerawechsel beim Abspielen");
        vm.PlayheadFrame = mc.StartFrame + 30;
        c.Check(await program.RenderStillNowAsync(), "Vorschau zeigt Kamera 2 " + program.MessageText);

        var pipClip = vm.Project.VideoTracks.Skip(1).SelectMany(t => t.Clips).First(cl => cl.SourcePaths.Count > 0);
        vm.SelectedClipId = pipClip.Id;
        c.Check(vm.SelectedClip!.IsOverlayClip, "Clip auf oberer Videospur erkannt");
        vm.SelectedClip.PipPresetCommand.Execute("TR");
        vm.SelectedClip.CropLeft = 10;
        vm.SelectedClip.FlipHorizontal = true;
        var pipLive = vm.Project.FindClip(pipClip.Id)!.Value.Clip;
        c.Check(Math.Abs(pipLive.Transform.Scale - 30) < 0.01 && pipLive.Transform.FlipHorizontal, "Bild-in-Bild oben rechts, gespiegelt");
        vm.PlayheadFrame = pipLive.StartFrame + 5;
        c.Check(await program.RenderStillNowAsync(), "Exakte Vorschau mit Bild-in-Bild " + program.MessageText);
        vm.SelectedClip.Blend = ClipViewModel.BlendModes.First(b => b.Value == Core.Timeline.BlendMode.Screen);
        c.Check(await program.RenderStillNowAsync(), "Vorschau mit Mischmodus " + program.MessageText);
        vm.Editor.SetMarks(pipLive.StartFrame, pipLive.StartFrame + 40);
        var compJob = vm.EnqueueExport(MovieWorker.Core.Export.ExportPreset.Find("web-h264-1080")!, Path.Combine(exportFolder, "mehrspur.mp4"), useRange: true);
        await vm.ExportQueue.WaitAllAsync();
        c.Check(compJob.State == ExportJobState.Done, "Export mit Bild-in-Bild und Mischmodus: " + compJob.Detail);
        c.Check(await vm.ExportStillToAsync(Path.Combine(exportFolder, "komposit.png")), "Einzelbild aus der Export-Kette");
        SaveScreenshot(window, Path.Combine(c.OutputFolder, "mehrspur.png"));
        clipCount = vm.Project.AllClips.Count();

        // ---- Farbe I (Sprint 11): LUTs, Grundkorrektur, Farbräder, Abgleich, Messinstrumente, HDR10
        MovieWorker.Core.Color.GradeLutCache.Folder = Path.Combine(c.Temp, "Farbkorrekturen");
        var lutFolder = Path.Combine(c.Temp, "LUTs");
        Directory.CreateDirectory(lutFolder);
        var lutFile = Path.Combine(lutFolder, "CanonLog3-to-BT709_Test.cube");
        File.WriteAllText(lutFile, MovieWorker.Core.Color.GradeLutCache.ToCube(new MovieWorker.Core.Color.ColorGrade { Contrast = 20, Saturation = 120 }, 17));
        dialogs.Answers.Enqueue(lutFolder);
        vm.SetLutFolderCommand.Execute(null);
        c.Check(vm.LutFiles.Count == 1 && settings.Settings.DefaultCanonLog3Lut == lutFile, "LUT-Ordner gelesen, Canon-Log-3-LUT als Standard erkannt");

        var logClip = vm.Project.VideoTracks.First().Clips.First(cl => !cl.IsMulticam
            && vm.Project.FindMedia(cl.MediaId)?.Detection.Transfer == ColorTransfer.CanonLog3);
        vm.SelectedClipId = logClip.Id;
        var color = vm.SelectedClip!.Color;
        color.Lut = color.Luts.First();
        color.Exposure = 0.5;
        color.Saturation = 110;
        color.Lift.X = 0.3;
        color.Gamma.Master = 0.1;
        var graded = vm.Project.FindClip(logClip.Id)!.Value.Clip;
        c.Check(graded.ColorTransform == Core.Timeline.ColorTransform.Lut && graded.LutPath == lutFile && !graded.Grade.IsNeutral
                && graded.Grade.Lift.R > 0, "LUT, Belichtung, Sättigung und Farbrad gesetzt");
        vm.PlayheadFrame = graded.StartFrame + 5;
        c.Check(await program.RenderStillNowAsync(), "Vorschau mit LUT und Farbkorrektur " + program.MessageText);
        foreach (var kind in vm.ScopeKinds)
        {
            vm.SelectedScopeChoice = kind;
            c.Check(await vm.UpdateScopesAsync() && vm.ScopeImage is not null, "Messinstrument " + kind.Label);
        }

        vm.CopyGrade();
        var eosClip = vm.Project.VideoTracks.First().Clips.First(cl => !cl.IsMulticam
            && vm.Project.FindMedia(cl.MediaId)?.Detection.Camera == CanonCamera.Eos70D);
        vm.SelectedClipId = eosClip.Id;
        vm.PasteGrade();
        c.Check(Math.Abs(vm.Project.FindClip(eosClip.Id)!.Value.Clip.Grade.Exposure - 0.5) < 1e-6, "Farbkorrektur kopiert und eingefügt");
        vm.ResetGradeCommand.Execute(null);
        vm.SelectedClipId = logClip.Id;
        await vm.SetGradeReferenceAsync();
        vm.SelectedClipId = eosClip.Id;
        c.Check(await vm.MatchToReferenceAsync() && vm.SelectedClip!.Color.IsMatched, "70D an R6-Referenz angeglichen");

        var pqClip = vm.Project.VideoTracks.First().Clips.First(cl => cl.ColorTransform == Core.Timeline.ColorTransform.HdrToSdr);
        vm.Editor.SetMarks(pqClip.StartFrame, pqClip.StartFrame + 25);
        var hdrJob = vm.EnqueueExport(MovieWorker.Core.Export.ExportPreset.Find("hevc-hdr10")!, Path.Combine(exportFolder, "hdr10.mp4"), useRange: true);
        await vm.ExportQueue.WaitAllAsync();
        c.Check(hdrJob.State == ExportJobState.Done, "HDR10-Export: " + hdrJob.Detail);
        var hdrInfo = await tools.Require().ProbeAsync(hdrJob.OutputPath);
        c.Check(hdrInfo.MetadataTransfer == ColorTransfer.HdrPq, "HDR10-Datei ist als PQ gekennzeichnet");
        SaveScreenshot(window, Path.Combine(c.OutputFolder, "farbe.png"));
        clipCount = vm.Project.AllClips.Count();

        // ---- Speichern & Laden
        var projectFile = Path.Combine(c.Temp, "Test.mvproj");
        c.Check(await vm.SaveToAsync(projectFile), "Projekt gespeichert");
        await vm.NewProjectAsync();
        c.Check(!vm.Project.AllClips.Any() && vm.MediaItems.Count == 0, "Neues leeres Projekt");
        await vm.OpenProjectFileAsync(projectFile);
        c.Check(vm.Project.AllClips.Count() == clipCount && vm.MediaItems.Count == imported.Count, "Projekt wieder geladen");
        c.Check(vm.MediaItems.Count(m => m.Model.HasProxy) == needProxy.Count, "Proxys bleiben im Projekt gespeichert");
        c.Check(vm.Bins.Count == 3 && vm.MediaItems.Any(m => m.Rating == 4 && m.KeywordsText.Contains("Drohne")) && vm.MediaItems.Any(m => m.HasSync),
            "Ordner, Bewertung, Schlagwörter und Synchronisation gespeichert");
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
