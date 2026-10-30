---
title: Locks
description: Protect a resource in asynchronous code with AsyncLock and AsyncReaderWriterLock, try to acquire a lock without waiting, cancel a wait, and use SemaphoreSlim in a using block.
---

## Exclusive access with AsyncLock

[AsyncLock](cref:Meziantou.Framework.Threading.AsyncLock) gives exclusive access to a resource, like the `lock` statement, but the critical section can contain
`await`:

<!-- ref:../../tests/Meziantou.Framework.Threading.Tests/AsyncLockTests.cs;region=AsyncLock_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var asyncLock = new AsyncLock();
var counter = 0;

async Task AccessResourceAsync()
{
    using (await asyncLock.LockAsync())
    {
        // Critical section - only one task can execute this at a time
        var value = counter;
        await Task.Yield();
        counter = value + 1;
    }
}

await Task.WhenAll(AccessResourceAsync(), AccessResourceAsync(), AccessResourceAsync());
Assert.Equal(3, counter);
```
<!-- endref -->

- Waiters acquire the lock in the order they called [LockAsync](cref:Meziantou.Framework.Threading.AsyncLock.LockAsync).
- The lock is not reentrant: calling [LockAsync](cref:Meziantou.Framework.Threading.AsyncLock.LockAsync) again from inside the critical section waits for a release that never
  comes. Keep the code that needs the lock in one place rather than nesting acquisitions.
- Only the first disposal of a lease releases the lock. Disposing the same lease again, or a copy of a disposed lease,
  does nothing.
- `new AsyncLock(allowInliningAwaiters: true)` lets the next waiter continue synchronously on the thread that releases
  the lock. By default, its continuation runs asynchronously, so releasing the lock never runs someone else's code.

### Acquire the lock only if it is free

[TryLock](cref:Meziantou.Framework.Threading.AsyncLock.TryLock) never waits. It returns `false` when the lock is held, and gives a lease to dispose when it returns `true`:

<!-- ref:../../tests/Meziantou.Framework.Threading.Tests/AsyncLockTests.cs;region=AsyncLock_TryLock;format=md-fenced-code-block;dedent=true -->
```csharp
var asyncLock = new AsyncLock();

// TryLock never waits: it returns false when the lock is already held
Assert.True(asyncLock.TryLock(out var lease));
using (lease)
{
    Assert.False(asyncLock.TryLock(out _));
}

// Disposing the lease released the lock
Assert.True(asyncLock.TryLock(out var secondLease));
secondLease.Dispose();
```
<!-- endref -->

### Stop waiting for the lock

Pass a `CancellationToken` to [LockAsync](cref:Meziantou.Framework.Threading.AsyncLock.LockAsync) to give up waiting. A canceled wait throws an `OperationCanceledException` and
does not acquire the lock:

<!-- ref:../../tests/Meziantou.Framework.Threading.Tests/AsyncLockTests.cs;region=AsyncLock_Cancellation;format=md-fenced-code-block;dedent=true -->
```csharp
var asyncLock = new AsyncLock();
using (await asyncLock.LockAsync())
{
    // Another caller gives up waiting for the lock when its token is canceled
    using var cts = new CancellationTokenSource();
    var waiting = asyncLock.LockAsync(cts.Token).AsTask();
    await cts.CancelAsync();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiting);
}
```
<!-- endref -->

## Readers and writers with AsyncReaderWriterLock

When most accesses only read the resource, [AsyncReaderWriterLock](cref:Meziantou.Framework.Threading.AsyncReaderWriterLock) lets several readers in at the same time, and gives
writers exclusive access:

<!-- ref:../../tests/Meziantou.Framework.Threading.Tests/AsyncReaderWriterLockTests.cs;region=AsyncReaderWriterLock_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var rwLock = new AsyncReaderWriterLock();
var data = new List<int>();

// Multiple readers can execute concurrently
async Task<int> ReadAsync()
{
    using (await rwLock.ReaderLockAsync())
    {
        return data.Count;
    }
}

// Only one writer can execute at a time
async Task WriteAsync(int value)
{
    using (await rwLock.WriterLockAsync())
    {
        data.Add(value);
    }
}

await Task.WhenAll(WriteAsync(1), WriteAsync(2));
Assert.Equal([2, 2], await Task.WhenAll(ReadAsync(), ReadAsync()));
```
<!-- endref -->

