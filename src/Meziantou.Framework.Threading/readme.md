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

Simplifying task awaiting ([blog post](https://www.meziantou.net/get-the-result-of-multiple-tasks-in-a-valuetuple-and-whenall.htm)). The extension methods are in the `Meziantou.Framework.Threading.Tasks` namespace.

<!-- ref:../../tests/Meziantou.Framework.Threading.Tests/TaskExtensionsTests.cs;region=TaskExtensions_WhenAll;format=md-fenced-code-block;dedent=true -->
```csharp
var task1 = Task.FromResult(42);
var task2 = Task.FromResult("value");

var (a, b) = await (task1, task2);
Assert.Equal(42, a);
Assert.Equal("value", b);

var (c, d) = await (task1, task2).ConfigureAwait(false);
Assert.Equal(42, c);
Assert.Equal("value", d);
```
<!-- endref -->

Thread-safe collection that can be bound to a UI control (`ConcurrentObservableCollection<T>` is in the `Meziantou.Framework.Collections.Concurrent` namespace)

<!-- ref:../../samples/Meziantou.Framework.WPF.CollectionSamples/ConcurrentObservableCollectionReadmeExamples.cs;region=ConcurrentObservableCollection_Bind;format=md-fenced-code-block;dedent=true -->
```csharp
// Created on the UI thread, so the notifications are raised on that thread
var collection = new ConcurrentObservableCollection<string>();
listBox.ItemsSource = collection.AsObservable;

// The collection itself is safe to use from any thread
await Task.Run(() => collection.Add("Item from a background thread"));
```
<!-- endref -->

The thread the notifications are raised on can also be set explicitly with a `SynchronizationContext`:

<!-- ref:../../tests/Meziantou.Framework.Threading.Tests/ObservableCollectionTests.cs;region=ConcurrentObservableCollection_SynchronizationContext;format=md-fenced-code-block;dedent=true -->
```csharp
var collection = new ConcurrentObservableCollection<string>(synchronizationContext);
```
<!-- endref -->
