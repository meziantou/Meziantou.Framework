# Meziantou.Framework.Threading

Provides types such as
- `ConcurrentHashSet<T>`
- `ConcurrentObservableCollection<T>`
- `SynchronizedList<T>`
- `AsyncAutoResetEvent`
- `AsyncLock`
- `ResettableCancellationTokenSource`
- `KeyedLock`
- `KeyedAsyncLock`

Provides extensions methods for
- `SemaphoreSlim`
- `Task`

Simplifying task awaiting ([blog post](https://www.meziantou.net/get-the-result-of-multiple-tasks-in-a-valuetuple-and-whenall.htm))

```c#
var (a, b) = await (task1, task2);
var (a, b) = await (task1, task2).ConfigureAwait(false);
```

Thread-safe collection that can be bound to a UI control

```c#
using Meziantou.Framework.Collections.Concurrent;

// Created on the UI thread, so the notifications are raised on that thread
var collection = new ConcurrentObservableCollection<string>();
listBox.ItemsSource = collection.AsObservable;

// The collection itself is safe to use from any thread
await Task.Run(() => collection.Add("Item from a background thread"));
```

The thread the notifications are raised on can also be set explicitly:

```c#
var collection = new ConcurrentObservableCollection<string>(synchronizationContext);
```
