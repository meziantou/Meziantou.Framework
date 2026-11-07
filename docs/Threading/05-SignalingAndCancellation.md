---
title: Signaling and cancellation
description: Wake a waiting task with AsyncAutoResetEvent, and reuse a cancellation token source with ResettableCancellationTokenSource.
---

## Wake a waiting task with AsyncAutoResetEvent

[AsyncAutoResetEvent](cref:Meziantou.Framework.Threading.AsyncAutoResetEvent) lets a task wait asynchronously until another one signals it. Each call to [Set](cref:Meziantou.Framework.Threading.AsyncAutoResetEvent.Set) releases one
waiter:

<!-- ref:../../tests/Meziantou.Framework.Threading.Tests/AsyncAutoResetEventTests.cs;region=AsyncAutoResetEvent_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var autoResetEvent = new AsyncAutoResetEvent(initialState: false);

// In one task
var waitingTask = Task.Run(async () =>
{
    await autoResetEvent.WaitAsync();
    return "Event was set!";
});

// In another task
autoResetEvent.Set();
Assert.Equal("Event was set!", await waitingTask);
```
<!-- endref -->

- [Set](cref:Meziantou.Framework.Threading.AsyncAutoResetEvent.Set) releases the oldest waiter. When nobody waits, the event stays signaled and the next [WaitAsync](cref:Meziantou.Framework.Threading.AsyncAutoResetEvent.WaitAsync) completes
  immediately and resets it.
- The event is signaled or not: calling [Set](cref:Meziantou.Framework.Threading.AsyncAutoResetEvent.Set) several times while nobody waits stores a single signal, not one per call.
- `new AsyncAutoResetEvent(initialState: true)` starts signaled, so the first [WaitAsync](cref:Meziantou.Framework.Threading.AsyncAutoResetEvent.WaitAsync) completes immediately.
- `WaitAsync(cancellationToken)` gives up waiting when the token is canceled.
- `new AsyncAutoResetEvent(initialState, allowInliningAwaiters: true)` lets the released waiter continue synchronously on
  the thread that calls [Set](cref:Meziantou.Framework.Threading.AsyncAutoResetEvent.Set). By default, it continues asynchronously.

<!-- ref:../../tests/Meziantou.Framework.Threading.Tests/AsyncAutoResetEventTests.cs;region=AsyncAutoResetEvent_SetBeforeWait;format=md-fenced-code-block;dedent=true -->
```csharp
var autoResetEvent = new AsyncAutoResetEvent(initialState: false);

// Nobody is waiting: the event stays signaled until a single waiter consumes it
autoResetEvent.Set();
autoResetEvent.Set();
await autoResetEvent.WaitAsync(); // Completes immediately and resets the event

// Setting the event twice did not store two signals
var secondWait = autoResetEvent.WaitAsync();
Assert.False(secondWait.IsCompleted);

autoResetEvent.Set();
await secondWait;
```
<!-- endref -->

## Reuse a cancellation token source

A `CancellationTokenSource` cannot be reused once it is canceled. [ResettableCancellationTokenSource](cref:Meziantou.Framework.Threading.ResettableCancellationTokenSource) can: [Reset](cref:Meziantou.Framework.Threading.ResettableCancellationTokenSource.Reset) gives a
fresh token, so one instance can serve, for instance, every search of a search box, canceling the previous search when
a new one starts.

<!-- ref:../../tests/Meziantou.Framework.Threading.Tests/ResettableCancellationTokenSourceTests.cs;region=ResettableCancellationTokenSource_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
using var cts = new ResettableCancellationTokenSource(cancelOnResetAndDispose: true);

// Use the token
var token = cts.Token;
await Task.Delay(1, token);

// Reset to reuse: the previous token is canceled, read Token again to get a new one
cts.Reset();
Assert.True(token.IsCancellationRequested);
await Task.Delay(1, cts.Token);
```
<!-- endref -->

[Token](cref:Meziantou.Framework.Threading.ResettableCancellationTokenSource.Token), [IsCancellationRequested](cref:Meziantou.Framework.Threading.ResettableCancellationTokenSource.IsCancellationRequested), [Cancel](cref:Meziantou.Framework.Threading.ResettableCancellationTokenSource.Cancel) and [CancelAfter](cref:Meziantou.Framework.Threading.ResettableCancellationTokenSource.CancelAfter) work like the `CancellationTokenSource` members of the same
name. All the members are safe to call concurrently.

