namespace Meziantou.Framework.UndoRedo;

/// <summary>
/// Records actions and provides asynchronous undo/redo capabilities. Actions can be grouped into
/// transactions and consecutive actions can be merged into a single undo step.
/// </summary>
/// <remarks>This type is not thread-safe; all operations are expected to run on a single logical flow.</remarks>
/// <example>
/// <code>
/// var manager = new UndoRedoManager();
/// var list = new List&lt;int&gt;();
///
/// // Asynchronous execute and unexecute delegates
/// await manager.RecordActionAsync(
///     execute: ct => { list.Add(1); return ValueTask.CompletedTask; },
///     unexecute: ct => { list.RemoveAt(list.Count - 1); return ValueTask.CompletedTask; });
///
/// // Synchronous execute and unexecute delegates
/// await manager.RecordActionAsync(
///     execute: () => list.Add(1),
///     unexecute: () => list.RemoveAt(list.Count - 1));
///
/// await manager.UndoAsync();
/// await manager.RedoAsync();
/// </code>
/// </example>
public sealed class UndoRedoManager
{
    private readonly ActionHistory _history = new();
    private readonly Stack<UndoRedoTransaction> _transactions = new();

    /// <summary>Gets a value indicating whether an action is currently being executed, undone, or redone.</summary>
    public bool ActionIsExecuting { get; private set; }

    /// <summary>Gets a value indicating whether there is at least one action that can be undone.</summary>
    public bool CanUndo => _history.CanUndo;

    /// <summary>Gets a value indicating whether there is at least one action that can be redone.</summary>
    public bool CanRedo => _history.CanRedo;

    /// <summary>Occurs when the undo/redo buffers change (an action is recorded, undone, redone, or the history is cleared).</summary>
    public event EventHandler? CollectionChanged;

    /// <summary>Executes an action and records it so it can later be undone.</summary>
    /// <param name="action">The action to execute and record.</param>
    /// <param name="cancellationToken">A token to cancel the execution.</param>
    public async ValueTask RecordActionAsync(IUndoRedoAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_transactions.Count > 0)
        {
            var transaction = _transactions.Peek();
            transaction.Add(action);
            if (!transaction.IsDelayed)
            {
                await ExecuteAsync(action, cancellationToken).ConfigureAwait(false);
                transaction.MarkExecuted();
            }

            return;
        }

