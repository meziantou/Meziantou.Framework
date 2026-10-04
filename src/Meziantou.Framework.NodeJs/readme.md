# Meziantou.Framework.NodeJs

Run JavaScript and call Node.js modules (local files or npm packages) from .NET.

`NodeJsHost` starts the `node` executable and communicates with it using JSON messages over a private local socket (a named pipe on Windows, a Unix domain socket elsewhere). Node.js must be installed; any version supporting ES modules works.

## Usage

````c#
await using var node = await NodeJsHost.StartAsync(new NodeJsHostOptions
{
    // Module specifiers (relative paths and npm packages) are resolved from this directory
    WorkingDirectory = "path/to/js/project",
});

// Evaluate code as the body of an async function
JsonElement sum = await node.EvaluateAsync("return 1 + 2;");

// Values stored on globalThis persist across calls, and require is available
await node.EvaluateAsync("globalThis.path = require('node:path');");

// Call an export of a module (ESM or CommonJS): npm package, relative path, absolute path, or URL
JsonElement html = await node.InvokeAsync("marked", "parse", ["# Hello"]);
JsonElement result = await node.InvokeAsync("./scripts/math.mjs", "add", [1, 2]);

// Call the default export
JsonElement greeting = await node.InvokeAsync("./greet.mjs", exportName: null, ["World"]);
````

If the export is a function, it is called with the arguments and its result is awaited when it is a promise. Otherwise, the value of the export is returned.

### Typed results

````c#
// Trimming and Native AOT compatible
Person? person = await node.InvokeAsync("./people.mjs", "get", [42], MyJsonContext.Default.Person);

// Reflection-based serialization of arguments and results
Person? person = await node.InvokeAsync<Person>("./people.mjs", "get", [42]);
````

### Errors

JavaScript errors are thrown as `NodeJsException`, with `JavaScriptErrorName` (e.g. `TypeError`) and `JavaScriptStack`. If the Node.js process exits, pending and future calls throw a `NodeJsException` whose `ExitCode` is set.

Errors that are not related to a call, such as an exception thrown by a timer callback or a promise rejection that is never handled, are written to the standard error of the process and do not stop it.

### Process lifetime

The Node.js process exits when the host is disposed, or when the connection with the .NET process is closed (e.g. when the .NET process exits). On .NET 11, on Linux and Windows, the operating system also kills it when the .NET process exits, even if its event loop is blocked. The process only inherits its standard input, output and error from the .NET process.

### Options

- `NodeExecutablePath`: path of `node`. By default, `node` is searched in the `PATH`.
- `NodeArguments`: additional arguments, such as `--max-old-space-size=4096`.
- `EnvironmentVariables`: environment variables of the Node.js process.
- `StandardOutputReceived` / `StandardErrorReceived`: callbacks for the output of the process (e.g. `console.log`).
- `StartupTimeout`: maximum time to wait for the process to start.

## Parallel calls

`NodeJsHost` is thread-safe, and concurrent calls run concurrently in the Node.js process. Asynchronous code (I/O, timers, promises) overlaps, but synchronous code runs one call at a time on the single event loop.

To run CPU-bound code in parallel, use `NodeJsHostPool`. It starts several Node.js processes and sends each call to the process with the fewest calls in progress. A process that exits is replaced automatically.

````c#
await using var pool = await NodeJsHostPool.StartAsync(Environment.ProcessorCount, new NodeJsHostOptions { WorkingDirectory = "path/to/js/project" });
var results = await Task.WhenAll(documents.Select(document => pool.InvokeAsync("./render.mjs", "render", [document])));

// Run several calls on the same process
await pool.RunAsync(async host =>
{
    await host.EvaluateAsync("globalThis.cache = new Map();");
    await host.InvokeAsync("./render.mjs", "warmUp");
});
````

Processes do not share state: values stored on `globalThis` are only visible to calls running on the same process.

## Limitations

- Arguments and results are serialized as JSON. Functions, symbols, and class instances cannot cross the boundary; `undefined` becomes `null` and `BigInt` values cannot be serialized.
- Cancelling a call only stops waiting for the result; the JavaScript code keeps running.
- JavaScript code cannot call back into .NET.
- A synchronous infinite loop blocks all the other calls. Disposing the host kills the process.
