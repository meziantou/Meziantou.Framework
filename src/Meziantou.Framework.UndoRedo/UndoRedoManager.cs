using System.ComponentModel;
using System.Runtime.ExceptionServices;

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
public sealed class UndoRedoManager : INotifyPropertyChanged
{
    private readonly ActionHistory _history;

    // A list rather than a stack so the transaction being completed can reach the one enclosing it.
    private readonly List<UndoRedoTransaction> _transactions = [];

    /// <summary>Initializes a new instance of the <see cref="UndoRedoManager"/> class with an unbounded history.</summary>
    public UndoRedoManager()
        : this(int.MaxValue)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UndoRedoManager"/> class keeping at most <paramref name="maxHistoryDepth"/> undo steps.</summary>
    /// <param name="maxHistoryDepth">The maximum number of undo steps to keep. Once exceeded, the oldest steps are dropped and can no longer be undone.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxHistoryDepth"/> is not strictly positive.</exception>
    public UndoRedoManager(int maxHistoryDepth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxHistoryDepth, 1);

        _history = new ActionHistory(maxHistoryDepth);
    }

    /// <summary>Gets the maximum number of undo steps kept in the history.</summary>
    public int MaxHistoryDepth => _history.MaxDepth;

    /// <summary>Gets a value indicating whether an action is currently being executed, undone, or redone.</summary>
    /// <remarks>
    /// While this is <see langword="true"/>, any operation that changes the history throws an
    /// <see cref="InvalidOperationException"/>. An action must not record, undo, or redo anything itself.
    /// </remarks>
    public bool ActionIsExecuting { get; private set; }

    /// <summary>Gets the number of transactions that are currently open.</summary>
    /// <remarks>Undo, redo and <see cref="Clear"/> are unavailable while this is greater than zero.</remarks>
    public int TransactionDepth => _transactions.Count;

    /// <summary>Gets a value indicating whether <see cref="UndoAsync"/> would revert an action right now.</summary>
    /// <remarks>This is <see langword="false"/> while a transaction is open or an action is executing, so it can be bound directly to a command's availability.</remarks>
    public bool CanUndo => _transactions.Count == 0 && !ActionIsExecuting && _history.CanUndo;

    /// <summary>Gets a value indicating whether <see cref="RedoAsync"/> would re-execute an action right now.</summary>
    /// <remarks>This is <see langword="false"/> while a transaction is open or an action is executing, so it can be bound directly to a command's availability.</remarks>
    public bool CanRedo => _transactions.Count == 0 && !ActionIsExecuting && _history.CanRedo;

    /// <summary>Occurs when the undo/redo buffers change (an action is recorded, undone, redone, or the history is cleared).</summary>
    /// <remarks>A handler that throws surfaces to the caller as an <see cref="UndoRedoNotificationException"/>, after the operation itself has completed.</remarks>
    public event EventHandler? HistoryChanged;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Executes an action and records it so it can later be undone.</summary>
    /// <param name="action">The action to execute and record.</param>
    /// <param name="cancellationToken">A token to cancel the execution.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">An action is currently executing, or <paramref name="action"/> is a transaction that is still open or belongs to another manager.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
    /// <exception cref="UndoRedoNotificationException">A <see cref="HistoryChanged"/> handler failed. The action was recorded.</exception>
    public async ValueTask RecordActionAsync(IUndoRedoAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        EnsureNoActionIsExecuting();
        EnsureCanBeRecorded(action);
        cancellationToken.ThrowIfCancellationRequested();

        if (_transactions.Count > 0)
        {
            var transaction = _transactions[^1];

            // An action that fails to execute must not become part of the transaction, otherwise it
            // would be reverted on rollback and executed for the first time on redo.
            if (transaction.Mode is TransactionExecutionMode.Immediate)
            {
                await ExecuteAsync(action, cancellationToken).ConfigureAwait(false);
            }

            var (merged, mergeFailure) = await TryToMergeAsync(transaction.LastAction, action, cancellationToken).ConfigureAwait(false);
            if (!merged)
            {
                transaction.Add(action);
            }

            mergeFailure?.Throw();
            return;
        }

        await RecordActionCoreAsync(action, execute: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Executes an asynchronous execute delegate and an asynchronous unexecute delegate as a single action and records it so it can later be undone.</summary>
    /// <param name="execute">The delegate invoked to apply the change.</param>
    /// <param name="unexecute">The delegate invoked to revert the change.</param>
    /// <param name="cancellationToken">A token to cancel the execution.</param>
    /// <exception cref="ArgumentNullException"><paramref name="execute"/> or <paramref name="unexecute"/> is <see langword="null"/>.</exception>
    public ValueTask RecordActionAsync(Func<CancellationToken, ValueTask> execute, Func<CancellationToken, ValueTask> unexecute, CancellationToken cancellationToken = default)
        => RecordActionAsync(new UndoRedoDelegateAction(execute, unexecute), cancellationToken);

    /// <summary>Executes an asynchronous execute delegate and a synchronous unexecute delegate as a single action and records it so it can later be undone.</summary>
    /// <param name="execute">The delegate invoked to apply the change.</param>
    /// <param name="unexecute">The delegate invoked to revert the change.</param>
    /// <param name="cancellationToken">A token to cancel the execution.</param>
    /// <exception cref="ArgumentNullException"><paramref name="execute"/> or <paramref name="unexecute"/> is <see langword="null"/>.</exception>
    public ValueTask RecordActionAsync(Func<CancellationToken, ValueTask> execute, Action unexecute, CancellationToken cancellationToken = default)
        => RecordActionAsync(new UndoRedoDelegateAction(execute, unexecute), cancellationToken);

    /// <summary>Executes a synchronous execute delegate and an asynchronous unexecute delegate as a single action and records it so it can later be undone.</summary>
    /// <param name="execute">The delegate invoked to apply the change.</param>
    /// <param name="unexecute">The delegate invoked to revert the change.</param>
    /// <param name="cancellationToken">A token to cancel the execution.</param>
    /// <exception cref="ArgumentNullException"><paramref name="execute"/> or <paramref name="unexecute"/> is <see langword="null"/>.</exception>
    public ValueTask RecordActionAsync(Action execute, Func<CancellationToken, ValueTask> unexecute, CancellationToken cancellationToken = default)
        => RecordActionAsync(new UndoRedoDelegateAction(execute, unexecute), cancellationToken);

    /// <summary>Executes a synchronous execute delegate and a synchronous unexecute delegate as a single action and records it so it can later be undone.</summary>
    /// <param name="execute">The delegate invoked to apply the change.</param>
    /// <param name="unexecute">The delegate invoked to revert the change.</param>
    /// <param name="cancellationToken">A token to cancel the execution.</param>
    /// <exception cref="ArgumentNullException"><paramref name="execute"/> or <paramref name="unexecute"/> is <see langword="null"/>.</exception>
    public ValueTask RecordActionAsync(Action execute, Action unexecute, CancellationToken cancellationToken = default)
        => RecordActionAsync(new UndoRedoDelegateAction(execute, unexecute), cancellationToken);

    /// <summary>Reverts the most recently recorded action. Does nothing when there is none.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">An action is currently executing, or a transaction is open.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
    /// <exception cref="AggregateException">A grouped action failed to revert and re-applying the actions already reverted failed too.</exception>
    /// <exception cref="UndoRedoNotificationException">A <see cref="HistoryChanged"/> handler failed. The action was reverted.</exception>
    public async ValueTask UndoAsync(CancellationToken cancellationToken = default)
    {
        EnsureNoActionIsExecuting();
        EnsureNoOpenTransaction("undo");
        cancellationToken.ThrowIfCancellationRequested();

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
        OnHistoryChanged();
    }

    /// <summary>Re-executes the most recently undone action. Does nothing when there is none.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">An action is currently executing, or a transaction is open.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled.</exception>
    /// <exception cref="AggregateException">A grouped action failed and reverting the actions that already ran failed too.</exception>
    /// <exception cref="UndoRedoNotificationException">A <see cref="HistoryChanged"/> handler failed. The action was re-executed.</exception>
    public async ValueTask RedoAsync(CancellationToken cancellationToken = default)
    {
        EnsureNoActionIsExecuting();
        EnsureNoOpenTransaction("redo");
        cancellationToken.ThrowIfCancellationRequested();

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
        OnHistoryChanged();
    }

    /// <summary>Empties both the undo and redo buffers.</summary>
    /// <exception cref="InvalidOperationException">An action is currently executing, or a transaction is open.</exception>
    /// <exception cref="UndoRedoNotificationException">A <see cref="HistoryChanged"/> handler failed. The history was cleared.</exception>
    public void Clear()
    {
        EnsureNoActionIsExecuting();
        EnsureNoOpenTransaction("clear the history");

        _history.Clear();
        OnHistoryChanged();
    }

    /// <summary>Gets a snapshot of the actions that can be undone, most recent first.</summary>
    /// <remarks>The snapshot is rebuilt when the history changes rather than on every access, so an instance obtained earlier stays unchanged.</remarks>
    public IReadOnlyList<IUndoRedoAction> UndoableActions => _history.UndoableActions;

    /// <summary>Gets a snapshot of the actions that can be redone, most recent first.</summary>
    /// <remarks>The snapshot is rebuilt when the history changes rather than on every access, so an instance obtained earlier stays unchanged.</remarks>
    public IReadOnlyList<IUndoRedoAction> RedoableActions => _history.RedoableActions;

    /// <summary>Begins a transaction that groups subsequent recorded actions into a single undo step.</summary>
    /// <param name="mode">
    /// Whether the recorded actions run when the transaction is committed (<see cref="TransactionExecutionMode.Deferred"/>,
    /// the default) or as they are recorded (<see cref="TransactionExecutionMode.Immediate"/>).
    /// </param>
    /// <returns>
    /// A transaction that must be committed with <see cref="UndoRedoTransaction.CommitAsync"/> to be kept. Disposing it
    /// rolls it back unless it was already completed. Nested transactions must use the same <paramref name="mode"/> as the
    /// transaction they are nested in, and must be completed from the innermost out.
    /// </returns>
    /// <exception cref="InvalidOperationException">An action is currently executing, or <paramref name="mode"/> differs from the enclosing transaction's mode.</exception>
    public UndoRedoTransaction CreateTransaction(TransactionExecutionMode mode = TransactionExecutionMode.Deferred)
    {
        EnsureNoActionIsExecuting();

        // Mixing the two modes would execute a nested immediate action before the deferred actions recorded
        // before it, so undoing the group would not be the inverse of applying it.
        if (_transactions.Count > 0 && _transactions[^1].Mode != mode)
            throw new InvalidOperationException($"Cannot create a {mode} transaction inside a {_transactions[^1].Mode} one. Nested transactions must use the same execution mode.");

        var transaction = new UndoRedoTransaction(this, mode);
        _transactions.Add(transaction);
        OnAvailabilityChanged();
        return transaction;
    }

    /// <summary>Commits <paramref name="transaction"/>, which must be the innermost open transaction.</summary>
    internal async ValueTask CommitTransactionAsync(UndoRedoTransaction transaction, CancellationToken cancellationToken)
    {
        EnsureNoActionIsExecuting();
        EnsureIsInnermostTransaction(transaction, "commit");
        cancellationToken.ThrowIfCancellationRequested();

        if (transaction.HasActions)
        {
            if (_transactions.Count > 1)
            {
                // The parent shares this transaction's mode, so the group is either already applied
                // (immediate) or applied by the parent when it is itself committed (deferred).
                _transactions[^2].Add(transaction);
            }
            else
            {
                // Record before completing the transaction: if an action fails, the transaction stays open
                // so the caller can roll it back or retry once the cause is resolved.
                await RecordActionCoreAsync(transaction, execute: transaction.Mode is TransactionExecutionMode.Deferred, cancellationToken).ConfigureAwait(false);
            }
        }

        CompleteTransaction(transaction);
    }

    /// <summary>Rolls back <paramref name="transaction"/>, which must be the innermost open transaction.</summary>
    internal async ValueTask RollbackTransactionAsync(UndoRedoTransaction transaction, CancellationToken cancellationToken)
    {
        EnsureNoActionIsExecuting();
        EnsureIsInnermostTransaction(transaction, "roll back");

        ActionIsExecuting = true;
        try
        {
            // The transaction is only completed once the revert succeeded, so a failed rollback can be retried.
            await transaction.UnExecuteCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ActionIsExecuting = false;
        }

        CompleteTransaction(transaction);
    }

    private void CompleteTransaction(UndoRedoTransaction transaction)
    {
        _transactions.RemoveAt(_transactions.Count - 1);
        transaction.MarkCompleted();
        OnAvailabilityChanged();
    }

    private void EnsureNoActionIsExecuting()
    {
        // Reentrancy would clear ActionIsExecuting while the outer action is still running and would
        // record the inner action as an unrelated undo step.
        if (ActionIsExecuting)
            throw new InvalidOperationException("Cannot change the undo/redo history while an action is executing.");
    }

    private void EnsureNoOpenTransaction(string operation)
    {
        if (_transactions.Count > 0)
            throw new InvalidOperationException($"Cannot {operation} while a transaction is open ({_transactions.Count} open). Commit or roll back the open transactions first.");
    }

    private void EnsureIsInnermostTransaction(UndoRedoTransaction transaction, string operation)
    {
        if (_transactions.Count == 0 || _transactions[^1] != transaction)
            throw new InvalidOperationException($"Cannot {operation} this transaction because it is not the innermost open transaction. Complete the nested transactions first.");
    }

    private void EnsureCanBeRecorded(IUndoRedoAction action)
    {
        if (action is not UndoRedoTransaction transaction)
            return;

        if (!ReferenceEquals(transaction.Manager, this))
            throw new InvalidOperationException("Cannot record a transaction created by another UndoRedoManager.");

        if (!transaction.IsCompleted)
            throw new InvalidOperationException("Cannot record a transaction that is still open. Commit it instead.");
    }

    private async ValueTask RecordActionCoreAsync(IUndoRedoAction action, bool execute, CancellationToken cancellationToken)
    {
        if (execute)
        {
            await ExecuteAsync(action, cancellationToken).ConfigureAwait(false);
        }

        var (merged, mergeFailure) = await TryToMergeAsync(_history.PeekUndo(), action, cancellationToken).ConfigureAwait(false);
        if (merged)
        {
            _history.RecordMerged();
        }
        else
        {
            _history.Record(action);
        }

        OnHistoryChanged();

        // The action is applied either way, so the buffers are made consistent before a failing merge
        // implementation is reported; otherwise the change would stay applied with no undo entry.
        mergeFailure?.Throw();
    }

    /// <summary>Attempts to merge <paramref name="action"/> into <paramref name="previous"/>, capturing rather than propagating a failure.</summary>
    private async ValueTask<(bool Merged, ExceptionDispatchInfo? Failure)> TryToMergeAsync(IUndoRedoAction? previous, IUndoRedoAction action, CancellationToken cancellationToken)
    {
        if (!action.AllowToMergeWithPrevious || previous is null)
            return (false, null);

        // TryToMergeAsync is user code just like execute and unexecute, so it gets the same reentrancy guard.
        ActionIsExecuting = true;
        try
        {
            return (await previous.TryToMergeAsync(action, cancellationToken).ConfigureAwait(false), null);
        }
        catch (Exception exception)
        {
            return (false, ExceptionDispatchInfo.Capture(exception));
        }
        finally
        {
            ActionIsExecuting = false;
        }
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

    private void OnHistoryChanged()
    {
        OnPropertyChanged(nameof(UndoableActions));
        OnPropertyChanged(nameof(RedoableActions));
        OnAvailabilityChanged();

        try
        {
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            throw new UndoRedoNotificationException($"A {nameof(HistoryChanged)} handler threw an exception. The undo/redo operation itself completed successfully and must not be retried.", exception);
        }
    }

    private void OnAvailabilityChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(TransactionDepth));
    }

    private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
