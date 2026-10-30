---
title: Keyed locks
description: Allow one operation at a time per key, such as a user or a file, while operations on different keys run concurrently.
---

A keyed lock is a set of locks, one per key: two callers that use the same key wait for each other, and callers with
different keys run concurrently. Use it to serialize the operations on one user, one file or one cache entry without
serializing the whole application.

## In asynchronous code

[KeyedAsyncLock\<TKey\>.LockAsync](cref:Meziantou.Framework.Threading.KeyedAsyncLock{TKey}.LockAsync) waits asynchronously for the lock of a key and returns a lease that releases it:

<!-- ref:../../tests/Meziantou.Framework.Threading.Tests/KeyedAsyncLockTests.cs;region=KeyedAsyncLock_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var keyedLock = new KeyedAsyncLock<string>(StringComparer.Ordinal);
var updateCounts = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

async Task ProcessUserAsync(string userId)
{
    using (await keyedLock.LockAsync(userId))
    {
        // Only one operation per userId at a time
        // Multiple different userIds can be processed concurrently
        var count = updateCounts.GetValueOrDefault(userId);
        await Task.Yield();
        updateCounts[userId] = count + 1;
    }
}

await Task.WhenAll(ProcessUserAsync("alice"), ProcessUserAsync("alice"), ProcessUserAsync("bob"));
Assert.Equal(2, updateCounts["alice"]);
Assert.Equal(1, updateCounts["bob"]);
```
<!-- endref -->

- The constructor takes the `IEqualityComparer<TKey>` that decides whether two keys are the same lock, for instance
  `StringComparer.OrdinalIgnoreCase` for case-insensitive keys. Pass `null` to use the default comparer of the key type.
- `LockAsync(key, cancellationToken)` accepts a `CancellationToken` to give up waiting.
- Only the first disposal of a lease releases the key.

## In synchronous code

[KeyedLock\<TKey\>.Lock](cref:Meziantou.Framework.Threading.KeyedLock{TKey}.Lock) blocks the calling thread until the lock of the key is free, and returns an `IDisposable` that
releases it:

<!-- ref:../../tests/Meziantou.Framework.Threading.Tests/KeyedLockTests.cs;region=KeyedLock_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var keyedLock = new KeyedLock<string>(StringComparer.Ordinal);
var updateCounts = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

void ProcessUser(string userId)
{
    using (keyedLock.Lock(userId))
    {
        // Only one operation per userId at a time
        // Multiple different userIds can be processed concurrently
        var count = updateCounts.GetValueOrDefault(userId);
        updateCounts[userId] = count + 1;
    }
}

Parallel.Invoke(() => ProcessUser("alice"), () => ProcessUser("alice"), () => ProcessUser("bob"));
Assert.Equal(2, updateCounts["alice"]);
Assert.Equal(1, updateCounts["bob"]);
```
<!-- endref -->

[KeyedLock\<TKey\>](cref:Meziantou.Framework.Threading.KeyedLock{TKey}) is thread-affine: the returned object must be disposed on the thread that called [Lock](cref:Meziantou.Framework.Threading.KeyedLock{TKey}.Lock). Disposing it
on another thread throws a `SynchronizationLockException` and leaves the lock held. The critical section must therefore
not contain an `await`; use [KeyedAsyncLock\<TKey\>](cref:Meziantou.Framework.Threading.KeyedAsyncLock{TKey}) in asynchronous code.

## Memory use

The locks only track the keys that are held or waited for: the state of a key is removed when its last lease is
released. A keyed lock used with many distinct keys over time does not grow with the number of keys it has seen.
