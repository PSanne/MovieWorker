namespace MovieWorker.Core.Timeline;

/// <summary>
/// Rückgängig/Wiederholen für die Timeline. Vor jeder Änderung wird eine Kopie des Projekts gesichert.
/// Das ist robust (jede Aktion lässt sich zurücknehmen, auch komplexe) und speichersparend, weil nur
/// Schnittdaten kopiert werden – die Videodateien selbst werden nie verändert.
/// </summary>
public sealed class TimelineHistory
{
    private readonly LinkedList<(MovieProject State, string Description)> _undo = new();
    private readonly Stack<(MovieProject State, string Description)> _redo = new();

    public TimelineHistory(int capacity = 100)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        Capacity = capacity;
    }

    public int Capacity { get; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? UndoDescription => _undo.Last?.Value.Description;

    public string? RedoDescription => _redo.TryPeek(out var e) ? e.Description : null;

    /// <summary>Merkt sich den Zustand <b>vor</b> einer Änderung.</summary>
    public void Record(MovieProject before, string description)
    {
        _undo.AddLast((before.Clone(), description));
        _redo.Clear();
        while (_undo.Count > Capacity)
        {
            _undo.RemoveFirst();
        }
    }

    public MovieProject? Undo(MovieProject current)
    {
        if (_undo.Last is null)
        {
            return null;
        }

        var entry = _undo.Last.Value;
        _undo.RemoveLast();
        _redo.Push((current.Clone(), entry.Description));
        return entry.State;
    }

    public MovieProject? Redo(MovieProject current)
    {
        if (!_redo.TryPop(out var entry))
        {
            return null;
        }

        _undo.AddLast((current.Clone(), entry.Description));
        return entry.State;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
