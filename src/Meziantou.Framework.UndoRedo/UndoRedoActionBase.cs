namespace Meziantou.Framework.UndoRedo;

/// <summary>
/// Base class for <see cref="IUndoRedoAction"/> implementations. It tracks how many times the action
/// has been executed so it cannot be executed or reverted twice in a row, and delegates the actual
/// work to <see cref="ExecuteCoreAsync"/> and <see cref="UnExecuteCoreAsync"/>.
/// </summary>
public abstract class UndoRedoActionBase : IUndoRedoAction
{
    /// <summary>Gets the number of times the action has been executed minus the number of times it has been reverted (0 or 1).</summary>
    protected int ExecuteCount { get; private set; }

    /// <inheritdoc />
    public bool AllowToMergeWithPrevious { get; set; }

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!CanExecute())
            return;

        await ExecuteCoreAsync(cancellationToken).ConfigureAwait(false);
        ExecuteCount++;
    }

    /// <summary>Contains the logic that applies the change. Called by <see cref="ExecuteAsync"/> when <see cref="CanExecute"/> is <see langword="true"/>.</summary>
    protected abstract ValueTask ExecuteCoreAsync(CancellationToken cancellationToken);

    /// <inheritdoc />
    public async ValueTask UnExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUnExecute())
            return;

        await UnExecuteCoreAsync(cancellationToken).ConfigureAwait(false);
        ExecuteCount--;
    }

    /// <summary>Contains the logic that reverts the change. Called by <see cref="UnExecuteAsync"/> when <see cref="CanUnExecute"/> is <see langword="true"/>.</summary>
    protected abstract ValueTask UnExecuteCoreAsync(CancellationToken cancellationToken);

    /// <inheritdoc />
    public bool CanExecute() => ExecuteCount == 0;

    /// <inheritdoc />
    public bool CanUnExecute() => !CanExecute();

    /// <inheritdoc />
    public virtual ValueTask<bool> TryToMergeAsync(IUndoRedoAction followingAction) => new(false);
}
