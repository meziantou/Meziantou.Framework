---
title: Transactions
description: Group several actions into one undo step, commit or roll them back, and choose when they run.
---

A transaction groups the actions recorded while it is open into a single undo step. Use one when a single user command
makes several changes, such as moving several shapes or replacing all the occurrences of a word.

## Group actions

<!-- ref:../../tests/Meziantou.Framework.UndoRedo.Tests/UndoRedoManagerTests.cs;region=UndoRedoManager_Transaction;format=md-fenced-code-block;dedent=true -->
```csharp
var manager = new UndoRedoManager();
var list = new List<int>();

await using (var transaction = manager.CreateTransaction())
{
    await manager.RecordActionAsync(() => list.Add(1), () => list.Remove(1));
    await manager.RecordActionAsync(() => list.Add(2), () => list.Remove(2));
    await transaction.CommitAsync();
}

Assert.Equal([1, 2], list);

// A single UndoAsync reverts both actions
await manager.UndoAsync();
Assert.Empty(list);
```
<!-- endref -->

- `CreateTransaction` opens a transaction. The actions recorded with `RecordActionAsync` until it is completed belong to
  it.
- `CommitAsync` adds the transaction to the undo history as one step. A transaction without actions adds nothing.
- `UndoAsync` reverts the actions of the transaction, most recent first, and `RedoAsync` applies them again, in order.
- Set `Description` on the transaction to label the step in the undo history.

**A transaction must be committed to be kept.** Disposing it without committing it rolls it back, so an exception that
leaves the `using` block discards the changes instead of recording half of them:

<!-- ref:../../tests/Meziantou.Framework.UndoRedo.Tests/UndoRedoManagerTests.cs;region=UndoRedoTransaction_NotCommitted;format=md-fenced-code-block;dedent=true -->
```csharp
var manager = new UndoRedoManager();
var list = new List<int>();

static void Validate(List<int> values)
{
    if (values.Sum() > 10)
        throw new InvalidOperationException("The total cannot exceed 10");
}

try
{
    await using var transaction = manager.CreateTransaction(TransactionExecutionMode.Immediate);
    await manager.RecordActionAsync(() => list.Add(4), () => list.Remove(4));
    await manager.RecordActionAsync(() => list.Add(8), () => list.Remove(8));
    Validate(list); // throws, so CommitAsync is never called
    await transaction.CommitAsync();
}
catch (InvalidOperationException)
{
}

// Disposing the transaction without committing it rolled it back
Assert.Empty(list);
Assert.False(manager.CanUndo);
```
<!-- endref -->

While a transaction is open, `CanUndo` and `CanRedo` are `false`, and `UndoAsync`, `RedoAsync` and `Clear` throw an
`InvalidOperationException`. `TransactionDepth` gives the number of open transactions.

## Choose when the actions run

`CreateTransaction` takes a `TransactionExecutionMode`:

| Mode | When the actions run | When an action fails |
| --- | --- | --- |
| `Deferred` (default) | When the transaction is committed | The actions that already ran are reverted, and the exception is thrown by `CommitAsync` |
| `Immediate` | As they are recorded | The failing action is not part of the transaction; the ones recorded before it stay applied until you roll back |

In `Deferred` mode, the document does not change until the commit, so code that reads it between two recordings sees the
state from before the transaction:

<!-- ref:../../tests/Meziantou.Framework.UndoRedo.Tests/UndoRedoManagerTests.cs;region=UndoRedoTransaction_Deferred;format=md-fenced-code-block;dedent=true -->
```csharp
var manager = new UndoRedoManager();
var list = new List<int>();

await using (var transaction = manager.CreateTransaction())
{
    await manager.RecordActionAsync(() => list.Add(1), () => list.Remove(1));

    // In the default Deferred mode, the actions run when the transaction is committed
    Assert.Empty(list);

    await transaction.CommitAsync();
}

Assert.Equal([1], list);
```
<!-- endref -->

Use `Immediate` when the code that records an action needs the result of the previous one. `RollbackAsync` reverts what
already ran:

<!-- ref:../../tests/Meziantou.Framework.UndoRedo.Tests/UndoRedoManagerTests.cs;region=UndoRedoTransaction_Immediate;format=md-fenced-code-block;dedent=true -->
```csharp
var manager = new UndoRedoManager();
var list = new List<int>();

await using (var transaction = manager.CreateTransaction(TransactionExecutionMode.Immediate))
{
    await manager.RecordActionAsync(() => list.Add(1), () => list.Remove(1));
    await manager.RecordActionAsync(() => list.Add(2), () => list.Remove(2));

    // In Immediate mode, the actions run as they are recorded
    Assert.Equal([1, 2], list);

    // Rolling back reverts them, and nothing is added to the history
    await transaction.RollbackAsync();
}

Assert.Empty(list);
Assert.False(manager.CanUndo);
```
<!-- endref -->

## Nested transactions

A transaction can be opened while another one is open. When the inner transaction is committed, it becomes one action of
the outer transaction, and the outer transaction is recorded as a single undo step when it is committed in turn.

- A nested transaction must use the mode of the transaction it is nested in: `CreateTransaction` throws an
  `InvalidOperationException` otherwise.
- Complete the transactions from the innermost out: committing or rolling back a transaction while a transaction nested
  in it is open throws an `InvalidOperationException`.

## Failures

- A commit or a rollback that fails leaves the transaction open, so you can roll it back, or retry once the cause is
  fixed.
- Reverting the actions that already ran can fail too. Then an `AggregateException` holds both the original failure and
  the failure of the revert, and the actions that could not be reverted stay applied.
- `IsCompleted` tells whether the transaction was committed or rolled back. Completing it a second time throws an
  `InvalidOperationException`.
