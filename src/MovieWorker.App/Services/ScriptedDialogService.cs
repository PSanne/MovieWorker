namespace MovieWorker.App.Services;

/// <summary>Dialog-Ersatz für den Selbsttest: liefert vorbereitete Antworten und protokolliert Meldungen.</summary>
public sealed class ScriptedDialogService(Action<string> log) : IDialogService
{
    public Queue<object?> Answers { get; } = new();

    public List<string> Errors { get; } = [];

    public IReadOnlyList<string> OpenFiles(string title, string filter) => Next<IReadOnlyList<string>>() ?? [];

    public string? OpenFile(string title, string filter) => Next<string>();

    public string? SaveFile(string title, string filter, string defaultName, string? initialFolder = null) => Next<string>();

    public string? PickFolder(string title, string? initialFolder = null) => Next<string>();

    public void Info(string message, string title = "MovieWorker") => log($"Info: {message}");

    public void Error(string message, string title = "MovieWorker")
    {
        Errors.Add(message);
        log($"Meldung: {message}");
    }

    public bool Confirm(string message, string title = "MovieWorker") => Next<bool?>() ?? true;

    public DialogAnswer AskYesNoCancel(string message, string title = "MovieWorker") => Next<DialogAnswer?>() ?? DialogAnswer.No;

    public string? Prompt(string message, string title, string defaultValue) => Next<string>() ?? defaultValue;

    public bool ShowDialog(object viewModel)
    {
        log($"Dialog: {viewModel.GetType().Name}");
        return Next<bool?>() ?? false;
    }

    private T? Next<T>() => Answers.Count > 0 && Answers.Dequeue() is T value ? value : default;
}
