---
title: Running calls in parallel
description: Run calls concurrently, and run CPU-bound JavaScript code in parallel on worker threads or on a pool of processes.
---

## Concurrent calls on one host

`NodeJsHost` is thread-safe, and concurrent calls run concurrently in the Node.js process. Asynchronous code (I/O,
timers, promises) overlaps, but synchronous code runs one call at a time on the single event loop of the process. One
host is enough for calls that mostly wait, such as calls that read files or send HTTP requests. For CPU-bound code, such
as rendering or parsing documents, use worker threads or a pool of processes.

| | One host | Worker threads | Pool of processes |
| --- | --- | --- | --- |
| Synchronous code | One call at a time | In parallel | In parallel |
| Memory and startup time | One process | Less than a pool | One process each |
| A crash of the process | Fails all the calls | Fails all the calls | Fails the calls of that process, which is replaced |
| A canceled call blocks the event loop | The process is killed | The worker thread is restarted | The process is killed and replaced |

The last row applies when `UnresponsiveTimeout` is set (see
[Cancellation and timeouts](09-CancellationAndTimeouts.md)). Both can be combined: each process of a pool can run worker
threads.

## Worker threads

Set `WorkerThreads` to run the calls on several [worker threads](https://nodejs.org/api/worker_threads.html) of a single
Node.js process:

<!-- ref:../../samples/Meziantou.Framework.NodeJs.Samples/ReadmeExamples.cs;region=NodeJs_WorkerThreads;format=md-fenced-code-block;dedent=true -->
```csharp
await using var node = await NodeJsHost.StartAsync(new NodeJsHostOptions { WorkerThreads = Environment.ProcessorCount, MaxConcurrentCalls = Environment.ProcessorCount });
var results = await Task.WhenAll(documents.Select(document => node.InvokeAsync("./render.mjs", "render", [document])));
```
<!-- endref -->

- Each worker thread has its own connection with the host, so the calls do not go through the main thread.
- Each call is sent to the worker thread with the fewest calls in progress. Set `MaxConcurrentCalls` to the number of
  worker threads for CPU-bound code, so the calls wait in the host and run on the first worker thread that becomes
  available.

Worker threads do not share state:

- Each worker thread has its own `globalThis` and loads its own modules, so consecutive calls may run on different
  worker threads and not see the same values.
- A reference is kept by the worker thread that created it. The calls whose arguments contain a reference, and the calls
  made through the reference, run on this worker thread. A call cannot use references kept by different worker threads.

A worker thread that exits is restarted, and its state is lost: its calls in progress fail, and its references can no
longer be used. Some APIs behave differently in a worker thread:

- `process.exit()` only stops the worker thread.
- `process.chdir()` is not supported, and `process.env` is a copy of the environment of the process.
- Native addons must support worker threads (context-aware addons).
- The output written asynchronously (such as by `console.log`) by a worker thread that is terminated may be lost.

## A pool of processes

`NodeJsHostPool` starts several Node.js processes, and sends each call to the process with the fewest calls in
progress:

<!-- ref:../../tests/Meziantou.Framework.NodeJs.Tests/NodeJsHostTests.cs;region=NodeJsHostPool_Example;format=md-fenced-code-block;dedent=true -->
```csharp
string[] documents = ["# Title", "Some *text*"];

await using var pool = await NodeJsHostPool.StartAsync(Environment.ProcessorCount);

// Each call runs on the host with the fewest calls in progress, so the calls run in parallel
var results = await Task.WhenAll(documents.Select(document => pool.EvaluateAsync("return args[0].length;", [document])));

Assert.Equal([7, 11], results.Select(result => result.GetInt32()));
```
<!-- endref -->

- The pool has the same calls as a host. `Environment.ProcessorCount` is a good size for CPU-bound code.
- A process that exits is replaced the next time a process is selected. With `UnresponsiveTimeout`, a process blocked
  by a canceled call is also killed and replaced.
- The options are copied when the pool starts, and are used for all the processes, including the ones that replace
  the processes that exit.

Processes do not share state: the values stored on `globalThis` are only visible to the calls that run on the same
process. Use `RunAsync` to run several calls on the same process:

<!-- ref:../../samples/Meziantou.Framework.NodeJs.Samples/ReadmeExamples.cs;region=NodeJs_Pool;format=md-fenced-code-block;dedent=true -->
```csharp
await using var pool = await NodeJsHostPool.StartAsync(Environment.ProcessorCount, new NodeJsHostOptions { WorkingDirectory = "path/to/js/project", MaxConcurrentCalls = 1 });
var results = await Task.WhenAll(documents.Select(document => pool.InvokeAsync("./render.mjs", "render", [document])));

// Run several calls on the same process
await pool.RunAsync(async host =>
{
    await host.EvaluateAsync("globalThis.cache = new Map();");
    await host.InvokeAsync("./render.mjs", "warmUp");
});
```
<!-- endref -->

- The host passed to the callback must not be used once the callback completes.
- A reference returned by the pool is bound to the process that created it. The calls of the pool whose arguments
  contain a reference run on that process, without waiting in the pool: only the `MaxConcurrentCalls` limit of that
  process applies. Once the process exits, they fail, even after the pool replaces the process.

## Limit the calls in progress

`MaxConcurrentCalls` limits the number of calls that run at the same time in a process, including all its worker
threads. The other calls wait, without keeping a serialized copy of their arguments. By default, the number of calls is
not limited.

- With a pool, the calls wait in the pool and run on the first process that becomes available. Otherwise, all the calls
  are sent immediately, and a call can wait for a long call sent to the same process while other processes are idle.
  `1` is a good value for CPU-bound code.
- With worker threads, use the number of worker threads for CPU-bound code, so a call runs on the first worker thread
  that becomes available.
- A canceled call no longer counts, even when its JavaScript code is still running.
- A `RunAsync` callback counts as one call of the pool, and each call it makes on the host counts as one call of the
  host. The calls it makes on the pool instead of the host do not wait in the pool, as the callback already counts as a
  call.
