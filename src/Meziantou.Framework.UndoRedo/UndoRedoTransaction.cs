namespace Meziantou.Framework.UndoRedo;

/// <summary>
/// Groups several actions into a single undoable/redoable unit. Create one with
/// <see cref="UndoRedoManager.CreateTransaction"/> and commit it (explicitly or by disposal) to
/// record it as a single step.
/// </summary>
/// <example>
/// <code>
/// await using (manager.CreateTransaction())
/// {
///     await manager.RecordActionAsync(addFirst, removeFirst);
///     await manager.RecordActionAsync(addSecond, removeSecond);
/// } // committed on dispose: a single UndoAsync reverts both actions
/// </code>
/// </example>
public sealed class UndoRedoTransaction : IUndoRedoAction, IAsyncDisposable
{
    private readonly UndoRedoManager _manager;
    private readonly List<IUndoRedoAction> _actions = [];
    private bool _completed;
    private bool _executed;

    internal UndoRedoTransaction(UndoRedoManager manager, bool isDelayed)
    {
        _manager = manager;
        IsDelayed = isDelayed;
    }

    /// <summary>Gets a value indicating whether the actions are executed on commit (<see langword="true"/>) or as they are recorded (<see langword="false"/>).</summary>
    internal bool IsDelayed { get; }

    internal bool HasActions => _actions.Count > 0;

    /// <inheritdoc />
    public bool AllowToMergeWithPrevious { get; set; }

    internal void Add(IUndoRedoAction action) => _actions.Add(action);

    internal void MarkExecuted() => _executed = true;

    internal void MarkCompleted() => _completed = true;

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (_executed)
            return;

        foreach (var action in _actions)
        {
            await action.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }

        _executed = true;
    }

    /// <inheritdoc />
    public async ValueTask UnExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!_executed)
            return;

        for (var i = _actions.Count - 1; i >= 0; i--)
        {
            await _actions[i].UnExecuteAsync(cancellationToken).ConfigureAwait(false);
        }

        _executed = false;
    }

    /// <inheritdoc />
    public bool CanExecute() => !_executed;

    /// <inheritdoc />
    public bool CanUnExecute() => _executed;

    /// <inheritdoc />
    public ValueTask<bool> TryToMergeAsync(IUndoRedoAction followingAction) => new(false);

    /// <summary>Commits the transaction, recording it as a single undo step.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public ValueTask CommitAsync(CancellationToken cancellationToken = default) => _manager.CommitTransactionAsync(cancellationToken);

    /// <summary>Rolls back the transaction, reverting any actions that were already executed.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public ValueTask RollbackAsync(CancellationToken cancellationToken = default) => _manager.RollbackTransactionAsync(cancellationToken);

    /// <summary>Commits the transaction if it has not already been committed or rolled back.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_completed)
            return;

        await CommitAsync().ConfigureAwait(false);
    }
}
