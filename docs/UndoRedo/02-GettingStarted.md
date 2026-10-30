---
title: Getting started
description: Install the package, record actions, then undo and redo them.
---

## Install the package

Add the `Meziantou.Framework.UndoRedo` package to a project:

```bash
dotnet add package Meziantou.Framework.UndoRedo
```

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework.UndoRedo;`.

## Record, undo and redo

Create one [UndoRedoManager](cref:Meziantou.Framework.UndoRedo.UndoRedoManager) for the document the user edits, and record each change with the code that applies it and
the code that reverts it:

<!-- ref:../../tests/Meziantou.Framework.UndoRedo.Tests/UndoRedoManagerTests.cs;region=UndoRedo_GettingStarted;format=md-fenced-code-block;dedent=true -->
```csharp
var manager = new UndoRedoManager();
var document = new List<string>();

// Each recorded action runs immediately and becomes one undo step
await manager.RecordActionAsync(
    execute: () => document.Add("Hello"),
    unexecute: () => document.RemoveAt(document.Count - 1));
await manager.RecordActionAsync(
    execute: () => document.Add("World"),
    unexecute: () => document.RemoveAt(document.Count - 1));
Assert.Equal(["Hello", "World"], document);

await manager.UndoAsync();
Assert.Equal(["Hello"], document);
Assert.True(manager.CanUndo);
Assert.True(manager.CanRedo);

// Recording a new action clears the redo history
await manager.RecordActionAsync(
    execute: () => document.Add("Everyone"),
    unexecute: () => document.RemoveAt(document.Count - 1));
Assert.Equal(["Hello", "Everyone"], document);
Assert.False(manager.CanRedo);
```
<!-- endref -->

- [RecordActionAsync](cref:Meziantou.Framework.UndoRedo.UndoRedoManager.RecordActionAsync) applies the change by calling `execute`, then adds it to the undo history. When `execute` throws,
  nothing is recorded.
- [UndoAsync](cref:Meziantou.Framework.UndoRedo.UndoRedoManager.UndoAsync) reverts the most recent change by calling `unexecute`, and moves it to the redo history. [RedoAsync](cref:Meziantou.Framework.UndoRedo.UndoRedoManager.RedoAsync)
  applies it again. Both do nothing when the history is empty.
- [CanUndo](cref:Meziantou.Framework.UndoRedo.UndoRedoManager.CanUndo) and [CanRedo](cref:Meziantou.Framework.UndoRedo.UndoRedoManager.CanRedo) tell whether [UndoAsync](cref:Meziantou.Framework.UndoRedo.UndoRedoManager.UndoAsync) and [RedoAsync](cref:Meziantou.Framework.UndoRedo.UndoRedoManager.RedoAsync) would do something.
- Recording a change after an undo clears the redo history, as in most editors.
- [Clear](cref:Meziantou.Framework.UndoRedo.UndoRedoManager.Clear) empties both histories, for instance after the document is saved or closed.

## Asynchronous changes

`execute` and `unexecute` can be asynchronous: they receive the `CancellationToken` passed to [RecordActionAsync](cref:Meziantou.Framework.UndoRedo.UndoRedoManager.RecordActionAsync),
[UndoAsync](cref:Meziantou.Framework.UndoRedo.UndoRedoManager.UndoAsync) or [RedoAsync](cref:Meziantou.Framework.UndoRedo.UndoRedoManager.RedoAsync). A synchronous and an asynchronous delegate can be mixed:

<!-- ref:../../tests/Meziantou.Framework.UndoRedo.Tests/UndoRedoManagerTests.cs;region=UndoRedoManager_DelegateAction;format=md-fenced-code-block;dedent=true -->
```csharp
var manager = new UndoRedoManager();
var items = new List<string>();

// Asynchronous execute, synchronous unexecute
var action = new UndoRedoDelegateAction(
    execute: async cancellationToken =>
    {
        await Task.Delay(TimeSpan.FromMilliseconds(1), cancellationToken);
        items.Add("item");
    },
    unexecute: () => items.Remove("item"));

await manager.RecordActionAsync(action);
Assert.Equal(["item"], items);
```
<!-- endref -->

[UndoRedoDelegateAction](cref:Meziantou.Framework.UndoRedo.UndoRedoDelegateAction) is the action the delegate overloads of [RecordActionAsync](cref:Meziantou.Framework.UndoRedo.UndoRedoManager.RecordActionAsync) create. Create it yourself to set
its [Description](cref:Meziantou.Framework.UndoRedo.UndoRedoActionBase.Description) or [AllowToMergeWithPrevious](cref:Meziantou.Framework.UndoRedo.UndoRedoActionBase.AllowToMergeWithPrevious).

A token that is already canceled makes [RecordActionAsync](cref:Meziantou.Framework.UndoRedo.UndoRedoManager.RecordActionAsync), [UndoAsync](cref:Meziantou.Framework.UndoRedo.UndoRedoManager.UndoAsync) and [RedoAsync](cref:Meziantou.Framework.UndoRedo.UndoRedoManager.RedoAsync) throw an
`OperationCanceledException` before anything runs.

## Next steps

- [Actions](03-Actions.md): write reusable action classes.
- [Transactions](04-Transactions.md): make several changes a single undo step.
