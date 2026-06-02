namespace Meziantou.Framework.UndoRedo;

/// <summary>
/// Stores the recorded actions as an undo buffer and a redo buffer. Recording a new action clears
/// the redo buffer, mirroring the behavior of a typical undo/redo stack.
/// </summary>
internal sealed class ActionHistory
{
    private readonly Stack<IUndoRedoAction> _undo = new();
    private readonly Stack<IUndoRedoAction> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Record(IUndoRedoAction action)
    {
        _undo.Push(action);
        _redo.Clear();
    }

    public IUndoRedoAction? PeekUndo() => _undo.Count > 0 ? _undo.Peek() : null;

    public IUndoRedoAction? PeekRedo() => _redo.Count > 0 ? _redo.Peek() : null;

    /// <summary>Moves the most recent undoable action to the redo buffer once it has been reverted.</summary>
    public void MoveTopUndoToRedo() => _redo.Push(_undo.Pop());

    /// <summary>Moves the most recent redoable action back to the undo buffer once it has been re-executed.</summary>
    public void MoveTopRedoToUndo() => _undo.Push(_redo.Pop());

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    public IEnumerable<IUndoRedoAction> UndoableActions => _undo;

    public IEnumerable<IUndoRedoAction> RedoableActions => _redo;
}
