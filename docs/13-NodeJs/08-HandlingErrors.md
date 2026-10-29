---
title: Handling errors
description: Handle JavaScript errors, the exit of the Node.js process, and the other exceptions a call can throw.
---

## JavaScript errors

An error thrown by the JavaScript code, or a rejected promise, is thrown as a `NodeJsException`:

<!-- ref:../../tests/Meziantou.Framework.NodeJs.Tests/NodeJsHostTests.cs;region=NodeJs_JavaScriptErrors;format=md-fenced-code-block;dedent=true -->
```csharp
await using var node = await NodeJsHost.StartAsync();

var exception = await Assert.ThrowsAsync<NodeJsException>(() => node.InvokeAsync("node:fs/promises", "readFile", ["missing-file.txt"]));
Assert.Equal("Error", exception.JavaScriptErrorName);
Assert.Equal("ENOENT", exception.JavaScriptErrorCode);
Assert.NotNull(exception.JavaScriptStack);
Assert.Null(exception.ExitCode); // The process is still running

// The cause of the error is the inner exception
var withCause = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("throw new Error('Cannot render the page', { cause: new TypeError('The title is not a string') });"));
Assert.Contains("Cannot render the page", withCause.Message);
var cause = Assert.IsType<NodeJsException>(withCause.InnerException);
Assert.Equal("TypeError", cause.JavaScriptErrorName);
```
<!-- endref -->

| Property | Value |
| --- | --- |
| `Message` | The message of the error |
| `JavaScriptErrorName` | The name of the error, such as `TypeError`, or `null` when the error does not come from JavaScript code |
| `JavaScriptErrorCode` | The `code` property of the error, converted to a string, such as `ENOENT` for the errors of `node:fs` |
| `JavaScriptStack` | The JavaScript stack trace, if available |
| `InnerException` | The `cause` of the error, if any |
| `ExitCode` | `null`: the process is still running |

A thrown value that is not an `Error` is described by its JSON representation when it is an object. The host keeps
working after a JavaScript error: the next calls run as usual.

## The process exits

When the Node.js process exits, its calls in progress and all the later calls throw a `NodeJsException` whose
`ExitCode` is the exit code of the process:

<!-- ref:../../tests/Meziantou.Framework.NodeJs.Tests/NodeJsHostTests.cs;region=NodeJs_ProcessExit;format=md-fenced-code-block;dedent=true -->
```csharp
await using var node = await NodeJsHost.StartAsync();

var exception = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateVoidAsync("process.exit(3);"));
Assert.Equal(3, exception.ExitCode);

// The host cannot run code anymore
var next = await Assert.ThrowsAsync<NodeJsException>(() => node.EvaluateAsync("return 1;"));
Assert.Equal(3, next.ExitCode);
```
<!-- endref -->

- This includes the calls made through the references of the process. The values they reference are lost.
- The message includes the last lines written to the standard error, which usually tell why the process exited, such
  as running out of memory.
- A host whose process exited cannot be restarted: start a new host. `NodeJsHostPool` replaces a process that exits; see
  [Running calls in parallel](10-ParallelCalls.md#a-pool-of-processes).

The process is not stopped by an error thrown outside of a call, such as by a timer callback, or by a promise rejection
that is never handled: the error is written to the standard error, which you can capture with `StandardErrorReceived`
(see [Configuring the Node.js process](07-ConfiguringTheProcess.md#output)).

## The process or a worker thread is killed

`ExitCode` is `null` when the error does not come from an exit of the process:

- The process was killed because a canceled call blocked its event loop for longer than `UnresponsiveTimeout`. See
  [Cancellation and timeouts](09-CancellationAndTimeouts.md).
- With [worker threads](10-ParallelCalls.md#worker-threads), a worker thread exited or was terminated: its calls, and
  the calls made through its references, fail, while the process keeps running.

## Other exceptions

| Exception | When |
| --- | --- |
| `NodeJsException` | `StartAsync` cannot find or start `node`, or the process does not start within `StartupTimeout` |
| `ArgumentOutOfRangeException` | `StartAsync` receives an option that is zero or negative |
| `JsonException` (or another exception of `System.Text.Json`) | The result cannot be deserialized to the requested type |
| `OperationCanceledException` | The cancellation token of the call is canceled. See [Cancellation and timeouts](09-CancellationAndTimeouts.md). |
| `ObjectDisposedException` | The host is disposed: the calls in progress and the later calls fail |
