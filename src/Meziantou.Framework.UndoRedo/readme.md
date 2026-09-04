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

`CanUndo` and `CanRedo` indicate whether the corresponding operation would succeed right now: they are `false` while a transaction is open or an action is running, so they can be bound directly to a command's availability. `UndoRedoManager` implements `INotifyPropertyChanged` and raises it for `CanUndo`, `CanRedo`, `UndoableActions`, `RedoableActions` and `TransactionDepth`; the `HistoryChanged` event is raised whenever the buffers change.

The `execute` and `unexecute` delegates can each be synchronous or asynchronous, and the two may be mixed. Build the action with `UndoRedoDelegateAction` and record it:

````c#
// Asynchronous execute, synchronous unexecute
var action = new UndoRedoDelegateAction(
    execute: ct => SaveAsync(ct),
    unexecute: () => Restore());

await manager.RecordActionAsync(action);
````

Set `Description` on an action to label it in an undo history UI; it is exposed through `UndoableActions` and `RedoableActions`.

## Limit how much history is kept

By default the history is unbounded. Pass a maximum depth to drop the oldest steps instead of growing for the lifetime of the process:

````c#
var manager = new UndoRedoManager(maxHistoryDepth: 100);
````

## Custom action

For reusable actions, derive from `UndoRedoActionBase`. The base class tracks whether the action is currently applied, and throws if it would be executed or reverted twice in a row.

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

An action instance can only be recorded once at a time: recording the same instance twice would add an undo step that reverts nothing, so it throws instead.

## Group actions in a transaction

A transaction groups several actions into a single undo step. **It must be committed to be kept** — disposing it without committing rolls it back, so leaving the scope because of an exception discards the work rather than applying it.

````c#
await using (var transaction = manager.CreateTransaction())
{
    await manager.RecordActionAsync(addFirst, removeFirst);
    await manager.RecordActionAsync(addSecond, removeSecond);
    await transaction.CommitAsync();
}

// A single UndoAsync reverts both actions
await manager.UndoAsync();
````

### Execution mode

`CreateTransaction` takes a `TransactionExecutionMode`:

| Mode | When the actions run | Failure semantics |
| --- | --- | --- |
| `Deferred` (default) | When the transaction is committed. Reading the model between two recordings shows the state from before the transaction. | All-or-nothing: if one action fails during the commit, the actions that already ran are reverted before the exception is propagated. |
| `Immediate` | As they are recorded, so the model stays up to date while the transaction is open. | A failing action does not join the transaction, but the actions recorded before it stay applied. Call `RollbackAsync` to revert them. |

Nested transactions must use the same mode as the transaction they are nested in; mixing them would execute the nested actions before the enclosing ones, so undo would not be the inverse of execute. `CreateTransaction` throws an `InvalidOperationException` instead.

Nested transactions must also be completed from the innermost out; committing or rolling back a transaction while one of its nested transactions is still open throws an `InvalidOperationException`.

A commit or a rollback that fails leaves the transaction open, so it can be rolled back or retried once the cause is resolved. If the compensating revert fails too, an `AggregateException` carrying both the original failure and the compensation failure is thrown, and the actions that could not be reverted stay applied.

## Merge consecutive actions

When an action sets `AllowToMergeWithPrevious` and the previous action's `TryToMergeAsync` returns `true`, both collapse into a single undo step. This is useful for chains of similar operations such as typing or dragging, and works both at the top level and inside a transaction.

`followingAction` is in the same applied state as the action absorbing it — both are already applied at the top level and in an `Immediate` transaction, neither is in a `Deferred` one — so an implementation must absorb its effect without applying it. Returning `true` discards `followingAction`: the merging action becomes responsible for applying and reverting both effects.

> Note: `UndoRedoManager` is not thread-safe; operate on it from a single logical flow. An action must not record, undo, or redo anything while it is executing — including from `TryToMergeAsync`: those operations throw an `InvalidOperationException` as long as `ActionIsExecuting` is `true`.

## Migrating from 3.x

| 3.x | 4.0 |
| --- | --- |
| `CreateTransaction(bool isDelayed = true)` | `CreateTransaction(TransactionExecutionMode mode = Deferred)`. `isDelayed: true` is `Deferred`, `isDelayed: false` is `Immediate`. |
| Disposing a transaction committed it | Disposing rolls it back unless `CommitAsync` was called. Add an explicit `await transaction.CommitAsync()` before leaving the scope. |
| Nesting transactions with different `isDelayed` values | Throws `InvalidOperationException`. Nested transactions must use the enclosing mode. |
| `UndoRedoManager.CommitTransactionAsync()` / `RollbackTransactionAsync()` | Removed; they acted on the innermost transaction regardless of which handle the caller held. Use `transaction.CommitAsync()` / `RollbackAsync()`. |
| `CollectionChanged` | `HistoryChanged`. `UndoRedoManager` now also implements `INotifyPropertyChanged`. |
| `CanUndo` / `CanRedo` reported the buffer state | They report whether the operation would succeed, so they are `false` while a transaction is open or an action is executing. |
| `UndoRedoActionBase.ExecuteCount` | `IsApplied`. Executing an already applied action, or reverting one that is not applied, now throws instead of being a silent no-op. |
| A cancelled `CancellationToken` was only forwarded to the action | Every operation throws `OperationCanceledException` before running anything. |
| A `CollectionChanged` handler that threw surfaced as if the operation had failed | Handler failures surface as `UndoRedoNotificationException` after the operation completed successfully. |
| Unbounded history | Still the default; pass `new UndoRedoManager(maxHistoryDepth)` to bound it. |
