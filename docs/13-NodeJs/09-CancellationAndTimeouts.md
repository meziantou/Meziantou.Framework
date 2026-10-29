---
title: Cancellation and timeouts
description: Cancel calls, and kill the process when a canceled call blocks it, such as with an infinite loop.
---

## Canceling a call

Every call takes a `CancellationToken`. Canceling it only stops waiting for the result: the call throws an
`OperationCanceledException`, but its JavaScript code keeps running. Code that never completes, such as an infinite
loop, keeps the event loop of the process busy and blocks the other calls, and killing the process is the only way to
stop it.

`StartAsync` also takes a token, to cancel the startup. The token of `NodeJsHostPool.RunAsync` stops waiting for a
process to be available.

## Kill a process blocked by a canceled call

Set `UnresponsiveTimeout` to kill the process when the code of a canceled call blocks its event loop for longer than
this time:

<!-- ref:../../tests/Meziantou.Framework.NodeJs.Tests/NodeJsHostTests.cs;region=NodeJs_UnresponsiveTimeout;format=md-fenced-code-block;dedent=true -->
```csharp
await using var node = await NodeJsHost.StartAsync(new NodeJsHostOptions
{
    // Kill the process when a canceled call blocks its event loop for more than 2 seconds
    UnresponsiveTimeout = TimeSpan.FromSeconds(2),
});

using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(1));
await Assert.ThrowsAnyAsync<OperationCanceledException>(() => node.EvaluateVoidAsync("while (true) { }", cancellationTokenSource.Token));

// The loop keeps running once the call is canceled, and blocks the other calls until the process is killed
var exception = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("return 1;"));
Assert.Null(exception.ExitCode);
```
<!-- endref -->

- The calls in progress fail with a `NodeJsException` whose `ExitCode` is `null`. The host cannot run code anymore:
  start a new one, or use `NodeJsHostPool`, which replaces the process.
- With `WorkerThreads`, only the worker thread whose event loop is blocked is terminated and restarted. The calls in
  progress on this worker thread fail, and the other worker threads keep running their calls.
- The event loop is blocked by synchronous code, such as an infinite loop, and by promises that never let the event loop
  run other callbacks, such as `while (true) await null;`.
- While canceled calls are running, the process is checked at least every second, so a canceled call that blocks the
  event loop later, after awaiting, is also stopped.

Choose a value larger than the longest synchronous step of a canceled call that you do not want to stop. By default,
the process is never killed.

## Calls that are not canceled are never stopped

The process is killed only when the code that blocks its event loop belongs to a canceled call. A call that is not
canceled is never stopped, even when it keeps the event loop busy for longer than the timeout. To bound the duration of
a call, cancel it, for instance with `new CancellationTokenSource(TimeSpan.FromSeconds(10))`: once canceled, the call is
stopped when it blocks the event loop for longer than `UnresponsiveTimeout`.

The code of a call is its own code, and the callbacks of the promises, timeouts, immediates and I/O operations (such as
reading a file) that it starts, directly or through other callbacks. The code run by long-lived objects belongs to no
call, as these objects can run code for several calls, such as a connection pool created by the first call that needs
it:

- Intervals (`setInterval`), sockets, servers, workers, and the event listeners registered on objects the call did not
  create (such as `process.on(...)`).
- The code that loads a module imported by `InvokeAsync` or by `require`, including its top-level code and the timers
  and connections it creates. The modules loaded using `import()` in the code of a call belong to the call.

Such code is never stopped. A call that runs a loop processing jobs for other calls, such as a recursive `setTimeout`,
owns the jobs it runs.

## Cost

To know which call blocks the event loop, the process tracks the asynchronous context of each call using
`node:async_hooks`, which slows down code that awaits many promises. Set `UnresponsiveTimeout` when you cancel calls
that can block the process, such as calls that run code you do not control, or a long computation that may need to be
abandoned.

## Canceled calls and the other options

- A canceled call no longer counts for `MaxConcurrentCalls`, even when its JavaScript code is still running.
- `NodeJsHostPool` and `WorkerThreads` send each call to the process or the worker thread with the fewest calls in
  progress, counting the canceled calls whose JavaScript code has not completed.
- Disposing the host kills a process whose event loop is blocked.
