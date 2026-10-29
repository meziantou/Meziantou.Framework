---
title: Introduction
description: An asynchronous undo/redo framework based on the command pattern, with transactions and merged steps.
---

`Meziantou.Framework.UndoRedo` adds undo and redo to an application, such as an editor or a form. Each change the user
makes is an action that knows how to apply itself and how to revert itself. `UndoRedoManager` runs the actions, keeps
them in a history, and reverts or reapplies them when the user asks to undo or redo.

## What you can do with it

- **Record a change** from two delegates, one that applies it and one that reverts it, or from an action class of your
  own. See [Getting started](02-GettingStarted.md) and [Actions](03-Actions.md).
- **Group several changes** into a single undo step, all applied or none. See [Transactions](04-Transactions.md).
- **Merge consecutive changes**, such as the characters of a word or the moves of a drag, into one undo step. See
  [Merging actions](05-MergingActions.md).
- **Bind the Undo and Redo commands** of a user interface, show the undo history, and limit its size. See
  [Undo history in the UI](06-UndoHistoryInTheUI.md).

## Principles

- **Asynchronous.** Applying and reverting an action return a `ValueTask` and accept a `CancellationToken`, so an
  action can save a file or call a service. Synchronous delegates are accepted too.
- **One action, one state.** An action is applied or reverted in turn, never twice in a row: the manager never applies
  an applied action, and `UndoRedoActionBase` throws when it would happen.
- **All or nothing.** A transaction that fails while applying its actions reverts the ones that already ran. A
  transaction that is not committed is rolled back.
- **Single-threaded.** `UndoRedoManager` is not thread-safe: use it from one logical flow, such as the UI thread.

## Namespace

Every type is in the `Meziantou.Framework.UndoRedo` namespace.

## Next steps

- [Getting started](02-GettingStarted.md): install the package and record your first actions.
- [Migrating from version 3](07-MigratingFromVersion3.md): what changed in version 4.
