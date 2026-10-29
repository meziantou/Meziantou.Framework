---
title: Awaiting tasks
description: Await a tuple of tasks or value tasks and get their results, understand failures, fire and forget a task, and resume on a SynchronizationContext.
---

The task helpers are extension methods in the `Meziantou.Framework.Threading.Tasks` namespace, except the
`SynchronizationContext` awaiter, which is in `Meziantou.Framework.Threading`.

## Await a tuple of tasks

Await a tuple of up to seven tasks to wait for all of them. With `Task<T>`, the result is the tuple of their results:

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

- A tuple of `Task` (without a result) can be awaited too; it returns nothing.
- `ConfigureAwait(false)` or `ConfigureAwait(ConfigureAwaitOptions)` on the tuple configures the await, like on a single
  task.
- The tasks are started by the calls in the tuple, before the `await`, so they run concurrently.

## Await a tuple of value tasks

Tuples of up to seven `ValueTask<T>` or `ValueTask` can be awaited the same way. `TaskExtensions.WhenAll` does the same
with method arguments, and returns a `ValueTask` of the tuple of results:

<!-- ref:../../tests/Meziantou.Framework.Threading.Tests/TaskExtensionsTests.cs;region=TaskExtensions_ValueTaskTuple;format=md-fenced-code-block;dedent=true -->
```csharp
static async ValueTask<int> GetCountAsync()
{
    await Task.Yield();
    return 42;
}

static ValueTask<string> GetNameAsync() => ValueTask.FromResult("value");

// Await a tuple of ValueTask<T>
var (count, name) = await (GetCountAsync(), GetNameAsync());
Assert.Equal(42, count);
Assert.Equal("value", name);

// Or call WhenAll explicitly
var results = await Meziantou.Framework.Threading.Tasks.TaskExtensions.WhenAll(GetCountAsync(), GetNameAsync());
Assert.Equal((42, "value"), results);
```
<!-- endref -->

- `System.Threading.Tasks` also has a `TaskExtensions` class, so calling `TaskExtensions.WhenAll` needs the full name
  `Meziantou.Framework.Threading.Tasks.TaskExtensions` when both namespaces are imported.
- The CA2012 analyzer ("Use ValueTasks correctly") reports the value tasks of an awaited tuple, even though awaiting the
  tuple consumes each of them once. Suppress it where you await such a tuple.
- Tuples of value tasks have no `ConfigureAwait` method.

## When a task fails

Awaiting a tuple behaves like `Task.WhenAll`: every task is awaited, even when one of them fails, and then:

- If at least one task failed, awaiting the tuple throws the first failure. The task returned by
  `TaskExtensions.WhenAll` carries all of them.
- Otherwise, if at least one task was canceled, awaiting the tuple throws an `OperationCanceledException`.

<!-- ref:../../tests/Meziantou.Framework.Threading.Tests/TaskExtensionsTests.cs;region=TaskExtensions_WhenAllFailure;format=md-fenced-code-block;dedent=true -->
```csharp
static async Task<int> SucceedAsync()
{
    await Task.Yield();
    return 1;
}

static async Task<int> FailAsync()
{
    await Task.Yield();
    throw new InvalidOperationException("Failed");
}

// Every task is awaited, then the first failure is thrown, as with Task.WhenAll
var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await (SucceedAsync(), FailAsync()));
Assert.Equal("Failed", exception.Message);
```
<!-- endref -->

## Fire and forget a task

`Forget` lets a task run without awaiting it. Its exception, if any, is observed, so it is never reported as an
unobserved task exception:

<!-- ref:../../tests/Meziantou.Framework.Threading.Tests/TaskExtensionsTests.cs;region=TaskExtensions_Forget;format=md-fenced-code-block;dedent=true -->
```csharp
static async Task SendTelemetryAsync()
{
    await Task.Yield();
    throw new InvalidOperationException("The telemetry endpoint is unavailable");
}

// Start the task without awaiting it: its exception is observed, so it is never reported
// as an unobserved task exception
SendTelemetryAsync().Forget();
```
<!-- endref -->

`Forget` discards the exception: log it inside the task when you need to know about it.

## Resume on a SynchronizationContext

Awaiting a `SynchronizationContext` posts the rest of the method to that context. Use it to come back to the UI thread
after work done with `ConfigureAwait(false)`:

<!-- ref:../../tests/Meziantou.Framework.Threading.Tests/SynchronizationContextExtensionsTests.cs;region=SynchronizationContext_Await;format=md-fenced-code-block;dedent=true -->
```csharp
async Task<int> ComputeAsync(SynchronizationContext uiContext)
{
    // Do the work on the thread pool
    var result = await Task.Run(() => 6 * 7).ConfigureAwait(false);

    // Resume on the context, for instance the UI thread, before updating the controls
    await uiContext;
    return result;
}
```
<!-- endref -->

- When `SynchronizationContext.Current` is already the awaited context, the method continues synchronously. The
  contexts are compared by instance: another instance bound to the same thread, such as the ones WPF installs while it
  runs a dispatcher operation, is not recognized, and the continuation is posted.
- When the context refuses the continuation, for instance a dispatcher that has shut down, the exception is thrown on the
  thread that completed the awaited operation, and the method never resumes.
