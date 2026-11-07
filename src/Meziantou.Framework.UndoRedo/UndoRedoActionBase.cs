namespace Meziantou.Framework.UndoRedo;

/// <summary>
/// Base class for <see cref="IUndoRedoAction"/> implementations. It tracks whether the action is currently
/// applied so it cannot be executed or reverted twice in a row, and delegates the actual work to
/// <see cref="ExecuteCoreAsync"/> and <see cref="UnExecuteCoreAsync"/>.
/// </summary>
public abstract class UndoRedoActionBase : IUndoRedoAction
{
    /// <summary>Gets a value indicating whether the action is currently applied.</summary>
    protected bool IsApplied { get; private set; }

    /// <inheritdoc />
    public bool AllowToMergeWithPrevious { get; set; }

    /// <inheritdoc />
    public virtual string? Description { get; set; }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The action is already applied. Recording the same instance twice would add an undo step that reverts nothing.</exception>
    public async ValueTask ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (IsApplied)
            throw new InvalidOperationException("The action is already applied. An action instance can only be recorded once at a time.");

        await ExecuteCoreAsync(cancellationToken).ConfigureAwait(false);
        IsApplied = true;
    }

    /// <summary>Contains the logic that applies the change. Called by <see cref="ExecuteAsync"/>.</summary>
    protected abstract ValueTask ExecuteCoreAsync(CancellationToken cancellationToken);

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The action is not currently applied.</exception>
    public async ValueTask UnExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!IsApplied)
            throw new InvalidOperationException("The action is not applied, so it cannot be reverted.");

        await UnExecuteCoreAsync(cancellationToken).ConfigureAwait(false);
        IsApplied = false;
    }

    /// <summary>Contains the logic that reverts the change. Called by <see cref="UnExecuteAsync"/>.</summary>
    protected abstract ValueTask UnExecuteCoreAsync(CancellationToken cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<bool> TryToMergeAsync(IUndoRedoAction followingAction, CancellationToken cancellationToken = default) => new(false);
}
