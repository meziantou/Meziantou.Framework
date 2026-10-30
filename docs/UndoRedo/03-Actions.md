---
title: Actions
description: Record changes with delegates or with action classes, and describe them for the undo history.
---

An action is an [IUndoRedoAction](cref:Meziantou.Framework.UndoRedo.IUndoRedoAction): it applies a change in [ExecuteAsync](cref:Meziantou.Framework.UndoRedo.IUndoRedoAction.ExecuteAsync) and reverts it in [UnExecuteAsync](cref:Meziantou.Framework.UndoRedo.IUndoRedoAction.UnExecuteAsync). The manager
calls them in turn: [ExecuteAsync](cref:Meziantou.Framework.UndoRedo.IUndoRedoAction.ExecuteAsync) when the action is recorded (or when its [transaction](04-Transactions.md) is
committed) and when it is redone, [UnExecuteAsync](cref:Meziantou.Framework.UndoRedo.IUndoRedoAction.UnExecuteAsync) when it is undone.

## Delegates or a class

| You write | Use it when |
| --- | --- |
| The delegate overloads of [RecordActionAsync](cref:Meziantou.Framework.UndoRedo.UndoRedoManager.RecordActionAsync) | The change is a few lines of code at one place |
| `new UndoRedoDelegateAction(execute, unexecute)` | The same, with a [Description](cref:Meziantou.Framework.UndoRedo.UndoRedoActionBase.Description) or [AllowToMergeWithPrevious](cref:Meziantou.Framework.UndoRedo.UndoRedoActionBase.AllowToMergeWithPrevious) |
| A class that derives from [UndoRedoActionBase](cref:Meziantou.Framework.UndoRedo.UndoRedoActionBase) | The same kind of change is recorded from several places, or it needs state, such as a merge |
| A class that implements [IUndoRedoAction](cref:Meziantou.Framework.UndoRedo.IUndoRedoAction) | You need full control over the state of the action |

## Write an action class

Derive from [UndoRedoActionBase](cref:Meziantou.Framework.UndoRedo.UndoRedoActionBase) and implement [ExecuteCoreAsync](cref:Meziantou.Framework.UndoRedo.UndoRedoActionBase.ExecuteCoreAsync) and [UnExecuteCoreAsync](cref:Meziantou.Framework.UndoRedo.UndoRedoActionBase.UnExecuteCoreAsync):

<!-- ref:../../tests/Meziantou.Framework.UndoRedo.Tests/UndoRedoManagerTests.cs;region=UndoRedoActionBase_CustomAction;format=md-fenced-code-block;dedent=true -->
```csharp
internal sealed class AddItemAction(IList<int> list, int value) : UndoRedoActionBase
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
```
<!-- endref -->

Record it like any other action:

<!-- ref:../../tests/Meziantou.Framework.UndoRedo.Tests/UndoRedoManagerTests.cs;region=UndoRedoManager_RecordCustomAction;format=md-fenced-code-block;dedent=true -->
```csharp
await manager.RecordActionAsync(new AddItemAction(list, 42));
```
<!-- endref -->

[UndoRedoActionBase](cref:Meziantou.Framework.UndoRedo.UndoRedoActionBase) tracks whether the action is applied. [ExecuteAsync](cref:Meziantou.Framework.UndoRedo.UndoRedoActionBase.ExecuteAsync) throws an `InvalidOperationException` when the
action is already applied, and [UnExecuteAsync](cref:Meziantou.Framework.UndoRedo.UndoRedoActionBase.UnExecuteAsync) when it is not. So an action instance can be recorded only once at a
time: recording the same instance twice would add an undo step that reverts nothing. Create a new instance for each
change.

## Describe an action

Set [Description](cref:Meziantou.Framework.UndoRedo.UndoRedoActionBase.Description) to the text to show in an undo history, such as "Add circle". The manager exposes the recorded
actions through [UndoableActions](cref:Meziantou.Framework.UndoRedo.UndoRedoManager.UndoableActions) and [RedoableActions](cref:Meziantou.Framework.UndoRedo.UndoRedoManager.RedoableActions): see
[Undo history in the UI](06-UndoHistoryInTheUI.md#show-the-history).

[UndoRedoActionBase.Description](cref:Meziantou.Framework.UndoRedo.UndoRedoActionBase.Description) is virtual, so an action class can compute the text from its state. For an action that
implements [IUndoRedoAction](cref:Meziantou.Framework.UndoRedo.IUndoRedoAction) directly, [Description](cref:Meziantou.Framework.UndoRedo.IUndoRedoAction.Description) returns `null` unless you implement it.

## Rules for actions

- Revert exactly what [ExecuteAsync](cref:Meziantou.Framework.UndoRedo.IUndoRedoAction.ExecuteAsync) applied: undo is correct only if [UnExecuteAsync](cref:Meziantou.Framework.UndoRedo.IUndoRedoAction.UnExecuteAsync) is the inverse of [ExecuteAsync](cref:Meziantou.Framework.UndoRedo.IUndoRedoAction.ExecuteAsync).
- Do not record, undo or redo from inside an action. While an action runs, [ActionIsExecuting](cref:Meziantou.Framework.UndoRedo.UndoRedoManager.ActionIsExecuting) is `true`, and those
  operations throw an `InvalidOperationException`.
- When [UnExecuteAsync](cref:Meziantou.Framework.UndoRedo.IUndoRedoAction.UnExecuteAsync) throws, the action stays in the undo history, and the exception reaches the caller of
  [UndoAsync](cref:Meziantou.Framework.UndoRedo.UndoRedoManager.UndoAsync). The same goes for [ExecuteAsync](cref:Meziantou.Framework.UndoRedo.IUndoRedoAction.ExecuteAsync) and [RedoAsync](cref:Meziantou.Framework.UndoRedo.UndoRedoManager.RedoAsync).
