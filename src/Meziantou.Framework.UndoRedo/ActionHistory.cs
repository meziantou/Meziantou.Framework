namespace Meziantou.Framework.UndoRedo;

/// <summary>
/// Stores the recorded actions as an undo buffer and a redo buffer. Recording a new action clears
/// the redo buffer, mirroring the behavior of a typical undo/redo stack.
/// </summary>
/// <remarks>
/// The undo buffer is a list ordered from oldest to most recent so the oldest entries can be dropped
/// once <see cref="MaxDepth"/> is reached; a <see cref="Stack{T}"/> cannot remove its oldest element.
/// The snapshots handed out by <see cref="UndoableActions"/> and <see cref="RedoableActions"/> are built
/// once per change instead of once per read, so binding to them does not copy the buffer on every access.
/// </remarks>
internal sealed class ActionHistory(int maxDepth)
{
    private readonly List<IUndoRedoAction> _undo = [];
    private readonly List<IUndoRedoAction> _redo = [];

    private IReadOnlyList<IUndoRedoAction>? _undoSnapshot;
    private IReadOnlyList<IUndoRedoAction>? _redoSnapshot;

    public int MaxDepth { get; } = maxDepth;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// Records an action that is not merged into the previous one, dropping the oldest entries once
    /// <see cref="MaxDepth"/> is exceeded. The redo buffer is cleared as the history moved forward.
    /// </summary>
    public void Record(IUndoRedoAction action)
    {
        _undo.Add(action);
        if (_undo.Count > MaxDepth)
        {
            _undo.RemoveRange(0, _undo.Count - MaxDepth);
        }

        _redo.Clear();
        InvalidateSnapshots();
    }

    /// <summary>Clears the redo buffer without recording anything, for an action merged into the previous one.</summary>
    public void RecordMerged()
    {
        _redo.Clear();
        InvalidateSnapshots();
    }

    public IUndoRedoAction? PeekUndo() => _undo.Count > 0 ? _undo[^1] : null;

    public IUndoRedoAction? PeekRedo() => _redo.Count > 0 ? _redo[^1] : null;

    /// <summary>Moves the most recent undoable action to the redo buffer once it has been reverted.</summary>
    public void MoveTopUndoToRedo()
    {
        _redo.Add(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
        InvalidateSnapshots();
    }

    /// <summary>Moves the most recent redoable action back to the undo buffer once it has been re-executed.</summary>
    public void MoveTopRedoToUndo()
    {
        _undo.Add(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
        InvalidateSnapshots();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        InvalidateSnapshots();
    }

    public IReadOnlyList<IUndoRedoAction> UndoableActions => _undoSnapshot ??= CreateSnapshot(_undo);

    public IReadOnlyList<IUndoRedoAction> RedoableActions => _redoSnapshot ??= CreateSnapshot(_redo);

    private void InvalidateSnapshots()
    {
        _undoSnapshot = null;
        _redoSnapshot = null;
    }

    /// <summary>Copies a buffer into an immutable snapshot ordered from most recent to oldest.</summary>
    private static IUndoRedoAction[] CreateSnapshot(List<IUndoRedoAction> actions)
    {
        if (actions.Count == 0)
            return [];

        var snapshot = actions.ToArray();
        Array.Reverse(snapshot);
        return snapshot;
    }
}
