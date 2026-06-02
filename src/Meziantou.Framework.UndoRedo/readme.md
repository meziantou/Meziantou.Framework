# Meziantou.Framework.UndoRedo

`Meziantou.Framework.UndoRedo` provides an async-first undo/redo framework based on the command pattern. Every action exposes asynchronous `ExecuteAsync` / `UnExecuteAsync` methods (returning `ValueTask`), supports cancellation, and can be grouped into transactions or merged into a single undo step.

## Record, undo and redo an action

The simplest way to record an action is to provide the *execute* and *unexecute* delegates:

````c#
var manager = new UndoRedoManager();
var list = new List<int>();

await manager.RecordActionAsync(
    execute: () => list.Add(1),
    unexecute: () => list.RemoveAt(list.Count - 1));

// list = [1]
await manager.UndoAsync(); // list = []
await manager.RedoAsync(); // list = [1]
````

`CanUndo` and `CanRedo` indicate whether an operation is available, and `CollectionChanged` is raised whenever the history changes.

The `execute` and `unexecute` delegates can each be synchronous or asynchronous, and the two may be mixed. Build the action with `UndoRedoDelegateAction` and record it:

````c#
// Asynchronous execute, synchronous unexecute
var action = new UndoRedoDelegateAction(
    execute: ct => SaveAsync(ct),
    unexecute: () => Restore());

await manager.RecordActionAsync(action);
````

## Custom action

For reusable actions, derive from `UndoRedoActionBase`. The base class guards against executing or reverting twice in a row.

````c#
sealed class AddItemAction(IList<int> list, int value) : UndoRedoActionBase
{
    protected override ValueTask ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        list.Add(value);
        return ValueTask.CompletedTask;
    }

    protected override ValueTask UnExecuteCoreAsync(CancellationToken cancellationToken)
    {
        list.Remove(value);
        return ValueTask.CompletedTask;
    }
}

await manager.RecordActionAsync(new AddItemAction(list, 42));
````

## Group actions in a transaction

A transaction groups several actions into a single undo step. Disposing the transaction commits it (use `await using`), or call `RollbackAsync` to revert it.

````c#
await using (manager.CreateTransaction())
{
    await manager.RecordActionAsync(addFirst, removeFirst);
    await manager.RecordActionAsync(addSecond, removeSecond);
}

// A single UndoAsync reverts both actions
await manager.UndoAsync();
````

## Merge consecutive actions

When an action sets `AllowToMergeWithPrevious` and the previous action's `TryToMergeAsync` returns `true`, both collapse into a single undo step. This is useful for chains of similar operations such as typing or dragging.

> Note: `UndoRedoManager` is not thread-safe; operate on it from a single logical flow.
