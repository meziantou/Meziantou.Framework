namespace Meziantou.Framework.UndoRedo;

/// <summary>
/// Represents a single undoable/redoable action. Implementations encapsulate both how to apply
/// a change (<see cref="ExecuteAsync"/>) and how to revert it (<see cref="UnExecuteAsync"/>).
/// </summary>
/// <remarks>
/// <see cref="UndoRedoManager"/> alternates the two methods: it never applies an action that is already
/// applied, nor reverts one that is not.
/// </remarks>
public interface IUndoRedoAction
{
    /// <summary>Applies the change encapsulated by this action.</summary>
    ValueTask ExecuteAsync(CancellationToken cancellationToken = default);

    /// <summary>Reverts the change previously applied by <see cref="ExecuteAsync"/>.</summary>
    ValueTask UnExecuteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to merge a following action into this one instead of recording it as a separate
    /// step. Useful for long chains of consecutive operations of the same kind (e.g. dragging or typing).
    /// </summary>
    /// <param name="followingAction">The action recorded right after this one.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> if <paramref name="followingAction"/> was merged into this action; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// <para>
    /// <paramref name="followingAction"/> is in the same applied state as this action: both are already
    /// applied at the top level and inside an <see cref="TransactionExecutionMode.Immediate"/> transaction,
    /// and neither is applied inside a <see cref="TransactionExecutionMode.Deferred"/> one. An implementation
    /// must therefore absorb <paramref name="followingAction"/>'s effect without applying it.
    /// </para>
    /// <para>
    /// Returning <see langword="true"/> discards <paramref name="followingAction"/>: it is never executed nor
    /// reverted by the manager again, so this action becomes responsible for applying and reverting both
    /// effects from its own <see cref="ExecuteAsync"/> and <see cref="UnExecuteAsync"/>.
    /// </para>
    /// </remarks>
    ValueTask<bool> TryToMergeAsync(IUndoRedoAction followingAction, CancellationToken cancellationToken = default);

    /// <summary>Gets a value indicating whether this action may be merged with the previous action in the undo buffer.</summary>
    bool AllowToMergeWithPrevious { get; }

    /// <summary>Gets a human-readable description of the action, for display in an undo history. Defaults to <see langword="null"/>.</summary>
    string? Description => null;
}
