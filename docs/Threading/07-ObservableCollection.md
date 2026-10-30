---
title: Observable collection
description: Bind a collection that background threads modify to a WPF or Windows Forms control with ConcurrentObservableCollection.
---

UI controls require the change notifications of the collection they display to be raised on the UI thread.
`ConcurrentObservableCollection<T>` can be modified from any thread, and exposes in `AsObservable` a view of the
collection whose notifications are raised on the UI thread.

## Bind the collection to a control

Bind the control to `AsObservable`, and modify the collection itself from any thread:

<!-- ref:../../samples/Meziantou.Framework.WPF.CollectionSamples/ConcurrentObservableCollectionReadmeExamples.cs;region=ConcurrentObservableCollection_Bind;format=md-fenced-code-block;dedent=true -->
```csharp
// Created on the UI thread, so the notifications are raised on that thread
var collection = new ConcurrentObservableCollection<string>();
listBox.ItemsSource = collection.AsObservable;

// The collection itself is safe to use from any thread
await Task.Run(() => collection.Add("Item from a background thread"));
```
<!-- endref -->

- `AsObservable` returns an `IReadOnlyObservableCollection<T>`: a read-only list that implements
  `INotifyCollectionChanged` and `INotifyPropertyChanged`. It reflects the changes made to the collection.
- The parameterless constructor uses the `SynchronizationContext` of the thread that creates the collection, so create
  it on the UI thread. When that thread has no synchronization context, the notifications are raised on the thread pool.

## Choose the thread of the notifications

Pass the `SynchronizationContext` of the UI thread to the constructor to create the collection on another thread:

<!-- ref:../../tests/Meziantou.Framework.Threading.Tests/ObservableCollectionTests.cs;region=ConcurrentObservableCollection_SynchronizationContext;format=md-fenced-code-block;dedent=true -->
```csharp
var collection = new ConcurrentObservableCollection<string>(synchronizationContext);
```
<!-- endref -->

When the collection is modified on the thread of that context, the notifications are raised synchronously. Otherwise,
they are posted to the context. The WPF and Windows Forms contexts are recognized by their thread. To support another
context bound to a single thread, derive from the collection and override `IsOnSynchronizationContextThread`.

## Add several items with one notification

`AddRange` and `InsertRange` raise one notification per item by default, because most UI controls, including the WPF
ones, do not support notifications that contain several items. Set `SupportRangeNotifications` to `true` when your
control supports them:

<!-- ref:../../tests/Meziantou.Framework.Threading.Tests/ObservableCollectionTests.cs;region=ConcurrentObservableCollection_RangeNotifications;format=md-fenced-code-block;dedent=true -->
```csharp
var collection = new ConcurrentObservableCollection<int>
{
    // Raise one notification for AddRange and InsertRange instead of one per item
    SupportRangeNotifications = true,
};

var notifications = new List<NotifyCollectionChangedEventArgs>();
collection.AsObservable.CollectionChanged += (sender, e) => notifications.Add(e);

collection.AddRange(1, 2, 3);

var notification = Assert.Single(notifications);
Assert.Equal(NotifyCollectionChangedAction.Add, notification.Action);
Assert.Equal([1, 2, 3], notification.NewItems!.Cast<int>().ToArray());
```
<!-- endref -->

The collection also provides `Insert`, `Remove`, `RemoveAt`, `Clear`, the indexer, `Sort` and `StableSort`.

## How notifications are raised

- All the notifications of an operation are queued before any of them is raised, and they are never raised recursively.
  A handler that modifies the collection gets the notifications of its own change after the ones already queued, so
  `AsObservable` ends up in the same state as the collection.
- The notifications are raised after the internal lock is released, so a handler can wait for another thread that
  modifies the collection without deadlocking.
- A handler that throws does not prevent the other notifications from being raised. The exception is rethrown to the code
  that modified the collection once all the queued notifications are raised, as an `AggregateException` when several
  handlers threw.
- When the synchronization context refuses the callback, for instance because its thread is gone, the exception is
  thrown to the code that modified the collection. The change is kept, and its notification is raised after a later
  change succeeds in posting to the context.
