---
title: Getting started
description: Install the package, protect a resource with an asynchronous lock, and await several tasks at once.
---

## Install the package

Add the `Meziantou.Framework.Threading` package to a project:

```bash
dotnet add package Meziantou.Framework.Threading
```

The types are in three namespaces: `Meziantou.Framework.Threading`, `Meziantou.Framework.Threading.Tasks` and
`Meziantou.Framework.Collections.Concurrent`. See [Introduction](01-Introduction.md#namespaces) for what each contains.

## Protect a resource in asynchronous code

The `lock` statement cannot contain an `await`. `AsyncLock` can: only one caller at a time runs the code inside the
`using` block, and the others wait asynchronously.

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

- `LockAsync` returns a lease once the lock is acquired. Disposing the lease, at the end of the `using` block, releases
  the lock.
- Without the lock, the three calls could read the same value of `counter` before any of them writes it back.

## Await several tasks and get their results

With the `Meziantou.Framework.Threading.Tasks` namespace, a tuple of tasks can be awaited directly. The result is a
tuple of the results, which you can deconstruct:

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

The tasks run concurrently: awaiting the tuple waits for all of them, like `Task.WhenAll`.

## Next steps

- [Locks](03-Locks.md): reader-writer locks, `TryLock`, cancellation and `SemaphoreSlim`.
- [Awaiting tasks](08-AwaitingTasks.md): `ValueTask` tuples, failures, and fire-and-forget.
