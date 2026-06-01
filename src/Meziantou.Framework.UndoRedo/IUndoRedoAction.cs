namespace Meziantou.Framework.UndoRedo;

/// <summary>
/// Represents a single undoable/redoable action. Implementations encapsulate both how to apply
/// a change (<see cref="ExecuteAsync"/>) and how to revert it (<see cref="UnExecuteAsync"/>).
/// </summary>
public interface IUndoRedoAction
{
    /// <summary>Applies the change encapsulated by this action.</summary>
    ValueTask ExecuteAsync(CancellationToken cancellationToken = default);

    /// <summary>Reverts the change previously applied by <see cref="ExecuteAsync"/>.</summary>
    ValueTask UnExecuteAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets a value indicating whether the action can currently be executed.</summary>
    bool CanExecute();

    /// <summary>Gets a value indicating whether the action can currently be reverted.</summary>
    bool CanUnExecute();

    /// <summary>
    /// Attempts to merge a following action into this one instead of recording it as a separate
    /// step. Useful for long chains of consecutive operations of the same kind (e.g. dragging or typing).
    /// </summary>
    /// <param name="followingAction">The action recorded right after this one.</param>
    /// <returns><see langword="true"/> if <paramref name="followingAction"/> was merged into this action; otherwise <see langword="false"/>.</returns>
    ValueTask<bool> TryToMergeAsync(IUndoRedoAction followingAction);

    /// <summary>Gets a value indicating whether this action may be merged with the previous action in the undo buffer.</summary>
    bool AllowToMergeWithPrevious { get; }
}
