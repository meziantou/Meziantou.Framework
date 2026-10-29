---
title: Merging actions
description: Merge consecutive actions, such as typed characters or drag moves, into a single undo step.
---

Some changes come in long series of small steps: each typed character, each move of a drag. Recording them as separate
undo steps forces the user to undo them one by one. Merging collapses each action into the previous one, so one undo
reverts the whole series.

## Merge an action into the previous one

Two conditions make the manager merge an action into the previous one, instead of recording a new undo step:

1. The new action allows it: its `AllowToMergeWithPrevious` property is `true`.
2. The previous action accepts it: its `TryToMergeAsync` method returns `true`.

`TryToMergeAsync` receives the new action. When it returns `true`, the new action is discarded: the manager never
executes or reverts it again, so the previous action becomes responsible for both changes. This action merges the text
of the following insertions:

<!-- ref:../../tests/Meziantou.Framework.UndoRedo.Tests/UndoRedoManagerTests.cs;region=UndoRedoActionBase_MergeAction;format=md-fenced-code-block;dedent=true -->
```csharp
internal sealed class InsertTextAction(StringBuilder document, string text) : UndoRedoActionBase
{
    private string _text = text;

    protected override ValueTask ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        document.Append(_text);
        return ValueTask.CompletedTask;
    }

    protected override ValueTask UnExecuteCoreAsync(CancellationToken cancellationToken)
    {
        document.Remove(document.Length - _text.Length, _text.Length);
        return ValueTask.CompletedTask;
    }

    public override ValueTask<bool> TryToMergeAsync(IUndoRedoAction followingAction, CancellationToken cancellationToken = default)
    {
        if (followingAction is InsertTextAction other)
        {
            // The following text is already in the document (or, in a Deferred transaction, neither text is):
            // take it over so that UnExecuteCoreAsync removes both texts
            _text += other._text;
            return ValueTask.FromResult(true);
        }

        return ValueTask.FromResult(false);
    }
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.UndoRedo.Tests/UndoRedoManagerTests.cs;region=UndoRedoManager_Merge;format=md-fenced-code-block;dedent=true -->
```csharp
var manager = new UndoRedoManager();
var document = new StringBuilder();

await manager.RecordActionAsync(new InsertTextAction(document, "Hel"));
await manager.RecordActionAsync(new InsertTextAction(document, "lo") { AllowToMergeWithPrevious = true });
Assert.Equal("Hello", document.ToString());

// Both insertions are a single undo step
Assert.Single(manager.UndoableActions);
await manager.UndoAsync();
Assert.Equal("", document.ToString());
```
<!-- endref -->

## The state of the merged action

When `TryToMergeAsync` runs, the new action is in the same state as the previous one:

- At the top level and in an `Immediate` transaction, both are already applied.
- In a `Deferred` transaction, neither is applied yet.

So `TryToMergeAsync` must take over the effect of the new action without applying it: in the example, the text is added
to `_text`, not to the document.

## Rules

- Merging works at the top level and inside a [transaction](04-Transactions.md), between actions of that transaction.
- Merging into the previous step clears the redo history, as recording a new step does.
- `TryToMergeAsync` must not record, undo or redo anything: those operations throw an `InvalidOperationException` while
  it runs.
- When `TryToMergeAsync` throws, the new action is recorded as a step of its own, then the exception reaches the caller of
  `RecordActionAsync`.
- `UndoRedoActionBase.TryToMergeAsync` returns `false`: override it to accept merges.
