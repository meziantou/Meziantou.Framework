---
title: Signaling and cancellation
description: Wake a waiting task with AsyncAutoResetEvent, and reuse a cancellation token source with ResettableCancellationTokenSource.
---

## Wake a waiting task with AsyncAutoResetEvent

`AsyncAutoResetEvent` lets a task wait asynchronously until another one signals it. Each call to `Set` releases one
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

- `Set` releases the oldest waiter. When nobody waits, the event stays signaled and the next `WaitAsync` completes
  immediately and resets it.
- The event is signaled or not: calling `Set` several times while nobody waits stores a single signal, not one per call.
- `new AsyncAutoResetEvent(initialState: true)` starts signaled, so the first `WaitAsync` completes immediately.
- `WaitAsync(cancellationToken)` gives up waiting when the token is canceled.
- `new AsyncAutoResetEvent(initialState, allowInliningAwaiters: true)` lets the released waiter continue synchronously on
  the thread that calls `Set`. By default, it continues asynchronously.

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

A `CancellationTokenSource` cannot be reused once it is canceled. `ResettableCancellationTokenSource` can: `Reset` gives a
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

`Token`, `IsCancellationRequested`, `Cancel` and `CancelAfter` work like the `CancellationTokenSource` members of the same
name. All the members are safe to call concurrently.

### Cancel on reset and on dispose

The options decide whether `Reset` and `Dispose` cancel the current token:

| Constructor | Reset cancels the token | Dispose cancels the token |
| --- | --- | --- |
| `new ResettableCancellationTokenSource(cancelOnResetAndDispose: true)` | Yes | Yes |
| `new ResettableCancellationTokenSource(cancelOnResetAndDispose: false)` | No | No |
| `new ResettableCancellationTokenSource(ResettableCancellationTokenSourceOptions.CancelOnReset)` | Yes | No |
| `new ResettableCancellationTokenSource(ResettableCancellationTokenSourceOptions.CancelOnDispose)` | No | Yes |

`ResettableCancellationTokenSourceOptions` is a flags enum: combine `CancelOnReset | CancelOnDispose` to get both.

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

`Reset` reuses the underlying `CancellationTokenSource` when it can, so a token read before the reset behaves
differently depending on the state of the source:

- When cancellation was already requested (by `Cancel`, an elapsed `CancelAfter` delay, or `CancelOnReset`), the
  source is replaced. The old token stays canceled and never reacts to a later `Cancel`.
- Otherwise the same source is reused: the old token is the one `Token` returns after the reset, and it reacts to a later
  `Cancel`. The reset removes the callbacks registered on it, which are never invoked, and disarms a pending
  `CancelAfter` delay.

Read `Token` again after each reset rather than relying on either behavior.

### Callbacks

The callbacks registered on `Token` run while the internal lock is held when `Cancel`, `Reset` or `Dispose` cancels the
token: a callback must not block waiting for another thread that uses the same instance. `DisposeAsync` runs the
callbacks on another thread and awaits them instead, so a callback can use the instance. When a callback throws, `Reset`
and `Dispose` still complete before the exception reaches the caller.
