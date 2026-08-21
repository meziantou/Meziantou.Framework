namespace Meziantou.Framework.UndoRedo;

/// <summary>
/// Groups several actions into a single undoable/redoable unit. Create one with
/// <see cref="UndoRedoManager.CreateTransaction"/> and commit it (explicitly or by disposal) to
/// record it as a single step. Committing is all-or-nothing: if one of the actions fails, the
/// actions that already ran are reverted before the exception is propagated.
/// </summary>
/// <remarks>
/// Disposal commits the transaction, including when the scope is left because of an exception. Call
/// <see cref="RollbackAsync"/> explicitly to discard the actions recorded so far. Nested transactions
/// must be completed from the innermost out.
/// </remarks>
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
        if (_executed)
            return;

        for (var i = 0; i < _actions.Count; i++)
        {
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

        _executed = true;
    }

    /// <summary>
    /// Reverts every action of the transaction. All-or-nothing: when an action fails to revert, the actions that
    /// were already reverted are re-applied before the failure is propagated. An <see cref="AggregateException"/>
    /// is thrown if re-applying them also fails.
    /// </summary>
    internal async ValueTask UnExecuteCoreAsync(CancellationToken cancellationToken)
    {
        if (!_executed)
            return;

        for (var i = _actions.Count - 1; i >= 0; i--)
        {
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

        _executed = false;
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
    /// <exception cref="InvalidOperationException">The transaction is already completed, or it is not the innermost open transaction.</exception>
    public ValueTask CommitAsync(CancellationToken cancellationToken = default)
    {
        EnsureNotCompleted();

        return _manager.CommitTransactionAsync(this, cancellationToken);
    }

    /// <summary>Rolls back the transaction, reverting any actions that were already executed.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">The transaction is already completed, or it is not the innermost open transaction.</exception>
    public ValueTask RollbackAsync(CancellationToken cancellationToken = default)
    {
        EnsureNotCompleted();

        return _manager.RollbackTransactionAsync(this, cancellationToken);
    }

    /// <summary>
    /// Commits the transaction if it has not already been committed or rolled back. Note that this commits even
    /// when the scope is left because of an exception; call <see cref="RollbackAsync"/> explicitly to discard the
    /// actions recorded so far.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_completed)
            return;

        await CommitAsync().ConfigureAwait(false);
    }

    private void EnsureNotCompleted()
    {
        if (_completed)
            throw new InvalidOperationException("The transaction is already committed or rolled back.");
    }
}
