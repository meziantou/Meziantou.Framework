---
title: Cancellation and killing
description: Cancel a running process with a cancellation token, kill it explicitly, or send it a signal.
---

## Cancel with a token

<!-- ref:../../samples/Meziantou.Framework.ProcessWrapper.Samples/ReadmeExamples.cs;region=ProcessWrapper_Cancellation;format=md-fenced-code-block;dedent=true -->
```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
await ProcessWrapper.Create("long-running-process")
    .ExecuteAsync(cts.Token);  // throws OperationCanceledException if cancelled
```
<!-- endref -->

When the token is canceled, the process and all its descendants are killed, and awaiting the instance throws an
`OperationCanceledException`. The validation does not run for a canceled process. When the token is already canceled,
[ExecuteAsync](cref:Meziantou.Framework.ProcessWrapper.ExecuteAsync) throws without starting the process.

## Kill a process

[ExecuteAsync](cref:Meziantou.Framework.ProcessWrapper.ExecuteAsync) returns as soon as the process is started, so you can kill it while it runs:

<!-- ref:../../samples/Meziantou.Framework.ProcessWrapper.Samples/ReadmeExamples.cs;region=ProcessWrapper_Kill;format=md-fenced-code-block;dedent=true -->
```csharp
var process = ProcessWrapper.Create("long-running-process")
    .WithValidation(ProcessValidationMode.None)
    .ExecuteAsync();

process.Kill(); // or process.Kill(entireProcessTree: false)
await process;
```
<!-- endref -->

- [Kill()](cref:Meziantou.Framework.ProcessInstance.Kill) kills the process and its descendants. `Kill(entireProcessTree: false)` kills only the process.
- Killing a process that already exited does nothing.
- A killed process usually exits with a non-zero exit code: disable the validation, as in the example, or catch the
  [ProcessExecutionException](cref:Meziantou.Framework.ProcessExecutionException).

## Send a signal

On .NET 11, `ProcessInstance.Signal(PosixSignal)` sends a signal to the process, such as `SIGTERM` to ask it to stop
gracefully. It returns the result of the operating system call, and throws an `InvalidOperationException` once the
process has exited and its handle was released.

## The process handle

[ProcessInstance.ProcessId](cref:Meziantou.Framework.ProcessInstance.ProcessId) and [StartDate](cref:Meziantou.Framework.ProcessInstance.StartDate) identify the running process. [UnsafeGetProcessHandle](cref:Meziantou.Framework.ProcessInstance.UnsafeGetProcessHandle) returns its
`SafeProcessHandle`, for native APIs, or `null` once the process has exited: the handle is released as soon as the process
exits, even if you never await the instance.
