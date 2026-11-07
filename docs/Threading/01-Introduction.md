---
title: Introduction
description: Asynchronous locks, signals, thread-safe collections, and helpers to await several tasks and schedule work.
---

`Meziantou.Framework.Threading` provides the synchronization primitives that .NET does not have out of the box for
asynchronous code, a few thread-safe collections, and helpers that make awaiting tasks and scheduling work simpler.

## What you can do with it

- **Protect a resource in asynchronous code** with [AsyncLock](cref:Meziantou.Framework.Threading.AsyncLock) and [AsyncReaderWriterLock](cref:Meziantou.Framework.Threading.AsyncReaderWriterLock), and use `SemaphoreSlim` in a
  `using` block. See [Locks](03-Locks.md).
- **Lock per key**, for instance one operation at a time per user, with [KeyedLock\<TKey\>](cref:Meziantou.Framework.Threading.KeyedLock{TKey}) and [KeyedAsyncLock\<TKey\>](cref:Meziantou.Framework.Threading.KeyedAsyncLock{TKey}).
  See [Keyed locks](04-KeyedLocks.md).
- **Signal a waiting task and reuse a cancellation token source** with [AsyncAutoResetEvent](cref:Meziantou.Framework.Threading.AsyncAutoResetEvent) and
  [ResettableCancellationTokenSource](cref:Meziantou.Framework.Threading.ResettableCancellationTokenSource). See [Signaling and cancellation](05-SignalingAndCancellation.md).
- **Share a set or a list between threads** with [ConcurrentHashSet\<T\>](cref:Meziantou.Framework.Collections.Concurrent.ConcurrentHashSet{T}) and [SynchronizedList\<T\>](cref:Meziantou.Framework.Collections.Concurrent.SynchronizedList{T}). See
  [Concurrent collections](06-ConcurrentCollections.md).
- **Bind a collection that background threads modify to a UI control** with [ConcurrentObservableCollection\<T\>](cref:Meziantou.Framework.Collections.Concurrent.ConcurrentObservableCollection{T}). See
  [Observable collection](07-ObservableCollection.md).
- **Await several tasks and get their results as a tuple**, fire and forget a task, and resume on a
  `SynchronizationContext`. See [Awaiting tasks](08-AwaitingTasks.md).
- **Run tasks on a dedicated thread** and **process a queue that grows while it is processed**. See
  [Scheduling work](09-SchedulingWork.md).

## Principles

- **Asynchronous waits.** The asynchronous locks and the event never block a thread while waiting: the waiting method is
  suspended and resumed when its turn comes. [AsyncLock](cref:Meziantou.Framework.Threading.AsyncLock) and [AsyncAutoResetEvent](cref:Meziantou.Framework.Threading.AsyncAutoResetEvent) serve waiters in the order they
  started waiting.
- **Cancellable.** Every asynchronous wait accepts a `CancellationToken`, so a caller can give up waiting.
- **Released by `using`.** The locks return a lease that releases them when disposed. Disposing a lease a second time
  does nothing, except for the `SemaphoreSlim` methods whose name says `Unsafe`.
- **Continuations run asynchronously by default.** Releasing a lock or setting the event does not run the code of the
  next waiter on the releasing thread, unless you opt in.

## Namespaces

| Namespace | Contains |
| --- | --- |
| [Meziantou.Framework.Threading](cref:Meziantou.Framework.Threading) | [AsyncLock](cref:Meziantou.Framework.Threading.AsyncLock), [AsyncReaderWriterLock](cref:Meziantou.Framework.Threading.AsyncReaderWriterLock), [KeyedLock\<TKey\>](cref:Meziantou.Framework.Threading.KeyedLock{TKey}), [KeyedAsyncLock\<TKey\>](cref:Meziantou.Framework.Threading.KeyedAsyncLock{TKey}), [AsyncAutoResetEvent](cref:Meziantou.Framework.Threading.AsyncAutoResetEvent), [ResettableCancellationTokenSource](cref:Meziantou.Framework.Threading.ResettableCancellationTokenSource), [MonoThreadedTaskScheduler](cref:Meziantou.Framework.Threading.MonoThreadedTaskScheduler), [MixedConsumerProducer](cref:Meziantou.Framework.Threading.MixedConsumerProducer), and the `SemaphoreSlim` and `SynchronizationContext` extensions |
| [Meziantou.Framework.Threading.Tasks](cref:Meziantou.Framework.Threading.Tasks) | [TaskExtensions](cref:Meziantou.Framework.Threading.Tasks.TaskExtensions): awaiting tuples of tasks, [WhenAll](cref:Meziantou.Framework.Threading.Tasks.TaskExtensions.WhenAll) for `ValueTask`, [Forget](cref:Meziantou.Framework.Threading.Tasks.TaskExtensions.Forget) |
| [Meziantou.Framework.Collections.Concurrent](cref:Meziantou.Framework.Collections.Concurrent) | [ConcurrentHashSet\<T\>](cref:Meziantou.Framework.Collections.Concurrent.ConcurrentHashSet{T}), [SynchronizedList\<T\>](cref:Meziantou.Framework.Collections.Concurrent.SynchronizedList{T}), [ConcurrentObservableCollection\<T\>](cref:Meziantou.Framework.Collections.Concurrent.ConcurrentObservableCollection{T}) |

## Next steps

- [Getting started](02-GettingStarted.md): install the package and protect a resource.
- [Locks](03-Locks.md): choose the right lock.