### Cancel on reset and on dispose

The options decide whether [Reset](cref:Meziantou.Framework.Threading.ResettableCancellationTokenSource.Reset) and [Dispose](cref:Meziantou.Framework.Threading.ResettableCancellationTokenSource.Dispose) cancel the current token:

| Constructor | Reset cancels the token | Dispose cancels the token |
| --- | --- | --- |
| `new ResettableCancellationTokenSource(cancelOnResetAndDispose: true)` | Yes | Yes |
| `new ResettableCancellationTokenSource(cancelOnResetAndDispose: false)` | No | No |
| `new ResettableCancellationTokenSource(ResettableCancellationTokenSourceOptions.CancelOnReset)` | Yes | No |
| `new ResettableCancellationTokenSource(ResettableCancellationTokenSourceOptions.CancelOnDispose)` | No | Yes |

[ResettableCancellationTokenSourceOptions](cref:Meziantou.Framework.Threading.ResettableCancellationTokenSourceOptions) is a flags enum: combine `CancelOnReset | CancelOnDispose` to get both.

<!-- ref:../../tests/Meziantou.Framework.Threading.Tests/ResettableCancellationTokenSourceTests.cs;region=ResettableCancellationTokenSource_Options;format=md-fenced-code-block;dedent=true -->
```csharp
using var cts = new ResettableCancellationTokenSource(ResettableCancellationTokenSourceOptions.CancelOnDispose);

// Reset gives a fresh token after a cancellation
cts.Cancel();
var canceledToken = cts.Token;
cts.Reset();
Assert.True(canceledToken.IsCancellationRequested);
Assert.False(cts.IsCancellationRequested);

// CancelOnDispose cancels the current token when the source is disposed
var token = cts.Token;
cts.Dispose();
Assert.True(token.IsCancellationRequested);
```
<!-- endref -->

### Read Token again after a reset

[Reset](cref:Meziantou.Framework.Threading.ResettableCancellationTokenSource.Reset) reuses the underlying `CancellationTokenSource` when it can, so a token read before the reset behaves
differently depending on the state of the source:

- When cancellation was already requested (by [Cancel](cref:Meziantou.Framework.Threading.ResettableCancellationTokenSource.Cancel), an elapsed [CancelAfter](cref:Meziantou.Framework.Threading.ResettableCancellationTokenSource.CancelAfter) delay, or [CancelOnReset](cref:Meziantou.Framework.Threading.ResettableCancellationTokenSourceOptions.CancelOnReset)), the
  source is replaced. The old token stays canceled and never reacts to a later [Cancel](cref:Meziantou.Framework.Threading.ResettableCancellationTokenSource.Cancel).
- Otherwise the same source is reused: the old token is the one [Token](cref:Meziantou.Framework.Threading.ResettableCancellationTokenSource.Token) returns after the reset, and it reacts to a later
  [Cancel](cref:Meziantou.Framework.Threading.ResettableCancellationTokenSource.Cancel). The reset removes the callbacks registered on it, which are never invoked, and disarms a pending
  [CancelAfter](cref:Meziantou.Framework.Threading.ResettableCancellationTokenSource.CancelAfter) delay.

Read [Token](cref:Meziantou.Framework.Threading.ResettableCancellationTokenSource.Token) again after each reset rather than relying on either behavior.

### Callbacks

The callbacks registered on [Token](cref:Meziantou.Framework.Threading.ResettableCancellationTokenSource.Token) run while the internal lock is held when [Cancel](cref:Meziantou.Framework.Threading.ResettableCancellationTokenSource.Cancel), [Reset](cref:Meziantou.Framework.Threading.ResettableCancellationTokenSource.Reset) or [Dispose](cref:Meziantou.Framework.Threading.ResettableCancellationTokenSource.Dispose) cancels the
token: a callback must not block waiting for another thread that uses the same instance. [DisposeAsync](cref:Meziantou.Framework.Threading.ResettableCancellationTokenSource.DisposeAsync) runs the
callbacks on another thread and awaits them instead, so a callback can use the instance. When a callback throws, [Reset](cref:Meziantou.Framework.Threading.ResettableCancellationTokenSource.Reset)
and [Dispose](cref:Meziantou.Framework.Threading.ResettableCancellationTokenSource.Dispose) still complete before the exception reaches the caller.
