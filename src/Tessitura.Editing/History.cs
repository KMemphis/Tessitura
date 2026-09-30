using Tessitura.Core;

namespace Tessitura.Editing;

/// <summary>Maintains immutable score and selection snapshots for undo and redo.</summary>
public sealed class History
{
    private readonly Stack<HistoryEntry> _undo = new();
    private readonly Stack<HistoryEntry> _redo = new();

    /// <summary>Creates history from the initial score and selection.</summary>
    /// <param name="initialScore">The starting score snapshot.</param>
    /// <param name="initialSelection">The starting selection, or an empty selection.</param>
    public History(Score initialScore, Selection? initialSelection = null)
    {
        ArgumentNullException.ThrowIfNull(initialScore);
        CurrentScore = initialScore;
        CurrentSelection = initialSelection ?? Selection.Empty;
    }

    /// <summary>Gets the current score snapshot.</summary>
    public Score CurrentScore { get; private set; }

    /// <summary>Gets the selection associated with the current snapshot.</summary>
    public Selection CurrentSelection { get; private set; }

    /// <summary>Gets whether an earlier score snapshot can be restored.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Gets whether a later score snapshot can be restored.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Adds an edited snapshot and its selection to the history.</summary>
    /// <param name="next">The new score snapshot.</param>
    /// <param name="description">The description of the edit.</param>
    /// <param name="selection">The selection associated with the new snapshot.</param>
    public void Push(Score next, string description, Selection selection)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(selection);

        _undo.Push(new HistoryEntry(CurrentScore, CurrentSelection, description));
        CurrentScore = next;
        CurrentSelection = selection;
        _redo.Clear();
    }

    /// <summary>Restores the previous score and selection, or returns the current score when empty.</summary>
    /// <returns>The restored score snapshot.</returns>
    public Score Undo()
    {
        if (_undo.TryPop(out HistoryEntry? entry))
        {
            _redo.Push(new HistoryEntry(CurrentScore, CurrentSelection, entry.Description));
            CurrentScore = entry.Score;
            CurrentSelection = entry.Selection;
        }

        return CurrentScore;
    }

    /// <summary>Restores the next score and selection, or returns the current score when empty.</summary>
    /// <returns>The restored score snapshot.</returns>
    public Score Redo()
    {
        if (_redo.TryPop(out HistoryEntry? entry))
        {
            _undo.Push(new HistoryEntry(CurrentScore, CurrentSelection, entry.Description));
            CurrentScore = entry.Score;
            CurrentSelection = entry.Selection;
        }

        return CurrentScore;
    }

    private sealed record HistoryEntry(Score Score, Selection Selection, string Description);
}
