# Meziantou.Framework.Threading

Provides types such as
- `ConcurrentHashSet<T>`
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
```
