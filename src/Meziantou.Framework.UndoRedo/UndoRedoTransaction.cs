namespace Meziantou.Framework.UndoRedo;

/// <summary>
/// Groups several actions into a single undoable/redoable unit. Create one with
/// <see cref="UndoRedoManager.CreateTransaction"/> and commit it to record it as a single step.
/// Committing is all-or-nothing: if one of the actions fails, the actions that already ran are
/// reverted before the exception is propagated.
/// </summary>
/// <remarks>
/// Disposal rolls the transaction back unless <see cref="CommitAsync"/> was called, so leaving the scope
/// because of an exception discards the work instead of applying it. Nested transactions must use the same
/// <see cref="TransactionExecutionMode"/> as the transaction they are nested in, and must be completed from
/// the innermost out.
/// </remarks>
/// <example>
/// <code>
/// await using (var transaction = manager.CreateTransaction())
/// {
///     await manager.RecordActionAsync(addFirst, removeFirst);
///     await manager.RecordActionAsync(addSecond, removeSecond);
///     await transaction.CommitAsync();
/// } // a single UndoAsync reverts both actions
/// </code>
/// </example>
public sealed class UndoRedoTransaction : IUndoRedoAction, IAsyncDisposable
{
    private readonly UndoRedoManager _manager;
    private readonly List<IUndoRedoAction> _actions = [];
    private bool _completed;
    private bool _applied;

    internal UndoRedoTransaction(UndoRedoManager manager, TransactionExecutionMode mode)
    {
        _manager = manager;
        Mode = mode;
    }

    /// <summary>Gets a value indicating when the actions recorded in this transaction are executed.</summary>
    public TransactionExecutionMode Mode { get; }

    /// <summary>Gets a value indicating whether the transaction has been committed or rolled back.</summary>
    public bool IsCompleted => _completed;

    /// <inheritdoc />
    public bool AllowToMergeWithPrevious { get; set; }

    /// <inheritdoc />
    public string? Description { get; set; }

    internal UndoRedoManager Manager => _manager;

    internal bool HasActions => _actions.Count > 0;

    internal IUndoRedoAction? LastAction => _actions.Count > 0 ? _actions[^1] : null;

    /// <summary>
    /// Adds an action to the transaction. In <see cref="TransactionExecutionMode.Immediate"/> mode the action
    /// has already been applied by the manager, which is what makes the transaction itself applied — this is
    /// the only place that state comes from, so a transaction that merely holds applied children still reverts
    /// them on undo or rollback.
    /// </summary>
    internal void Add(IUndoRedoAction action)
    {
        _actions.Add(action);
        if (Mode is TransactionExecutionMode.Immediate)
        {
            _applied = true;
        }
    }

    internal void MarkCompleted() => _completed = true;

    /// <inheritdoc />
    ValueTask IUndoRedoAction.ExecuteAsync(CancellationToken cancellationToken) => ExecuteCoreAsync(cancellationToken);

    /// <inheritdoc />
    ValueTask IUndoRedoAction.UnExecuteAsync(CancellationToken cancellationToken) => UnExecuteCoreAsync(cancellationToken);

    /// <inheritdoc />
    ValueTask<bool> IUndoRedoAction.TryToMergeAsync(IUndoRedoAction followingAction, CancellationToken cancellationToken) => new(false);

    /// <summary>
    /// Applies every action of the transaction. All-or-nothing: when an action fails, the actions that already ran
    /// are reverted before the failure is propagated. An <see cref="AggregateException"/> is thrown if reverting
    /// them also fails.
    /// </summary>
    internal async ValueTask ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        if (_applied)
            return;

        for (var i = 0; i < _actions.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await _actions[i].ExecuteAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                await CompensateAsync(exception, () => RevertRangeAsync(0, i - 1)).ConfigureAwait(false);
                throw;
            }
        }

        _applied = true;
    }

    /// <summary>
    /// Reverts every action of the transaction. All-or-nothing: when an action fails to revert, the actions that
    /// were already reverted are re-applied before the failure is propagated. An <see cref="AggregateException"/>
    /// is thrown if re-applying them also fails.
    /// </summary>
    internal async ValueTask UnExecuteCoreAsync(CancellationToken cancellationToken)
    {
        if (!_applied)
            return;

        for (var i = _actions.Count - 1; i >= 0; i--)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await _actions[i].UnExecuteAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                await CompensateAsync(exception, () => ReapplyRangeAsync(i + 1, _actions.Count - 1)).ConfigureAwait(false);
                throw;
            }
        }

        _applied = false;
    }

    private static async ValueTask CompensateAsync(Exception exception, Func<ValueTask> compensate)
    {
        try
        {
            await compensate().ConfigureAwait(false);
        }
        catch (Exception compensationException)
        {
            throw new AggregateException(exception, compensationException);
        }
    }

    /// <summary>Reverts the actions in <c>[firstIndex, lastIndex]</c>, most recent first.</summary>
    private async ValueTask RevertRangeAsync(int firstIndex, int lastIndex)
    {
        for (var i = lastIndex; i >= firstIndex; i--)
        {
            // The compensation must run to completion even when the failure was a cancellation.
            await _actions[i].UnExecuteAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Re-applies the actions in <c>[firstIndex, lastIndex]</c>, oldest first.</summary>
    private async ValueTask ReapplyRangeAsync(int firstIndex, int lastIndex)
    {
        for (var i = firstIndex; i <= lastIndex; i++)
        {
            await _actions[i].ExecuteAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Commits the transaction, recording it as a single undo step.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">The transaction is already completed, it is not the innermost open transaction, or an action is currently executing.</exception>
    /// <exception cref="AggregateException">An action failed and reverting the actions that already ran failed too, leaving them applied.</exception>
    public ValueTask CommitAsync(CancellationToken cancellationToken = default)
    {
        EnsureNotCompleted();

        return _manager.CommitTransactionAsync(this, cancellationToken);
    }

    /// <summary>Rolls back the transaction, reverting any actions that were already executed.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <remarks>The transaction stays open if the rollback fails, so it can be retried once the cause is resolved.</remarks>
    /// <exception cref="InvalidOperationException">The transaction is already completed, it is not the innermost open transaction, or an action is currently executing.</exception>
    /// <exception cref="AggregateException">An action failed to revert and re-applying the actions that were already reverted failed too.</exception>
    public ValueTask RollbackAsync(CancellationToken cancellationToken = default)
    {
        EnsureNotCompleted();

        return _manager.RollbackTransactionAsync(this, cancellationToken);
    }

    /// <summary>
    /// Rolls the transaction back if it has not already been committed or rolled back, so leaving the scope
    /// because of an exception discards the recorded actions instead of applying them. Call
    /// <see cref="CommitAsync"/> to keep them.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_completed)
            return;

        await RollbackAsync().ConfigureAwait(false);
    }

    private void EnsureNotCompleted()
    {
        if (_completed)
            throw new InvalidOperationException("The transaction is already committed or rolled back.");
    }
}
