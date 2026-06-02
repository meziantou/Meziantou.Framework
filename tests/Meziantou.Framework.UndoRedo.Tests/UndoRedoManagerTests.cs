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
    public async Task Transaction_GroupsActionsIntoSingleUndoStep()
    {
        var manager = new UndoRedoManager();
        var log = new List<string>();

        await using (manager.CreateTransaction())
        {
            await manager.RecordActionAsync(_ => { log.Add("do:a"); return ValueTask.CompletedTask; }, _ => { log.Add("undo:a"); return ValueTask.CompletedTask; }, CancellationToken);
            await manager.RecordActionAsync(_ => { log.Add("do:b"); return ValueTask.CompletedTask; }, _ => { log.Add("undo:b"); return ValueTask.CompletedTask; }, CancellationToken);
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
    public async Task Transaction_Rollback_RevertsExecutedActions()
    {
        var manager = new UndoRedoManager();
        var value = 0;

        var transaction = manager.CreateTransaction(isDelayed: false);
        await manager.RecordActionAsync(_ => { value += 1; return ValueTask.CompletedTask; }, _ => { value -= 1; return ValueTask.CompletedTask; }, CancellationToken);
        await manager.RecordActionAsync(_ => { value += 10; return ValueTask.CompletedTask; }, _ => { value -= 10; return ValueTask.CompletedTask; }, CancellationToken);
        Assert.Equal(11, value);

        await transaction.RollbackAsync(CancellationToken);

        Assert.Equal(0, value);
        Assert.False(manager.CanUndo);
    }

    [Fact]
    public async Task Transaction_Nested_RecordsSingleUndoStep()
    {
        var manager = new UndoRedoManager();
        var log = new List<string>();

        await using (manager.CreateTransaction())
        {
            await manager.RecordActionAsync(_ => { log.Add("do:a"); return ValueTask.CompletedTask; }, _ => { log.Add("undo:a"); return ValueTask.CompletedTask; }, CancellationToken);
            await using (manager.CreateTransaction())
            {
                await manager.RecordActionAsync(_ => { log.Add("do:b"); return ValueTask.CompletedTask; }, _ => { log.Add("undo:b"); return ValueTask.CompletedTask; }, CancellationToken);
            }
        }

        Assert.Single(manager.UndoableActions);
        await manager.UndoAsync(CancellationToken);
        Assert.Equal(["do:a", "do:b", "undo:b", "undo:a"], log);
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
    public async Task CollectionChanged_RaisedOnRecordUndoRedoAndClear()
    {
        var manager = new UndoRedoManager();
        var count = 0;
        manager.CollectionChanged += (_, _) => count++;

        await manager.RecordActionAsync(CreateLoggingAction("a").Action, CancellationToken);
        await manager.UndoAsync(CancellationToken);
        await manager.RedoAsync(CancellationToken);
        manager.Clear();

        Assert.Equal(4, count);
    }

    [Fact]
    public async Task Undo_WhenTransactionOpen_Throws()
    {
        var manager = new UndoRedoManager();
        var transaction = manager.CreateTransaction();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await manager.UndoAsync(CancellationToken));

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

        public override ValueTask<bool> TryToMergeAsync(IUndoRedoAction followingAction)
        {
            // The following action already executed its own effect; absorb its value so a single
            // UnExecute reverts both actions.
            if (followingAction is AddAction other)
            {
                _value += other._value;
                return new(true);
            }

            return new(false);
        }
    }
}
