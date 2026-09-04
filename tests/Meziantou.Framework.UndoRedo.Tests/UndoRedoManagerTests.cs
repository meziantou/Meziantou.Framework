namespace Meziantou.Framework.UndoRedo.Tests;

public sealed class UndoRedoManagerTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private static (UndoRedoDelegateAction Action, List<string> Log) CreateLoggingAction(string name)
    {
        var log = new List<string>();
        var action = new UndoRedoDelegateAction(
            execute: _ => { log.Add($"do:{name}"); return ValueTask.CompletedTask; },
            unexecute: _ => { log.Add($"undo:{name}"); return ValueTask.CompletedTask; });
        return (action, log);
    }

    [Fact]
    public async Task RecordAction_ExecutesActionAndEnablesUndo()
    {
        var manager = new UndoRedoManager();
        var (action, log) = CreateLoggingAction("a");

        await manager.RecordActionAsync(action, CancellationToken);

        Assert.Equal(["do:a"], log);
        Assert.True(manager.CanUndo);
        Assert.False(manager.CanRedo);
    }

    [Fact]
    public async Task Undo_RevertsAction_Redo_ReExecutes()
    {
        var manager = new UndoRedoManager();
        var (action, log) = CreateLoggingAction("a");
        await manager.RecordActionAsync(action, CancellationToken);

        await manager.UndoAsync(CancellationToken);
        Assert.Equal(["do:a", "undo:a"], log);
        Assert.False(manager.CanUndo);
        Assert.True(manager.CanRedo);

        await manager.RedoAsync(CancellationToken);
        Assert.Equal(["do:a", "undo:a", "do:a"], log);
        Assert.True(manager.CanUndo);
        Assert.False(manager.CanRedo);
    }

    [Fact]
    public async Task RecordAction_AfterUndo_ClearsRedoBuffer()
    {
        var manager = new UndoRedoManager();
        await manager.RecordActionAsync(CreateLoggingAction("a").Action, CancellationToken);
        await manager.UndoAsync(CancellationToken);
        Assert.True(manager.CanRedo);

        await manager.RecordActionAsync(CreateLoggingAction("b").Action, CancellationToken);

        Assert.False(manager.CanRedo);
        Assert.True(manager.CanUndo);
    }

    [Fact]
    public async Task Clear_EmptiesBothBuffers()
    {
        var manager = new UndoRedoManager();
        await manager.RecordActionAsync(CreateLoggingAction("a").Action, CancellationToken);
        await manager.UndoAsync(CancellationToken);

        manager.Clear();

        Assert.False(manager.CanUndo);
        Assert.False(manager.CanRedo);
    }

    [Fact]
    public async Task UndoRedoDelegateAction_RunsProvidedDelegates()
    {
        var manager = new UndoRedoManager();
        var value = 0;

        await manager.RecordActionAsync(
            execute: _ => { value = 42; return ValueTask.CompletedTask; },
            unexecute: _ => { value = 0; return ValueTask.CompletedTask; },
            CancellationToken);

        Assert.Equal(42, value);
        await manager.UndoAsync(CancellationToken);
        Assert.Equal(0, value);
    }

    [Fact]
    public async Task Merge_CollapsesConsecutiveActionsIntoSingleUndoStep()
    {
        var manager = new UndoRedoManager();
        var sum = 0;
        var first = new AddAction(v => sum += v, v => sum -= v, value: 1);
        var second = new AddAction(v => sum += v, v => sum -= v, value: 2) { AllowToMergeWithPrevious = true };

        // first executes (sum=1), then second executes (sum=3) and is merged into first.

        await manager.RecordActionAsync(first, CancellationToken);
        await manager.RecordActionAsync(second, CancellationToken);

        Assert.Equal(3, sum);
        Assert.Single(manager.UndoableActions);

        await manager.UndoAsync(CancellationToken);
        Assert.Equal(0, sum); // both reverted in a single undo
        Assert.False(manager.CanUndo);
    }

    [Fact]
    public async Task Merge_ClearsRedoBuffer()
    {
        var manager = new UndoRedoManager();
        var sum = 0;

        await manager.RecordActionAsync(new AddAction(v => sum += v, v => sum -= v, value: 1), CancellationToken);
        await manager.RecordActionAsync(new AddAction(v => sum += v, v => sum -= v, value: 2), CancellationToken);
        await manager.UndoAsync(CancellationToken);
        Assert.True(manager.CanRedo);

        // Recording moves the history forward, so the redo buffer must be dropped even when the action is merged.
        await manager.RecordActionAsync(new AddAction(v => sum += v, v => sum -= v, value: 4) { AllowToMergeWithPrevious = true }, CancellationToken);

        Assert.False(manager.CanRedo);
        Assert.Equal(5, sum);
        Assert.Single(manager.UndoableActions);

        await manager.UndoAsync(CancellationToken);
        Assert.Equal(0, sum);
    }

    [Fact]
    public async Task Merge_RefusedByPreviousAction_RecordsItsOwnStep()
    {
        var manager = new UndoRedoManager();
        var log = new List<string>();

        await manager.RecordActionAsync(() => log.Add("do:a"), () => log.Add("undo:a"), CancellationToken);

        // The previous action is a delegate action, whose TryToMergeAsync always refuses.
        var refused = new UndoRedoDelegateAction(() => log.Add("do:b"), () => log.Add("undo:b")) { AllowToMergeWithPrevious = true };
        await manager.RecordActionAsync(refused, CancellationToken);

        Assert.Equal(2, manager.UndoableActions.Count);

        await manager.UndoAsync(CancellationToken);
        await manager.UndoAsync(CancellationToken);
        Assert.Equal(["do:a", "do:b", "undo:b", "undo:a"], log);
    }

    [Fact]
    public async Task Merge_OnAnEmptyHistory_RecordsItsOwnStep()
    {
        var manager = new UndoRedoManager();
        var sum = 0;

        await manager.RecordActionAsync(new AddAction(v => sum += v, v => sum -= v, value: 1) { AllowToMergeWithPrevious = true }, CancellationToken);

        Assert.Equal(1, sum);
        Assert.Single(manager.UndoableActions);
    }

    [Fact]
    public async Task Merge_InsideATransaction_CollapsesTheActions()
    {
        var manager = new UndoRedoManager();
        var sum = 0;

        var transaction = manager.CreateTransaction(TransactionExecutionMode.Immediate);
        await manager.RecordActionAsync(new AddAction(v => sum += v, v => sum -= v, value: 1), CancellationToken);
        await manager.RecordActionAsync(new AddAction(v => sum += v, v => sum -= v, value: 2) { AllowToMergeWithPrevious = true }, CancellationToken);
        await transaction.CommitAsync(CancellationToken);

        Assert.Equal(3, sum);
        Assert.Single(manager.UndoableActions);

        await manager.UndoAsync(CancellationToken);
        Assert.Equal(0, sum);
    }

    [Fact]
    public async Task Merge_ThatThrows_StillRecordsTheAppliedActionAndClearsRedo()
    {
        var manager = new UndoRedoManager();
        var doc = new List<string>();

        await manager.RecordActionAsync(new ThrowingMergeAction(doc, "p"), CancellationToken);
        await manager.RecordActionAsync(() => doc.Add("y"), () => doc.Remove("y"), CancellationToken);
        await manager.UndoAsync(CancellationToken);
        Assert.True(manager.CanRedo);

        var following = new UndoRedoDelegateAction(() => doc.Add("n"), () => doc.Remove("n")) { AllowToMergeWithPrevious = true };
        await Assert.ThrowsAsync<InvalidCastException>(async () => await manager.RecordActionAsync(following, CancellationToken));

        // The action was applied, so the history must describe it and the superseded redo entry must be gone.
        Assert.Equal(["p", "n"], doc);
        Assert.Equal(2, manager.UndoableActions.Count);
        Assert.False(manager.CanRedo);

        await manager.UndoAsync(CancellationToken);
        Assert.Equal(["p"], doc);
    }

    [Fact]
    public async Task Merge_FromInsideTryToMergeAsync_CannotChangeTheHistory()
    {
        var manager = new UndoRedoManager();
        var doc = new List<string>();
        var reentrant = new ReentrantMergeAction(doc, "p", manager);

        await manager.RecordActionAsync(reentrant, CancellationToken);
        await manager.RecordActionAsync(
            new UndoRedoDelegateAction(() => doc.Add("n"), () => doc.Remove("n")) { AllowToMergeWithPrevious = true },
            CancellationToken);

        Assert.True(reentrant.ObservedActionIsExecuting);
        Assert.True(reentrant.UndoWasRejected);
        Assert.Equal(["p", "n"], doc);
        Assert.Equal(2, manager.UndoableActions.Count);
    }

    [Fact]
    public async Task Transaction_GroupsActionsIntoSingleUndoStep()
    {
        var manager = new UndoRedoManager();
        var log = new List<string>();

        await using (var transaction = manager.CreateTransaction())
        {
            await manager.RecordActionAsync(_ => { log.Add("do:a"); return ValueTask.CompletedTask; }, _ => { log.Add("undo:a"); return ValueTask.CompletedTask; }, CancellationToken);
            await manager.RecordActionAsync(_ => { log.Add("do:b"); return ValueTask.CompletedTask; }, _ => { log.Add("undo:b"); return ValueTask.CompletedTask; }, CancellationToken);

            // A deferred transaction runs nothing until it is committed.
            Assert.Empty(log);
            await transaction.CommitAsync(CancellationToken);
        }

        Assert.Equal(["do:a", "do:b"], log);
        Assert.Single(manager.UndoableActions);

        await manager.UndoAsync(CancellationToken);
        Assert.Equal(["do:a", "do:b", "undo:b", "undo:a"], log); // reverted in reverse order
        Assert.False(manager.CanUndo);

        await manager.RedoAsync(CancellationToken);
        Assert.Equal(["do:a", "do:b", "undo:b", "undo:a", "do:a", "do:b"], log);
    }

    [Fact]
    public async Task Transaction_Immediate_RunsActionsAsTheyAreRecorded()
    {
        var manager = new UndoRedoManager();
        var log = new List<string>();

        await using (var transaction = manager.CreateTransaction(TransactionExecutionMode.Immediate))
        {
            await manager.RecordActionAsync(() => log.Add("do:a"), () => log.Add("undo:a"), CancellationToken);
            Assert.Equal(["do:a"], log);

            await manager.RecordActionAsync(() => log.Add("do:b"), () => log.Add("undo:b"), CancellationToken);
            await transaction.CommitAsync(CancellationToken);
        }

        Assert.Equal(["do:a", "do:b"], log);
        Assert.Single(manager.UndoableActions);

        await manager.UndoAsync(CancellationToken);
        Assert.Equal(["do:a", "do:b", "undo:b", "undo:a"], log);
    }

    [Fact]
    public async Task Transaction_Rollback_RevertsExecutedActions()
    {
        var manager = new UndoRedoManager();
        var value = 0;

        var transaction = manager.CreateTransaction(TransactionExecutionMode.Immediate);
        await manager.RecordActionAsync(_ => { value += 1; return ValueTask.CompletedTask; }, _ => { value -= 1; return ValueTask.CompletedTask; }, CancellationToken);
        await manager.RecordActionAsync(_ => { value += 10; return ValueTask.CompletedTask; }, _ => { value -= 10; return ValueTask.CompletedTask; }, CancellationToken);
        Assert.Equal(11, value);

        await transaction.RollbackAsync(CancellationToken);

        Assert.Equal(0, value);
        Assert.False(manager.CanUndo);
    }

    [Theory]
    [InlineData(TransactionExecutionMode.Deferred)]
    [InlineData(TransactionExecutionMode.Immediate)]
    public async Task Transaction_Nested_Commit_UndoRevertsEveryAction(TransactionExecutionMode mode)
    {
        var manager = new UndoRedoManager();
        var log = new List<string>();

        var outer = manager.CreateTransaction(mode);
        var inner = manager.CreateTransaction(mode);
        await manager.RecordActionAsync(() => log.Add("do:a"), () => log.Add("undo:a"), CancellationToken);
        await inner.CommitAsync(CancellationToken);
        await outer.CommitAsync(CancellationToken);

        Assert.Equal(["do:a"], log);
        Assert.Single(manager.UndoableActions);

        // The outer transaction only holds an already-applied nested transaction; undo must still revert it.
        await manager.UndoAsync(CancellationToken);
        Assert.Equal(["do:a", "undo:a"], log);
        Assert.False(manager.CanUndo);
        Assert.True(manager.CanRedo);

        await manager.RedoAsync(CancellationToken);
        Assert.Equal(["do:a", "undo:a", "do:a"], log);
    }

    [Theory]
    [InlineData(TransactionExecutionMode.Deferred)]
    [InlineData(TransactionExecutionMode.Immediate)]
    public async Task Transaction_Nested_RollbackOuter_RevertsTheCommittedInnerTransaction(TransactionExecutionMode mode)
    {
        var manager = new UndoRedoManager();
        var log = new List<string>();

        var outer = manager.CreateTransaction(mode);
        var inner = manager.CreateTransaction(mode);
        await manager.RecordActionAsync(() => log.Add("do:a"), () => log.Add("undo:a"), CancellationToken);
        await inner.CommitAsync(CancellationToken);

        await outer.RollbackAsync(CancellationToken);

        // Whatever the inner transaction applied must be reverted; nothing may stay applied without a way back.
        var expected = mode is TransactionExecutionMode.Immediate ? new[] { "do:a", "undo:a" } : [];
        Assert.Equal(expected, log);
        Assert.False(manager.CanUndo);
        Assert.False(manager.CanRedo);
    }

    [Fact]
    public async Task Transaction_Nested_RecordsSingleUndoStep()
    {
        var manager = new UndoRedoManager();
        var log = new List<string>();

        await using (var outer = manager.CreateTransaction())
        {
            await manager.RecordActionAsync(_ => { log.Add("do:a"); return ValueTask.CompletedTask; }, _ => { log.Add("undo:a"); return ValueTask.CompletedTask; }, CancellationToken);
            await using (var inner = manager.CreateTransaction())
            {
                await manager.RecordActionAsync(_ => { log.Add("do:b"); return ValueTask.CompletedTask; }, _ => { log.Add("undo:b"); return ValueTask.CompletedTask; }, CancellationToken);
                await inner.CommitAsync(CancellationToken);
            }

            await outer.CommitAsync(CancellationToken);
        }

        Assert.Single(manager.UndoableActions);
        await manager.UndoAsync(CancellationToken);
        Assert.Equal(["do:a", "do:b", "undo:b", "undo:a"], log);
    }

    [Theory]
    [InlineData(TransactionExecutionMode.Deferred, TransactionExecutionMode.Immediate)]
    [InlineData(TransactionExecutionMode.Immediate, TransactionExecutionMode.Deferred)]
    public void Transaction_NestingADifferentExecutionMode_Throws(TransactionExecutionMode outerMode, TransactionExecutionMode innerMode)
    {
        var manager = new UndoRedoManager();
        _ = manager.CreateTransaction(outerMode);

        // Mixing the modes would execute the nested actions before the enclosing ones, so undo would
        // not be the inverse of execute.
        Assert.Throws<InvalidOperationException>(() => manager.CreateTransaction(innerMode));
    }

    [Fact]
    public async Task Transaction_Dispose_RollsBackWhenNotCommitted()
    {
        var manager = new UndoRedoManager();
        var log = new List<string>();

        await using (manager.CreateTransaction(TransactionExecutionMode.Immediate))
        {
            await manager.RecordActionAsync(() => log.Add("do:a"), () => log.Add("undo:a"), CancellationToken);
        }

        Assert.Equal(["do:a", "undo:a"], log);
        Assert.False(manager.CanUndo);
        Assert.Equal(0, manager.TransactionDepth);
    }

    [Fact]
    public async Task Transaction_ExceptionInTheScope_DiscardsTheWorkAndKeepsTheOriginalException()
    {
        var manager = new UndoRedoManager();
        var log = new List<string>();

        var exception = await Assert.ThrowsAsync<FormatException>(async () =>
        {
            await using (manager.CreateTransaction())
            {
                await manager.RecordActionAsync(() => log.Add("do:a"), () => log.Add("undo:a"), CancellationToken);
                throw new FormatException("the caller failed");
            }
        });

        Assert.Equal("the caller failed", exception.Message);
        Assert.Empty(log); // the abandoned work must never be applied on the way out
        Assert.False(manager.CanUndo);
    }

    [Fact]
    public async Task Transaction_ActionThatFailsToExecute_IsNotPartOfTheTransaction()
    {
        var manager = new UndoRedoManager();
        var log = new List<string>();

        var transaction = manager.CreateTransaction(TransactionExecutionMode.Immediate);
        await manager.RecordActionAsync(() => log.Add("do:a"), () => log.Add("undo:a"), CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await manager.RecordActionAsync(
            () => throw new InvalidOperationException("boom"),
            () => log.Add("undo:b"),
            CancellationToken));
        await transaction.CommitAsync(CancellationToken);

        Assert.Equal(["do:a"], log);

        await manager.UndoAsync(CancellationToken);
        await manager.RedoAsync(CancellationToken);

        // The failed action must not run for the first time during redo.
        Assert.Equal(["do:a", "undo:a", "do:a"], log);
    }

    [Fact]
    public async Task Transaction_CommitOuterWhileInnerIsOpen_Throws()
    {
        var manager = new UndoRedoManager();
        var outer = manager.CreateTransaction();
        var inner = manager.CreateTransaction();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await outer.CommitAsync(CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await outer.RollbackAsync(CancellationToken));

        await inner.CommitAsync(CancellationToken);
        await outer.CommitAsync(CancellationToken);
    }

    [Fact]
    public async Task Transaction_CompletedTwice_Throws()
    {
        var manager = new UndoRedoManager();

        var outer = manager.CreateTransaction();
        var inner = manager.CreateTransaction();
        await inner.CommitAsync(CancellationToken);

        // Completing an already completed transaction must not silently complete the enclosing one.
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await inner.CommitAsync(CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await inner.RollbackAsync(CancellationToken));

        // Disposing an already completed transaction stays a no-op.
        await inner.DisposeAsync();

        await outer.CommitAsync(CancellationToken);
    }

    [Fact]
    public async Task Transaction_CommitFailure_RevertsAlreadyExecutedActionsAndStaysOpen()
    {
        var manager = new UndoRedoManager();
        var log = new List<string>();

        var transaction = manager.CreateTransaction();
        await manager.RecordActionAsync(() => log.Add("do:a"), () => log.Add("undo:a"), CancellationToken);
        await manager.RecordActionAsync(() => throw new InvalidOperationException("boom"), () => log.Add("undo:b"), CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await transaction.CommitAsync(CancellationToken));

        Assert.Equal(["do:a", "undo:a"], log);
        Assert.False(manager.CanUndo);

        // The transaction is left open so the caller can still roll it back or retry.
        Assert.False(transaction.IsCompleted);
        Assert.Equal(1, manager.TransactionDepth);

        await transaction.RollbackAsync(CancellationToken);
        Assert.True(transaction.IsCompleted);
    }

    [Fact]
    public async Task Transaction_RollbackFailure_StaysOpenSoItCanBeRetried()
    {
        var manager = new UndoRedoManager();
        var log = new List<string>();
        var failOnUndo = true;

        var transaction = manager.CreateTransaction(TransactionExecutionMode.Immediate);
        await manager.RecordActionAsync(
            () => log.Add("do:a"),
            () => { if (failOnUndo) throw new InvalidOperationException("boom"); log.Add("undo:a"); },
            CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await transaction.RollbackAsync(CancellationToken));
        Assert.False(transaction.IsCompleted);
        Assert.Equal(1, manager.TransactionDepth);

        failOnUndo = false;
        await transaction.RollbackAsync(CancellationToken);

        Assert.Equal(["do:a", "undo:a"], log);
        Assert.True(transaction.IsCompleted);
        Assert.Equal(0, manager.TransactionDepth);
    }

    [Fact]
    public async Task Transaction_UndoFailure_ReappliesRevertedActions()
    {
        var manager = new UndoRedoManager();
        var log = new List<string>();
        var failOnUndo = true;

        await using (var transaction = manager.CreateTransaction())
        {
            await manager.RecordActionAsync(() => log.Add("do:a"), () => log.Add("undo:a"), CancellationToken);
            await manager.RecordActionAsync(
                () => log.Add("do:b"),
                () => { if (failOnUndo) throw new InvalidOperationException("boom"); log.Add("undo:b"); },
                CancellationToken);
            await manager.RecordActionAsync(() => log.Add("do:c"), () => log.Add("undo:c"), CancellationToken);
            await transaction.CommitAsync(CancellationToken);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await manager.UndoAsync(CancellationToken));

        // "b" failed to revert, so "c" is re-applied and the step stays fully executed and undoable.
        Assert.Equal(["do:a", "do:b", "do:c", "undo:c", "do:c"], log);
        Assert.True(manager.CanUndo);
        Assert.False(manager.CanRedo);

        failOnUndo = false;
        log.Clear();
        await manager.UndoAsync(CancellationToken);
        Assert.Equal(["undo:c", "undo:b", "undo:a"], log);
    }

    [Fact]
    public async Task Transaction_CompensationFailure_ThrowsAggregateExceptionPreservingBothCauses()
    {
        var manager = new UndoRedoManager();
        var log = new List<string>();

        var transaction = manager.CreateTransaction();
        await manager.RecordActionAsync(() => log.Add("do:a"), () => throw new NotSupportedException("cannot revert a"), CancellationToken);
        await manager.RecordActionAsync(() => throw new InvalidOperationException("boom"), () => log.Add("undo:b"), CancellationToken);

        var exception = await Assert.ThrowsAsync<AggregateException>(async () => await transaction.CommitAsync(CancellationToken));

        Assert.Collection(
            exception.InnerExceptions,
            first => Assert.IsType<InvalidOperationException>(first),
            second => Assert.IsType<NotSupportedException>(second));
        Assert.Equal(["do:a"], log); // "a" is knowingly left applied
    }

    [Fact]
    public async Task Transaction_FromAnotherManager_CannotBeRecorded()
    {
        var first = new UndoRedoManager();
        var second = new UndoRedoManager();

        var transaction = first.CreateTransaction();
        await first.RecordActionAsync(() => { }, () => { }, CancellationToken);
        await transaction.CommitAsync(CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await second.RecordActionAsync(transaction, CancellationToken));
    }

    [Fact]
    public async Task Transaction_ThatIsStillOpen_CannotBeRecorded()
    {
        var manager = new UndoRedoManager();
        var other = new UndoRedoManager();
        var transaction = manager.CreateTransaction();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await other.RecordActionAsync(transaction, CancellationToken));
    }

    [Fact]
    public async Task Execute_ForwardsCancellationToken()
    {
        var manager = new UndoRedoManager();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var action = new UndoRedoDelegateAction(
            execute: ct => { ct.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; },
            unexecute: _ => ValueTask.CompletedTask);

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await manager.RecordActionAsync(action, cts.Token));
    }

    [Fact]
    public async Task CancelledToken_RunsNothing()
    {
        var manager = new UndoRedoManager();
        var log = new List<string>();
        using var cts = new CancellationTokenSource();

        await manager.RecordActionAsync(() => log.Add("do:a"), () => log.Add("undo:a"), CancellationToken);
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await manager.RecordActionAsync(() => log.Add("do:b"), () => log.Add("undo:b"), cts.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await manager.UndoAsync(cts.Token));

        Assert.Equal(["do:a"], log);
        Assert.True(manager.CanUndo);
        Assert.Single(manager.UndoableActions);
    }

    [Fact]
    public async Task Merge_ForwardsCancellationToken()
    {
        var manager = new UndoRedoManager();
        var sum = 0;
        using var cts = new CancellationTokenSource();

        await manager.RecordActionAsync(new AddAction(v => sum += v, v => sum -= v, value: 1), cts.Token);

        await cts.CancelAsync();
        var second = new AddAction(v => sum += v, v => sum -= v, value: 2) { AllowToMergeWithPrevious = true };

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await manager.RecordActionAsync(second, cts.Token));

        // The cancelled recording must leave neither the model nor the history changed.
        Assert.Equal(1, sum);
        Assert.Single(manager.UndoableActions);
    }

    [Fact]
    public async Task HistoryChanged_RaisedOnRecordUndoRedoAndClear()
    {
        var manager = new UndoRedoManager();
        var count = 0;
        manager.HistoryChanged += (_, _) => count++;

        await manager.RecordActionAsync(CreateLoggingAction("a").Action, CancellationToken);
        await manager.UndoAsync(CancellationToken);
        await manager.RedoAsync(CancellationToken);
        manager.Clear();

        Assert.Equal(4, count);
    }

    [Fact]
    public async Task HistoryChanged_RaisedOnceForACommittedTransactionAndNotForARollback()
    {
        var manager = new UndoRedoManager();
        var count = 0;
        manager.HistoryChanged += (_, _) => count++;

        await using (var transaction = manager.CreateTransaction())
        {
            await manager.RecordActionAsync(() => { }, () => { }, CancellationToken);
            await manager.RecordActionAsync(() => { }, () => { }, CancellationToken);
            await transaction.CommitAsync(CancellationToken);
        }

        Assert.Equal(1, count);

        await using (manager.CreateTransaction(TransactionExecutionMode.Immediate))
        {
            await manager.RecordActionAsync(() => { }, () => { }, CancellationToken);
        }

        Assert.Equal(1, count); // a rollback does not change the history
    }

    [Fact]
    public async Task HistoryChanged_HandlerThatThrows_ReportsTheOperationAsCompleted()
    {
        var manager = new UndoRedoManager();
        var log = new List<string>();

        await manager.RecordActionAsync(() => log.Add("do:a"), () => log.Add("undo:a"), CancellationToken);
        manager.HistoryChanged += (_, _) => throw new InvalidOperationException("a binding blew up");

        var exception = await Assert.ThrowsAsync<UndoRedoNotificationException>(async () => await manager.UndoAsync(CancellationToken));

        // The undo did happen, so the caller must be able to tell this apart from a failed undo and not retry.
        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Equal(["do:a", "undo:a"], log);
        Assert.False(manager.CanUndo);
        Assert.True(manager.CanRedo);
    }

    [Fact]
    public async Task PropertyChanged_RaisedForTheBindableMembers()
    {
        var manager = new UndoRedoManager();
        var changed = new List<string>();
        manager.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        await manager.RecordActionAsync(CreateLoggingAction("a").Action, CancellationToken);

        Assert.Contains(nameof(UndoRedoManager.CanUndo), changed);
        Assert.Contains(nameof(UndoRedoManager.CanRedo), changed);
        Assert.Contains(nameof(UndoRedoManager.UndoableActions), changed);
        Assert.Contains(nameof(UndoRedoManager.RedoableActions), changed);

        changed.Clear();
        var transaction = manager.CreateTransaction();
        Assert.Contains(nameof(UndoRedoManager.CanUndo), changed);

        await transaction.RollbackAsync(CancellationToken);
    }

    [Fact]
    public async Task CanUndoAndCanRedo_AreFalseWhileATransactionIsOpen()
    {
        var manager = new UndoRedoManager();
        await manager.RecordActionAsync(CreateLoggingAction("a").Action, CancellationToken);
        await manager.UndoAsync(CancellationToken);
        await manager.RedoAsync(CancellationToken);
        Assert.True(manager.CanUndo);

        var transaction = manager.CreateTransaction();

        // They gate the operations, so they must not advertise something that is guaranteed to throw.
        Assert.False(manager.CanUndo);
        Assert.False(manager.CanRedo);
        Assert.Equal(1, manager.TransactionDepth);

        await transaction.RollbackAsync(CancellationToken);
        Assert.True(manager.CanUndo);
    }

    [Fact]
    public async Task UndoRedoAndClear_WhenTransactionOpen_Throw()
    {
        var manager = new UndoRedoManager();
        await manager.RecordActionAsync(CreateLoggingAction("a").Action, CancellationToken);
        var transaction = manager.CreateTransaction();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await manager.UndoAsync(CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await manager.RedoAsync(CancellationToken));
        Assert.Throws<InvalidOperationException>(manager.Clear);

        await transaction.RollbackAsync(CancellationToken);
    }

    [Fact]
    public async Task UndoRedoDelegateAction_SupportsSyncDelegates()
    {
        var manager = new UndoRedoManager();
        var value = 0;

        var action = new UndoRedoDelegateAction(execute: () => value = 1, unexecute: () => value = 0);
        await manager.RecordActionAsync(action, CancellationToken);

        Assert.Equal(1, value);
        await manager.UndoAsync(CancellationToken);
        Assert.Equal(0, value);
    }

    [Fact]
    public async Task UndoRedoDelegateAction_SupportsMixedSyncAndAsyncDelegates()
    {
        var manager = new UndoRedoManager();
        var value = 0;

        // async execute, sync unexecute
        var action = new UndoRedoDelegateAction(
            execute: async ct => { await Task.Yield(); value = 5; },
            unexecute: () => value = 0);
        await manager.RecordActionAsync(action, CancellationToken);

        Assert.Equal(5, value);
        await manager.UndoAsync(CancellationToken);
        Assert.Equal(0, value);

        // sync execute, async unexecute
        var other = new UndoRedoDelegateAction(
            execute: () => value = 7,
            unexecute: async ct => { await Task.Yield(); value = 0; });
        await manager.RecordActionAsync(other, CancellationToken);

        Assert.Equal(7, value);
        await manager.UndoAsync(CancellationToken);
        Assert.Equal(0, value);
    }

    [Fact]
    public async Task RecordActionAsync_SupportsSyncDelegateOverload()
    {
        var manager = new UndoRedoManager();
        var value = 0;

        await manager.RecordActionAsync(execute: () => value = 1, unexecute: () => value = 0, CancellationToken);

        Assert.Equal(1, value);
        await manager.UndoAsync(CancellationToken);
        Assert.Equal(0, value);
    }

    [Fact]
    public async Task RecordActionAsync_SupportsMixedDelegateOverloads()
    {
        var manager = new UndoRedoManager();
        var value = 0;

        // async execute, sync unexecute
        await manager.RecordActionAsync(execute: async ct => { await Task.Yield(); value = 5; }, unexecute: () => value = 0, CancellationToken);
        Assert.Equal(5, value);
        await manager.UndoAsync(CancellationToken);
        Assert.Equal(0, value);

        // sync execute, async unexecute
        await manager.RecordActionAsync(execute: () => value = 7, unexecute: async ct => { await Task.Yield(); value = 0; }, CancellationToken);
        Assert.Equal(7, value);
        await manager.UndoAsync(CancellationToken);
        Assert.Equal(0, value);
    }

    [Fact]
    public async Task RecordAction_FromInsideAnExecutingAction_Throws()
    {
        var manager = new UndoRedoManager();
        var log = new List<string>();

        var action = new UndoRedoDelegateAction(
            execute: async ct =>
            {
                Assert.True(manager.ActionIsExecuting);
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await manager.RecordActionAsync(() => log.Add("do:inner"), () => log.Add("undo:inner"), ct));
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await manager.UndoAsync(ct));
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await manager.RedoAsync(ct));
                Assert.Throws<InvalidOperationException>(manager.Clear);
                Assert.Throws<InvalidOperationException>(() => manager.CreateTransaction());
                log.Add("do:outer");
            },
            unexecute: () => log.Add("undo:outer"));

        await manager.RecordActionAsync(action, CancellationToken);

        Assert.Equal(["do:outer"], log);
        Assert.False(manager.ActionIsExecuting);
        Assert.Single(manager.UndoableActions);
    }

    [Fact]
    public async Task ActionIsExecuting_IsResetAfterAFailedAction()
    {
        var manager = new UndoRedoManager();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await manager.RecordActionAsync(
            () => throw new InvalidOperationException("boom"),
            () => { },
            CancellationToken));

        Assert.False(manager.ActionIsExecuting);
        Assert.False(manager.CanUndo);
    }

    [Fact]
    public async Task RecordAction_SameInstanceTwice_Throws()
    {
        var manager = new UndoRedoManager();
        var count = 0;
        var action = new UndoRedoDelegateAction(() => count++, () => count--);

        await manager.RecordActionAsync(action, CancellationToken);

        // Recording it again would add an undo step that reverts nothing.
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await manager.RecordActionAsync(action, CancellationToken));

        Assert.Equal(1, count);
        Assert.Single(manager.UndoableActions);
    }

    [Fact]
    public async Task UndoableActions_IsASnapshotOrderedFromMostRecent()
    {
        var manager = new UndoRedoManager();
        var (first, _) = CreateLoggingAction("a");
        var (second, _) = CreateLoggingAction("b");

        await manager.RecordActionAsync(first, CancellationToken);
        await manager.RecordActionAsync(second, CancellationToken);

        var snapshot = manager.UndoableActions;
        Assert.Equal([second, first], snapshot);

        // Reading again without a change reuses the same snapshot instead of copying the buffer.
        Assert.Same(snapshot, manager.UndoableActions);

        // Recording more actions must not change a snapshot taken earlier.
        await manager.RecordActionAsync(CreateLoggingAction("c").Action, CancellationToken);
        Assert.Equal([second, first], snapshot);
        Assert.Equal(3, manager.UndoableActions.Count);

        await manager.UndoAsync(CancellationToken);
        Assert.Equal(2, manager.UndoableActions.Count);
        Assert.Single(manager.RedoableActions);
    }

    [Fact]
    public async Task MaxHistoryDepth_DropsTheOldestSteps()
    {
        var manager = new UndoRedoManager(maxHistoryDepth: 2);
        var log = new List<string>();

        await manager.RecordActionAsync(() => log.Add("do:a"), () => log.Add("undo:a"), CancellationToken);
        await manager.RecordActionAsync(() => log.Add("do:b"), () => log.Add("undo:b"), CancellationToken);
        await manager.RecordActionAsync(() => log.Add("do:c"), () => log.Add("undo:c"), CancellationToken);

        Assert.Equal(2, manager.MaxHistoryDepth);
        Assert.Equal(2, manager.UndoableActions.Count);

        log.Clear();
        await manager.UndoAsync(CancellationToken);
        await manager.UndoAsync(CancellationToken);
        Assert.Equal(["undo:c", "undo:b"], log); // "a" was dropped and can no longer be undone
        Assert.False(manager.CanUndo);
    }

    [Fact]
    public void MaxHistoryDepth_MustBeStrictlyPositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UndoRedoManager(maxHistoryDepth: 0));
    }

    [Fact]
    public async Task Description_IsExposedOnTheRecordedActions()
    {
        var manager = new UndoRedoManager();
        var action = new UndoRedoDelegateAction(() => { }, () => { }) { Description = "Insert paragraph" };

        await manager.RecordActionAsync(action, CancellationToken);

        Assert.Equal("Insert paragraph", manager.UndoableActions[0].Description);
    }

    private sealed class AddAction(Action<int> add, Action<int> subtract, int value) : UndoRedoActionBase
    {
        // The total amount this action is responsible for; grows when following actions are merged in.
        private int _value = value;

        protected override ValueTask ExecuteCoreAsync(CancellationToken cancellationToken)
        {
            add(_value);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask UnExecuteCoreAsync(CancellationToken cancellationToken)
        {
            subtract(_value);
            return ValueTask.CompletedTask;
        }

        public override ValueTask<bool> TryToMergeAsync(IUndoRedoAction followingAction, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The following action is in the same applied state as this one; absorb its value so a single
            // UnExecute reverts both actions.
            if (followingAction is AddAction other)
            {
                _value += other._value;
                return new(true);
            }

            return new(false);
        }
    }

    private sealed class ThrowingMergeAction(List<string> document, string tag) : UndoRedoActionBase
    {
        protected override ValueTask ExecuteCoreAsync(CancellationToken cancellationToken)
        {
            document.Add(tag);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask UnExecuteCoreAsync(CancellationToken cancellationToken)
        {
            document.Remove(tag);
            return ValueTask.CompletedTask;
        }

        public override ValueTask<bool> TryToMergeAsync(IUndoRedoAction followingAction, CancellationToken cancellationToken = default)
            => throw new InvalidCastException("the merge implementation did not expect this action type");
    }

    private sealed class ReentrantMergeAction(List<string> document, string tag, UndoRedoManager manager) : UndoRedoActionBase
    {
        public bool ObservedActionIsExecuting { get; private set; }

        public bool UndoWasRejected { get; private set; }

        protected override ValueTask ExecuteCoreAsync(CancellationToken cancellationToken)
        {
            document.Add(tag);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask UnExecuteCoreAsync(CancellationToken cancellationToken)
        {
            document.Remove(tag);
            return ValueTask.CompletedTask;
        }

        public override async ValueTask<bool> TryToMergeAsync(IUndoRedoAction followingAction, CancellationToken cancellationToken = default)
        {
            ObservedActionIsExecuting = manager.ActionIsExecuting;
            try
            {
                await manager.UndoAsync(cancellationToken);
            }
            catch (InvalidOperationException)
            {
                UndoWasRejected = true;
            }

            return false;
        }
    }
}
