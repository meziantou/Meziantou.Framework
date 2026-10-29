---
title: Undo history in the UI
description: Bind the Undo and Redo commands, show the undo history, limit its size and handle notification errors.
---

## Enable the Undo and Redo commands

`CanUndo` and `CanRedo` tell whether `UndoAsync` and `RedoAsync` would do something right now. They are `false` when the
history is empty, while a transaction is open and while an action runs, so you can bind them to the availability of
the Undo and Redo commands directly.

`UndoRedoManager` implements `INotifyPropertyChanged`. It raises `PropertyChanged` for `CanUndo`, `CanRedo`,
`UndoableActions`, `RedoableActions` and `TransactionDepth`, and raises `HistoryChanged` whenever the histories change:
an action is recorded, undone or redone, a transaction is committed, or the history is cleared.

<!-- ref:../../tests/Meziantou.Framework.UndoRedo.Tests/UndoRedoManagerTests.cs;region=UndoRedo_Notifications;format=md-fenced-code-block;dedent=true -->
```csharp
var manager = new UndoRedoManager();
var changedProperties = new List<string?>();
var historyChanges = 0;
manager.PropertyChanged += (sender, e) => changedProperties.Add(e.PropertyName);
manager.HistoryChanged += (sender, e) => historyChanges++;

await manager.RecordActionAsync(() => { }, () => { });

// Refresh the Undo and Redo commands of the UI from these notifications
Assert.Contains(nameof(UndoRedoManager.CanUndo), changedProperties);
Assert.Contains(nameof(UndoRedoManager.UndoableActions), changedProperties);
Assert.Equal(1, historyChanges);
```
<!-- endref -->

A transaction that is rolled back does not raise `HistoryChanged`: the histories did not change.

## Show the history

`UndoableActions` and `RedoableActions` list the actions of each history, the most recent first. Show their
`Description` in an undo list or in the tooltip of the Undo button:

<!-- ref:../../tests/Meziantou.Framework.UndoRedo.Tests/UndoRedoManagerTests.cs;region=UndoRedo_History;format=md-fenced-code-block;dedent=true -->
```csharp
var manager = new UndoRedoManager();
var shapes = new List<string>();

await manager.RecordActionAsync(new UndoRedoDelegateAction(() => shapes.Add("circle"), () => shapes.Remove("circle")) { Description = "Add circle" });
await manager.RecordActionAsync(new UndoRedoDelegateAction(() => shapes.Add("square"), () => shapes.Remove("square")) { Description = "Add square" });
await manager.UndoAsync();

// The most recent action comes first
Assert.Equal(["Add circle"], manager.UndoableActions.Select(action => action.Description));
Assert.Equal(["Add square"], manager.RedoableActions.Select(action => action.Description));
```
<!-- endref -->

Both properties return a snapshot: a list obtained earlier does not change when the history does. Reading the property
again without a change returns the same instance, so binding to it does not copy the history on every read.

## Limit the history

The history is unbounded by default. Pass a maximum number of undo steps to drop the oldest ones instead:

<!-- ref:../../tests/Meziantou.Framework.UndoRedo.Tests/UndoRedoManagerTests.cs;region=UndoRedoManager_MaxHistoryDepth;format=md-fenced-code-block;dedent=true -->
```csharp
var manager = new UndoRedoManager(maxHistoryDepth: 100);
```
<!-- endref -->

- A step that is dropped can no longer be undone.
- A transaction counts as one step, as does a series of merged actions.
- The value must be greater than 0: `ArgumentOutOfRangeException` otherwise. `MaxHistoryDepth` returns it.

## Errors in event handlers

When a `HistoryChanged` handler throws, the operation that raised the event has already completed: the action was
recorded, undone or redone, or the history was cleared. The exception reaches the caller as an
`UndoRedoNotificationException`, whose `InnerException` is the exception of the handler. Do not retry the operation.

## Threads

`UndoRedoManager` is not thread-safe. Call it from one logical flow, such as the UI thread, and await each operation
before starting the next one.
