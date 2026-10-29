---
title: Migrating from version 3
description: The changes of version 4 and how to update code written for version 3.
---

Version 4 makes transactions safe by default, reports availability the way a user interface needs it, and checks the
state of actions. Update your code as follows:

| Version 3 | Version 4 |
| --- | --- |
| `CreateTransaction(bool isDelayed = true)` | `CreateTransaction(TransactionExecutionMode mode = TransactionExecutionMode.Deferred)`. `isDelayed: true` is `Deferred`, `isDelayed: false` is `Immediate`. |
| Disposing a transaction committed it | Disposing rolls it back unless `CommitAsync` was called. Call `await transaction.CommitAsync()` before leaving the scope. |
| Nesting transactions with different `isDelayed` values | Throws an `InvalidOperationException`: a nested transaction uses the mode of the enclosing one. |
| `UndoRedoManager.CommitTransactionAsync()` and `RollbackTransactionAsync()` | Removed: they completed the innermost transaction, whichever handle the caller held. Call `transaction.CommitAsync()` or `transaction.RollbackAsync()`. |
| `CollectionChanged` | `HistoryChanged`. `UndoRedoManager` also implements `INotifyPropertyChanged`. |
| `CanUndo` and `CanRedo` reported the content of the histories | They report whether the operation would succeed, so they are `false` while a transaction is open or an action runs. |
| `UndoRedoActionBase.ExecuteCount` | `IsApplied`. Executing an applied action, or reverting an action that is not applied, throws instead of doing nothing. |
| A canceled `CancellationToken` was only passed to the action | Every operation throws an `OperationCanceledException` before running anything. |
| A `CollectionChanged` handler that threw looked like a failure of the operation | The exception is wrapped in an `UndoRedoNotificationException`, thrown after the operation completed. |
| Unbounded history | Still the default. Pass `maxHistoryDepth` to the constructor to bound it. |

See [Transactions](04-Transactions.md) and [Undo history in the UI](06-UndoHistoryInTheUI.md) for the details.
