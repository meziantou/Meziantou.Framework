---
title: Cancellation and timeouts
description: Cancel calls, stop their JavaScript code with an AbortSignal, and kill the process when a canceled call blocks it, such as with an infinite loop.
---

## Canceling a call

Every call takes a `CancellationToken`. Canceling it stops waiting for the result: the call throws an
`OperationCanceledException`, and the `AbortSignal` of the call, `nodeJsHost.signal`, is aborted in the Node.js
process. JavaScript code cannot be interrupted: it stops only when it checks the signal, or passes it to the APIs it
calls. Code that ignores the signal keeps running.

## Stop the JavaScript code of a canceled call

`nodeJsHost.signal` is the `AbortSignal` of the call whose code runs. Pass it to the APIs that accept one, such as
`fetch`, `setTimeout` of `node:timers/promises`, or `readFile` of `node:fs/promises`, so they stop as soon as the call is
canceled:

<!-- ref:../../tests/Meziantou.Framework.NodeJs.Tests/NodeJsHostTests.cs;region=NodeJs_CancellationSignal;format=md-fenced-code-block;dedent=true -->
```csharp
await using var node = await NodeJsHost.StartAsync();

using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(1));
await Assert.ThrowsAnyAsync<OperationCanceledException>(() => node.EvaluateVoidAsync("""
    // nodeJsHost.signal is aborted when the call is canceled, so the timer stops immediately
    const { setTimeout } = require("node:timers/promises");
    try {
        await setTimeout(60_000, undefined, { signal: nodeJsHost.signal });
    } catch (error) {
        globalThis.stopReason = error.name;
    }
    """, cancellationTokenSource.Token));
```
<!-- endref -->

In a long computation, check the signal regularly, for instance with `signal.throwIfAborted()`, and read it at the
start of the function, as it is only available in the code of a call:

```javascript
export async function render(document) {
    const signal = nodeJsHost.signal;
    for (const page of document.pages) {
        signal.throwIfAborted();
        await renderPage(page, { signal });
    }
}
```

- The signal is available in the code of the call, including the module functions called by
  [InvokeAsync](cref:Meziantou.Framework.NodeJs.NodeJsHost.InvokeAsync) and the methods called through a
  [JSReference](cref:Meziantou.Framework.NodeJs.JSReference), and in the callbacks of the promises, timers and I/O
  operations that the call starts.
- It is `undefined` in the top-level code of the modules loaded by [InvokeAsync](cref:Meziantou.Framework.NodeJs.NodeJsHost.InvokeAsync) and by the `require` function of
  evaluated code, as these modules are shared by all the calls. The modules that the code of a call loads itself, using
  `import()` or the `require` function of a CommonJS module, see the signal of that call while they load.
- Its `reason` is an `AbortError`. The error thrown by the code of a canceled call is ignored, as the host no longer
  waits for its result.
- The signal of a call that completes is never aborted.
- The abort listeners run on the thread of the call, once its event loop processes the cancellation: synchronous code
  that never awaits, such as an infinite loop, never sees the signal aborted. Use
  [UnresponsiveTimeout](cref:Meziantou.Framework.NodeJs.NodeJsHostOptions.UnresponsiveTimeout) to stop such code.
- `signal.reason` and `signal.throwIfAborted()` need Node.js 17.3 or later.

[StartAsync](cref:Meziantou.Framework.NodeJs.NodeJsHost.StartAsync) also takes a token, to cancel the startup. The token of [NodeJsHostPool.RunAsync](cref:Meziantou.Framework.NodeJs.NodeJsHostPool.RunAsync) stops waiting for a
process to be available.

## Kill a process blocked by a canceled call

Set [UnresponsiveTimeout](cref:Meziantou.Framework.NodeJs.NodeJsHostOptions.UnresponsiveTimeout) to kill the process when the code of a canceled call blocks its event loop for longer than
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

- The calls in progress fail with a [NodeJsException](cref:Meziantou.Framework.NodeJs.NodeJsException) whose [ExitCode](cref:Meziantou.Framework.NodeJs.NodeJsException.ExitCode) is `null`. The host cannot run code anymore:
  start a new one, or use [NodeJsHostPool](cref:Meziantou.Framework.NodeJs.NodeJsHostPool), which replaces the process.
- With [WorkerThreads](cref:Meziantou.Framework.NodeJs.NodeJsHostOptions.WorkerThreads), only the worker thread whose event loop is blocked is terminated and restarted. The calls in
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
stopped when it blocks the event loop for longer than [UnresponsiveTimeout](cref:Meziantou.Framework.NodeJs.NodeJsHostOptions.UnresponsiveTimeout).

The code of a call is its own code, and the callbacks of the promises, timeouts, immediates and I/O operations (such as
reading a file) that it starts, directly or through other callbacks. The code run by long-lived objects belongs to no
call, as these objects can run code for several calls, such as a connection pool created by the first call that needs
it:

- Intervals (`setInterval`), sockets, servers, workers, and the event listeners registered on objects the call did not
  create (such as `process.on(...)`).
- The code that loads a module imported by [InvokeAsync](cref:Meziantou.Framework.NodeJs.NodeJsHost.InvokeAsync) or by `require`, including its top-level code and the timers
  and connections it creates. The modules loaded using `import()` in the code of a call belong to the call.

Such code is never stopped. A call that runs a loop processing jobs for other calls, such as a recursive `setTimeout`,
owns the jobs it runs.

## Cost

To know which call blocks the event loop, the process tracks the asynchronous context of each call using
`node:async_hooks`, which slows down code that awaits many promises. Set [UnresponsiveTimeout](cref:Meziantou.Framework.NodeJs.NodeJsHostOptions.UnresponsiveTimeout) when you cancel calls
that can block the process, such as calls that run code you do not control, or a long computation that may need to be
abandoned.

## Canceled calls and the other options

- A canceled call no longer counts for [MaxConcurrentCalls](cref:Meziantou.Framework.NodeJs.NodeJsHostOptions.MaxConcurrentCalls), even when its JavaScript code is still running.
- [NodeJsHostPool](cref:Meziantou.Framework.NodeJs.NodeJsHostPool) and [WorkerThreads](cref:Meziantou.Framework.NodeJs.NodeJsHostOptions.WorkerThreads) send each call to the process or the worker thread with the fewest calls in
  progress, counting the canceled calls whose JavaScript code has not completed.
- Disposing the host kills a process whose event loop is blocked.
