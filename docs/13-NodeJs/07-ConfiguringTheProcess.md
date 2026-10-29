---
title: Configuring the Node.js process
description: Locate Node.js, set the working directory, the arguments and the environment variables, capture the output, and understand the lifetime of the process.
---

## Set the options

`NodeJsHostOptions` configures the Node.js process when the host starts:

<!-- ref:../../tests/Meziantou.Framework.NodeJs.Tests/NodeJsHostTests.cs;region=NodeJs_ProcessOptions;format=md-fenced-code-block;dedent=true -->
```csharp
var output = new ConcurrentQueue<string>();
var options = new NodeJsHostOptions
{
    WorkingDirectory = Path.Combine(AppContext.BaseDirectory, "scripts"),
    StartupTimeout = TimeSpan.FromMinutes(1),

    // Each line written by console.log or console.error
    StandardOutputReceived = output.Enqueue,
    StandardErrorReceived = output.Enqueue,
};
options.NodeArguments.Add("--max-old-space-size=4096");
options.EnvironmentVariables["APP_ENVIRONMENT"] = "Development";

await using var node = await NodeJsHost.StartAsync(options);
var environment = await node.EvaluateAsync<string>("console.log('started'); return process.env.APP_ENVIRONMENT;");
Assert.Equal("Development", environment);
```
<!-- endref -->

| Option | Default | Use |
| --- | --- | --- |
| `NodeExecutablePath` | `node`, searched in the `PATH` | The path of the `node` executable |
| `WorkingDirectory` | The current directory | The folder from which relative paths and npm packages are resolved |
| `NodeArguments` | None | Additional arguments of `node`, such as `--max-old-space-size=4096` |
| `EnvironmentVariables` | The variables of your application | Variables to set, or to remove with a `null` value |
| `StartupTimeout` | 30 seconds | The maximum time to wait for the process to start, or `Timeout.InfiniteTimeSpan` |
| `StandardOutputReceived`, `StandardErrorReceived` | Discarded | Callbacks that receive each line of the output |
| `MaxConcurrentCalls` | Not limited | See [Running calls in parallel](10-ParallelCalls.md#limit-the-calls-in-progress) |
| `WorkerThreads` | None | See [Running calls in parallel](10-ParallelCalls.md#worker-threads) |
| `UnresponsiveTimeout` | Never killed | See [Cancellation and timeouts](09-CancellationAndTimeouts.md) |

The options are copied when a host or a pool starts, so changing them afterwards has no effect on it. A zero or negative
`StartupTimeout`, `UnresponsiveTimeout`, `MaxConcurrentCalls` or `WorkerThreads` throws an
`ArgumentOutOfRangeException`.

## Locate Node.js

By default, the `node` executable is searched in the `PATH`. When it is not found, `StartAsync` throws a
`NodeJsException` that asks you to install Node.js or to set `NodeExecutablePath`. Set `NodeExecutablePath` when:

- Node.js is installed in a folder that is not in the `PATH` of your application, which can happen with services and
  version managers.
- Several versions are installed, and your code needs a specific one.
- Your application ships its own copy of Node.js.

Node.js 16.9 or later is required: older versions fail to start or fail some calls. `NodeVersion` gives the version of
the process that runs (`process.version`, such as `v24.15.0`), and `ProcessId` its identifier.

## Working directory

Relative module paths (`./slug.mjs`) and npm packages are resolved from the working directory. Use a folder that you
deploy with your application, such as a folder copied to the output directory, rather than the current directory, which
depends on how the application is started. See [Calling modules](04-CallingModules.md).

## Output

The standard output and the standard error of the process, such as the output of `console.log` and `console.error`, are
captured: each line is passed to `StandardOutputReceived` or `StandardErrorReceived`. Without a callback, the line is
discarded, so it does not appear in the console of your application. The exceptions thrown by the callbacks are
ignored.

The last lines of the standard error are included in the message of the exception thrown when the process exits, so the
reason of a crash is visible even without a callback.

The standard input of the process is used to start it, and is then closed: JavaScript code must not read from
`process.stdin`.

## Lifetime of the process

- The process exits when the host is disposed, or when the connection with the .NET process is closed, such as when
  your application exits.
- A process whose event loop is blocked cannot notice that the connection is closed. Disposing the host kills it. When
  your application exits without disposing the host, the process keeps running, except on .NET 11 on Linux and Windows,
  where the operating system kills it.
- Processes started by the JavaScript code, such as with `node:child_process`, are not stopped when the host is
  disposed, unless the Node.js process must be killed, in which case its whole process tree is killed.
- On .NET 11, the process does not inherit the other handles of your application (files, sockets, pipes…). On .NET 10,
  it inherits the inheritable ones, as any process started using `Process.Start`.

A host is meant to live as long as the work that uses it: start it once, for instance as a singleton of your
application, and dispose it when the application stops.