        await RecordActionCoreAsync(action, execute: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Executes an asynchronous execute delegate and an asynchronous unexecute delegate as a single action and records it so it can later be undone.</summary>
    /// <param name="execute">The delegate invoked to apply the change.</param>
    /// <param name="unexecute">The delegate invoked to revert the change.</param>
    /// <param name="cancellationToken">A token to cancel the execution.</param>
    public ValueTask RecordActionAsync(Func<CancellationToken, ValueTask> execute, Func<CancellationToken, ValueTask> unexecute, CancellationToken cancellationToken = default)
        => RecordActionAsync(new UndoRedoDelegateAction(execute, unexecute), cancellationToken);

    /// <summary>Executes an asynchronous execute delegate and a synchronous unexecute delegate as a single action and records it so it can later be undone.</summary>
    /// <param name="execute">The delegate invoked to apply the change.</param>
    /// <param name="unexecute">The delegate invoked to revert the change.</param>
    /// <param name="cancellationToken">A token to cancel the execution.</param>
    public ValueTask RecordActionAsync(Func<CancellationToken, ValueTask> execute, Action unexecute, CancellationToken cancellationToken = default)
        => RecordActionAsync(new UndoRedoDelegateAction(execute, unexecute), cancellationToken);

    /// <summary>Executes a synchronous execute delegate and an asynchronous unexecute delegate as a single action and records it so it can later be undone.</summary>
    /// <param name="execute">The delegate invoked to apply the change.</param>
    /// <param name="unexecute">The delegate invoked to revert the change.</param>
    /// <param name="cancellationToken">A token to cancel the execution.</param>
    public ValueTask RecordActionAsync(Action execute, Func<CancellationToken, ValueTask> unexecute, CancellationToken cancellationToken = default)
        => RecordActionAsync(new UndoRedoDelegateAction(execute, unexecute), cancellationToken);

    /// <summary>Executes a synchronous execute delegate and a synchronous unexecute delegate as a single action and records it so it can later be undone.</summary>
    /// <param name="execute">The delegate invoked to apply the change.</param>
    /// <param name="unexecute">The delegate invoked to revert the change.</param>
    /// <param name="cancellationToken">A token to cancel the execution.</param>
    public ValueTask RecordActionAsync(Action execute, Action unexecute, CancellationToken cancellationToken = default)
        => RecordActionAsync(new UndoRedoDelegateAction(execute, unexecute), cancellationToken);

    /// <summary>Reverts the most recently recorded action.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async ValueTask UndoAsync(CancellationToken cancellationToken = default)
    {
        if (_transactions.Count > 0)
            throw new InvalidOperationException("Cannot undo while a transaction is open.");

        if (_history.PeekUndo() is not { } action)
            return;

        ActionIsExecuting = true;
        try
        {
            await action.UnExecuteAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ActionIsExecuting = false;
        }

        _history.MoveTopUndoToRedo();
        OnCollectionChanged();
    }

    /// <summary>Re-executes the most recently undone action.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async ValueTask RedoAsync(CancellationToken cancellationToken = default)
    {
        if (_transactions.Count > 0)
            throw new InvalidOperationException("Cannot redo while a transaction is open.");

        if (_history.PeekRedo() is not { } action)
            return;

        ActionIsExecuting = true;
        try
        {
            await action.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ActionIsExecuting = false;
        }

        _history.MoveTopRedoToUndo();
        OnCollectionChanged();
    }

    /// <summary>Empties both the undo and redo buffers.</summary>
    public void Clear()
    {
        if (_transactions.Count > 0)
            throw new InvalidOperationException("Cannot clear the history while a transaction is open.");

        _history.Clear();
        OnCollectionChanged();
    }

    /// <summary>Gets the actions that can be undone, most recent first.</summary>
    public IEnumerable<IUndoRedoAction> UndoableActions => _history.UndoableActions;

    /// <summary>Gets the actions that can be redone, most recent first.</summary>
    public IEnumerable<IUndoRedoAction> RedoableActions => _history.RedoableActions;

    /// <summary>Begins a transaction that groups subsequent recorded actions into a single undo step.</summary>
    /// <param name="isDelayed">
    /// When <see langword="true"/> (the default), actions added to the transaction are executed only when the
    /// transaction is committed. When <see langword="false"/>, actions are executed as soon as they are recorded.
    /// </param>
    /// <returns>A transaction that should be committed or rolled back. Disposing it commits the transaction unless it was rolled back.</returns>
    public UndoRedoTransaction CreateTransaction(bool isDelayed = true)
    {
        var transaction = new UndoRedoTransaction(this, isDelayed);
        _transactions.Push(transaction);
        return transaction;
    }

    /// <summary>Commits the innermost open transaction, recording it as a single undo step (or merging it into its parent transaction).</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async ValueTask CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transactions.Count == 0)
            throw new InvalidOperationException("There is no transaction to commit.");

        var transaction = _transactions.Pop();
        transaction.MarkCompleted();

        if (!transaction.HasActions)
            return;

        // When the transaction is not delayed, its actions have already been executed.
        var execute = transaction.IsDelayed;

        if (_transactions.Count > 0)
        {
            var parent = _transactions.Peek();
            parent.Add(transaction);
            if (execute && !parent.IsDelayed)
            {
                await ExecuteAsync(transaction, cancellationToken).ConfigureAwait(false);
                parent.MarkExecuted();
            }

            return;
        }

        await RecordActionCoreAsync(transaction, execute, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Rolls back the innermost open transaction, reverting any actions that were already executed.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async ValueTask RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transactions.Count == 0)
            throw new InvalidOperationException("There is no transaction to roll back.");

        var transaction = _transactions.Pop();
        transaction.MarkCompleted();

        ActionIsExecuting = true;
        try
        {
            await transaction.UnExecuteAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ActionIsExecuting = false;
        }
    }

    private async ValueTask RecordActionCoreAsync(IUndoRedoAction action, bool execute, CancellationToken cancellationToken)
    {
        if (execute)
            await ExecuteAsync(action, cancellationToken).ConfigureAwait(false);

        if (action.AllowToMergeWithPrevious && _history.PeekUndo() is { } previous && await previous.TryToMergeAsync(action).ConfigureAwait(false))
        {
            OnCollectionChanged();
            return;
        }

        _history.Record(action);
        OnCollectionChanged();
    }

    private async ValueTask ExecuteAsync(IUndoRedoAction action, CancellationToken cancellationToken)
    {
        ActionIsExecuting = true;
        try
        {
            await action.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ActionIsExecuting = false;
        }
    }

    private void OnCollectionChanged() => CollectionChanged?.Invoke(this, EventArgs.Empty);
}
