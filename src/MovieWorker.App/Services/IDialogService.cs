namespace MovieWorker.App.Services;

public enum DialogAnswer
{
    Yes,
    No,
    Cancel,
}

/// <summary>Alle Dialoge laufen über diese Schnittstelle – so bleiben die ViewModels ohne Fenster testbar.</summary>
public interface IDialogService
{
    IReadOnlyList<string> OpenFiles(string title, string filter);

    string? OpenFile(string title, string filter);

    string? SaveFile(string title, string filter, string defaultName, string? initialFolder = null);

    string? PickFolder(string title, string? initialFolder = null);

    void Info(string message, string title = "MovieWorker");

    void Error(string message, string title = "MovieWorker");

    bool Confirm(string message, string title = "MovieWorker");

    DialogAnswer AskYesNoCancel(string message, string title = "MovieWorker");

    /// <summary>Fragt einen kurzen Text ab (z. B. Ordnername). null = abgebrochen.</summary>
    string? Prompt(string message, string title, string defaultValue);

    /// <summary>Öffnet ein eigenes Fenster mit ViewModel modal. Liefert true bei „OK“.</summary>
    bool ShowDialog(object viewModel);
}