- [ReaderLockAsync](cref:Meziantou.Framework.Threading.AsyncReaderWriterLock.ReaderLockAsync) and [WriterLockAsync](cref:Meziantou.Framework.Threading.AsyncReaderWriterLock.WriterLockAsync) return a releaser; dispose it to release the lock. Both accept a
  `CancellationToken`.
- Writers have priority: once a writer waits, new readers queue behind it, so a steady stream of readers cannot starve
  the writers.
- Always await the returned value and dispose the releaser. A wait that is started and never awaited still gets the
  lock when its turn comes, and nothing releases it. To give up on a wait, cancel its token. To start a wait and await it
  later, store `ReaderLockAsync().AsTask()` rather than the value task itself.

## SemaphoreSlim in a using block

The `SemaphoreSlim` extensions wait on the semaphore and return an object that releases it when disposed, so a
`using` block releases the semaphore even when the code inside throws. Use a semaphore to let a fixed number of callers
in at the same time:

<!-- ref:../../tests/Meziantou.Framework.Threading.Tests/SemaphoreSlimExtensionsTests.cs;region=SemaphoreSlim_DisposableWaitAsync;format=md-fenced-code-block;dedent=true -->
```csharp
using var semaphore = new SemaphoreSlim(initialCount: 2);

// Disposing the returned object releases the semaphore, even when the block throws
using (await semaphore.DisposableWaitAsync())
{
    Assert.Equal(1, semaphore.CurrentCount);
}

Assert.Equal(2, semaphore.CurrentCount);
```
<!-- endref -->

| Method | Returns | Disposing it more than once |
| --- | --- | --- |
| [DisposableWait](cref:Meziantou.Framework.Threading.SemaphoreSlimExtensions.DisposableWait), [DisposableWaitAsync](cref:Meziantou.Framework.Threading.SemaphoreSlimExtensions.DisposableWaitAsync) | An `IDisposable` | Releases the semaphore once |
| [DisposableWaitUnsafe](cref:Meziantou.Framework.Threading.SemaphoreSlimExtensions.DisposableWaitUnsafe), [DisposableWaitUnsafeAsync](cref:Meziantou.Framework.Threading.SemaphoreSlimExtensions.DisposableWaitUnsafeAsync) | A [SemaphoreDisposer](cref:Meziantou.Framework.Threading.SemaphoreSlimExtensions.SemaphoreDisposer) struct | Releases the semaphore again for each copy |

The unsafe variants avoid allocating an object, but every copy of the struct releases the semaphore: dispose exactly one
copy, exactly once. Prefer [DisposableWait](cref:Meziantou.Framework.Threading.SemaphoreSlimExtensions.DisposableWait) and [DisposableWaitAsync](cref:Meziantou.Framework.Threading.SemaphoreSlimExtensions.DisposableWaitAsync) unless the allocation matters. All of them accept a
`CancellationToken`.

## Choose a lock

| You need | Use |
| --- | --- |
| Exclusive access, with `await` inside | [AsyncLock](cref:Meziantou.Framework.Threading.AsyncLock) |
| Many readers or one writer, with `await` inside | [AsyncReaderWriterLock](cref:Meziantou.Framework.Threading.AsyncReaderWriterLock) |
| Up to N callers at the same time | `SemaphoreSlim` with [DisposableWaitAsync](cref:Meziantou.Framework.Threading.SemaphoreSlimExtensions.DisposableWaitAsync) |
| One caller at a time per key | [KeyedAsyncLock\<TKey\>](cref:Meziantou.Framework.Threading.KeyedAsyncLock{TKey}), or [KeyedLock\<TKey\>](cref:Meziantou.Framework.Threading.KeyedLock{TKey}) for synchronous code. See [Keyed locks](04-KeyedLocks.md) |
