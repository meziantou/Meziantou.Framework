# Meziantou.Framework.NodeJs

Run JavaScript and call Node.js modules (local files or npm packages) from .NET.

`NodeJsHost` starts the `node` executable and communicates with it using JSON messages over a private local socket (a named pipe on Windows, a Unix domain socket elsewhere). Node.js 16.9 or later must be installed; older versions fail to start or fail some calls. Returning `BigInt` values requires Node.js 21 or later (see [Results](#results)).

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

// Pass values as arguments, available in the args array, instead of inserting them in the code
JsonElement joined = await node.EvaluateAsync("return path.join(...args);", ["a", userInput]);

// Call an export of a module (ESM or CommonJS): npm package, relative path, absolute path, or URL
JsonElement html = await node.InvokeAsync("marked", "parse", ["# Hello"]);
JsonElement result = await node.InvokeAsync("./scripts/math.mjs", "add", [1, 2]);

// Call the default export
JsonElement greeting = await node.InvokeAsync("./greet.mjs", exportName: null, ["World"]);
````

If the export is a function, it is called with the arguments and its result is awaited when it is a promise. Otherwise, the value of the export is returned.

### Modules

Modules are loaded using `import()`, so module specifiers follow the rules of ES modules, even for CommonJS modules:

- A relative path must start with `./` or `../`, use `/` as separator, and include the file extension (e.g. `./scripts/math.mjs`). It is resolved from `WorkingDirectory`. A path such as `scripts/math.mjs` is the name of a package (`scripts`), and `./scripts/math` is not found.
- An absolute path, or a `file:` or `node:` URL.
- An npm package name, resolved from the `node_modules` folders of the working directory and of its parents.

The `require` function available to evaluated code follows the rules of CommonJS, so the extension is optional.

A module is loaded once and kept for the lifetime of the process (or of the worker thread, see [Worker threads](#worker-threads)): changes to its file have no effect until a new host is started, and a module whose top-level code throws keeps failing.

### Typed results

````c#
// Trimming and Native AOT compatible
Person? person = await node.InvokeAsync("./people.mjs", "get", [42], MyJsonContext.Default.Person);

// Reflection-based serialization of arguments and results
Person? person = await node.InvokeAsync<Person>("./people.mjs", "get", [42]);
````

Without `JsonSerializerOptions`, the reflection-based overloads use the web defaults (`JsonSerializerDefaults.Web`), as JavaScript code uses camelCase names: properties are written in camelCase, and read case-insensitively. Numbers can also be read from strings, and `NaN` and infinities can be read as numbers. Options that you provide are used as is.

A result that cannot be deserialized to the requested type throws the exception of `System.Text.Json`, usually a `JsonException`.

### Ignoring the result

`InvokeVoidAsync` and `EvaluateVoidAsync` wait for the call to complete but do not serialize its result, so the result does not need to be serializable.

````c#
await node.InvokeVoidAsync("./cache.mjs", "warmUp");
await node.EvaluateVoidAsync("globalThis.server = require('node:http').createServer();");
````

### JavaScript values

Arguments are serialized as JSON. Use `JSValue` for values that JSON cannot represent; it also covers `null`, strings and booleans, so any argument can be expressed with it. It converts implicitly to `JsonNode`, so it can be an argument or be nested in a `JsonObject` or `JsonArray` argument.

| .NET | JavaScript |
| --- | --- |
| `JSValue.Undefined` | `undefined` |
| `JSValue.Null` | `null` |
| `JSValue.String(string)` | string |
| `JSValue.Boolean(bool)` | boolean |
| `JSValue.BigInt(BigInteger)` | `BigInt`. `long`, `ulong`, `Int128`, and `UInt128` convert implicitly to `BigInteger`. |
| `JSValue.Number(...)` | `Number`, from `double`, `float`, `Half`, `decimal`, and all integer types. `NaN`, infinities, and `-0` are preserved. Values that are not exactly representable are rounded to the nearest double, as `Number` would. |
| `JSValue.Date(DateTimeOffset)`, `JSValue.Date(DateTime)` | `Date`, truncated to the millisecond. A local or unspecified `DateTime` is a local time. |
| `JSValue.Uint8Array(ReadOnlyMemory<byte>)` | `Uint8Array` |

````c#
await node.InvokeAsync("./module.mjs", "run", [JSValue.Undefined, JSValue.BigInt(long.MaxValue), new JsonObject { ["date"] = JSValue.Date(DateTimeOffset.UtcNow) }]);
````

### Results

Results are serialized as JSON. Values that JSON does not support, or supports with a loss of information, are converted:

| JavaScript | JSON | .NET |
| --- | --- | --- |
| `BigInt` | exact number (Node.js 21 or later) | `GetInt64()`, `Int128`, `decimal`, or `BigInteger.Parse(element.GetRawText())` |
| `NaN`, `Infinity`, `-Infinity` | `"NaN"`, `"Infinity"`, `"-Infinity"` | `double` or `float` with `JsonNumberHandling.AllowNamedFloatingPointLiterals` (the default when no `JsonSerializerOptions` are provided) |
| `-0` | `-0` (Node.js 21 or later) | `double` |
| `Uint8Array`, `Buffer`, `Uint8ClampedArray`, `DataView`, `ArrayBuffer` | base64 string | `byte[]`, or `GetBytesFromBase64()` |
| Other typed arrays (`Float64Array`, `Int32Array`...) | array of numbers | `double[]`, `int[]`... |
| `Map` | object, keys converted to strings | `Dictionary<string, T>`, `Dictionary<int, T>`... Keys must be strings, numbers, `BigInt`s, or booleans. |
| `Set` | array | `HashSet<T>`, `List<T>`... |
| `Date` | ISO 8601 string | `DateTimeOffset`, `DateTime` |
| `undefined` | `null` | |
| String or key containing a lone surrogate (e.g. a string cut in the middle of an emoji) | string, the lone surrogate replaced by `U+FFFD` | `string` |

A function or a `Symbol` returned by the call, or a value whose `toJSON` method returns `undefined`, cannot be serialized, so the call fails. Use a call that returns a `JSReference` to keep it in the Node.js process, or a void call to ignore it. Functions and `Symbol`s nested in the result are omitted, as `JSON.stringify` does.

The depth of results is only limited by the stack of the Node.js process, as `JSON.stringify` is recursive.

When deserializing with a `JsonTypeInfo` or with your own `JsonSerializerOptions`, set `NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals` to read `NaN` and infinities as numbers.

Before Node.js 21, a result that contains a `BigInt` cannot be serialized, so the call fails, and `-0` is returned as `0`.

### References

A value that cannot be serialized, or that must be reused across calls (a class instance, a `Map`, a database client, a function...), can be kept in the Node.js process. `InvokeReferenceAsync` and `EvaluateReferenceAsync` return a `JSReference` instead of the JSON representation of the result.

````c#
await using var client = await node.InvokeReferenceAsync("./database.mjs", "connect", ["connection-string"]);

// Call a method of the referenced value, with the value as "this"
JsonElement rows = await client.InvokeAsync("query", ["SELECT 1"]);

// Pass the referenced value to a function, directly or nested in a JsonObject or JsonArray argument
await node.InvokeVoidAsync("./database.mjs", "seed", [client]);

// Call a referenced function
await using var add = await node.EvaluateReferenceAsync("return (a, b) => a + b;");
JsonElement sum = await add.InvokeAsync(methodName: null, [1, 2]);

// Create an instance of an exported class (new), or of a class that is a member of a referenced value
await using var parser = await node.CreateInstanceAsync("./parser.mjs", "Parser", [new JsonObject { ["strict"] = true }]);

// Get the JSON representation of the referenced value
JsonElement value = await client.GetValueAsync();
````

The value is kept until the reference is disposed or the process exits. A reference can only be used with the process that created it: once the process exits, calls that use it fail with a `NodeJsException` whose `ExitCode` is set. With [worker threads](#worker-threads), the value is kept by the worker thread that created it, and is lost when this worker thread exits.

### Errors

JavaScript errors are thrown as `NodeJsException`, with `JavaScriptErrorName` (e.g. `TypeError`), `JavaScriptStack`, and `JavaScriptErrorCode` (the `code` property of the error, e.g. `ENOENT`). The `cause` of the error is the `InnerException`. A thrown value that is not an `Error` is described by its JSON representation when it is an object. If the Node.js process exits, pending and future calls throw a `NodeJsException` whose `ExitCode` is set, including calls made through its references and calls made once `NodeJsHostPool` has replaced the process. When the process is killed because of `UnresponsiveTimeout`, calls throw a `NodeJsException` whose `ExitCode` is `null`. With [worker threads](#worker-threads), when a worker thread exits or is terminated, its calls and the calls made through its references throw a `NodeJsException` whose `ExitCode` is `null`, as the process keeps running. Calls made once you dispose the host throw an `ObjectDisposedException`.

Errors that are not related to a call, such as an exception thrown by a timer callback or a promise rejection that is never handled, are written to the standard error of the process and do not stop it.

### Process lifetime

The Node.js process exits when the host is disposed, or when the connection with the .NET process is closed (e.g. when the .NET process exits). A process whose event loop is blocked cannot notice that the connection is closed: disposing the host kills it, but when the .NET process exits without disposing the host, the process keeps running, except on .NET 11 on Linux and Windows, where the operating system kills it.

Processes started by the JavaScript code (e.g. using `node:child_process`) are not stopped when the host is disposed, unless the Node.js process must be killed, in which case its whole process tree is killed.

### Standard streams

The standard input of the process is used to send the bootstrap script, and is then closed, so JavaScript code must not read from `process.stdin`. The standard output and error (e.g. `console.log`) are captured: each line is passed to `StandardOutputReceived` or `StandardErrorReceived`, and is discarded when the callback is not set, so it does not appear in the console of the .NET process. The last lines of the standard error are included in the message of the exception thrown when the process exits.

On .NET 11, the process does not inherit other handles of the .NET process (files, sockets, pipes...). On .NET 10, it inherits the inheritable ones, as any process started using `Process.Start`.

### Options

- `NodeExecutablePath`: path of `node`. By default, `node` is searched in the `PATH`.
- `NodeArguments`: additional arguments, such as `--max-old-space-size=4096`.
- `EnvironmentVariables`: environment variables of the Node.js process.
- `StandardOutputReceived` / `StandardErrorReceived`: callbacks for the output of the process (e.g. `console.log`). Exceptions thrown by the callbacks are ignored.
- `StartupTimeout`: maximum time to wait for the process to start.
- `MaxConcurrentCalls`: maximum number of calls running at the same time in a process, including all its worker threads. Other calls wait, without keeping a serialized copy of their arguments. A canceled call no longer counts, even if its JavaScript code is still running. By default, the number of calls is not limited.
- `UnresponsiveTimeout`: maximum time a canceled call can block the event loop (e.g. with an infinite loop, or with promises that never let the event loop run other callbacks). The process is then killed and the calls in progress fail. `NodeJsHostPool` replaces it. Calls that are not canceled are never stopped, even when they keep the event loop busy. To know which call blocks the event loop, the process tracks the asynchronous context of each call using `node:async_hooks`, which slows down code that awaits many promises. By default, the process is never killed. See [Cancellation](#cancellation) for the code that belongs to a call. With `WorkerThreads`, only the blocked worker thread is terminated and restarted.
- `WorkerThreads`: number of worker threads that run the calls, so CPU-bound calls run in parallel in a single process. By default, calls run on the main thread. See [Worker threads](#worker-threads).

Options are copied when a host or a pool is started, so changing them afterwards has no effect.

## Security

The JavaScript code runs with the permissions of the .NET process, and the Node.js process inherits its environment variables. Only run code you trust.

- The module specifier, the export name, and the member name of a `JSReference` call select the code to run (e.g. `InvokeAsync("node:child_process", "execSync", ["..."])`). Never build them from untrusted input.
- Never insert untrusted values in the code passed to `EvaluateAsync`. Pass them as arguments, using the `args` array.
- `InvokeAsync` only accepts `file:` and `node:` URLs (a `data:` URL contains the code to run), inherited `constructor` and `__proto__` members cannot be accessed, and calling `Function` or `eval` directly, or creating an instance of `Function` using `CreateInstanceAsync`, is rejected. These checks only close the most direct ways of running code; they are not a sandbox. Built-in modules run code by design (e.g. `InvokeAsync("node:vm", "runInThisContext", [code])`), so a module specifier, an export name, or a member name built from untrusted input can still run arbitrary code.
- The socket used to communicate with the Node.js process is only accessible by the current user, and the process must prove it was started by the host.

## Parallel calls

`NodeJsHost` is thread-safe, and concurrent calls run concurrently in the Node.js process. Asynchronous code (I/O, timers, promises) overlaps, but synchronous code runs one call at a time on the single event loop.

To run CPU-bound code in parallel, set `WorkerThreads` (see [Worker threads](#worker-threads)), or use `NodeJsHostPool`, which starts several Node.js processes and sends each call to the process with the fewest calls in progress, including canceled calls whose JavaScript code has not completed. A process that exits is replaced automatically.

A canceled call keeps running, and synchronous code (e.g. an infinite loop) blocks its process. Set `UnresponsiveTimeout` so such a process is killed and replaced. Only the process blocked by a canceled call is killed: a process busy with calls that are not canceled is not (see [Cancellation](#cancellation)).

Set `MaxConcurrentCalls` so calls wait in the pool and run on the first process that becomes available. Otherwise, all calls are sent immediately, and a call can wait for a long call sent to the same process while other processes are idle. `1` is a good value for CPU-bound code. Calls made on the pool by a `RunAsync` callback do not wait in the pool, as the callback already counts as a call.

````c#
await using var pool = await NodeJsHostPool.StartAsync(Environment.ProcessorCount, new NodeJsHostOptions { WorkingDirectory = "path/to/js/project", MaxConcurrentCalls = 1 });
var results = await Task.WhenAll(documents.Select(document => pool.InvokeAsync("./render.mjs", "render", [document])));

// Run several calls on the same process
await pool.RunAsync(async host =>
{
    await host.EvaluateAsync("globalThis.cache = new Map();");
    await host.InvokeAsync("./render.mjs", "warmUp");
});
````

Processes do not share state: values stored on `globalThis` are only visible to calls running on the same process.

A reference returned by the pool is bound to the process that created it. Calls of the pool whose arguments contain a reference run on that process, without waiting in the pool: only the `MaxConcurrentCalls` limit of that process applies. Once the process exits, they fail, even after the pool replaces the process.

### Worker threads

Set `WorkerThreads` to run calls on several [worker threads](https://nodejs.org/api/worker_threads.html) of a single Node.js process. Each worker thread has its own connection with the host, so calls do not go through the main thread. Each call is sent to the worker thread with the fewest calls in progress, including canceled calls whose JavaScript code has not completed. Set `MaxConcurrentCalls` to the number of worker threads for CPU-bound code, so calls wait in the host and run on the first worker thread that becomes available.

````c#
await using var node = await NodeJsHost.StartAsync(new NodeJsHostOptions { WorkerThreads = Environment.ProcessorCount, MaxConcurrentCalls = Environment.ProcessorCount });
var results = await Task.WhenAll(documents.Select(document => node.InvokeAsync("./render.mjs", "render", [document])));
````

Compared to `NodeJsHostPool`, worker threads use less memory and start faster, and a worker thread blocked by a canceled call is terminated and restarted without restarting the process (see `UnresponsiveTimeout`). However, a crash of the process (e.g. a native crash, or running out of memory) fails the calls of all the worker threads. Both can be combined: each process of a pool can run worker threads.

Worker threads do not share state:

- Each worker thread has its own `globalThis` and loads its own modules, so consecutive calls may run on different worker threads and not see the same values.
- A reference is kept by the worker thread that created it. Calls whose arguments contain a reference, and calls made through the reference, run on this worker thread. A call cannot use references kept by different worker threads.

A worker thread that exits is restarted, and its state is lost: its calls in progress fail, and its references can no longer be used. Some APIs behave differently in a worker thread:

- `process.exit()` only stops the worker thread.
- `process.chdir()` is not supported, and `process.env` is a copy of the environment of the process.
- Native addons must support worker threads (context-aware addons).
- The output written asynchronously (e.g. `console.log`) by a worker thread that is terminated may be lost.

## Cancellation

Canceling a call only stops waiting for its result: the JavaScript code keeps running, and it may block the event loop of the process, and so the other calls. With `UnresponsiveTimeout`, the process is killed when the code of a canceled call blocks the event loop for longer than the timeout, synchronously (e.g. an infinite loop) or with promises that never let the event loop run other callbacks. With `WorkerThreads`, only the worker thread whose event loop is blocked is terminated and restarted, and the other worker threads keep running their calls.

The code of a call is its own code, and the callbacks of the promises, timeouts, immediates, and I/O operations (e.g. reading a file) it starts, directly or through other callbacks. To never kill a call that is not canceled, the code run by long-lived objects belongs to no call, as these objects can run code for several calls (e.g. a connection pool created by the first call that needs it):

- Intervals (`setInterval`), sockets, servers, workers, and event listeners registered on objects the call did not create (e.g. `process.on(...)`).
- The code that loads a module imported by `InvokeAsync` or `require`, including its top-level code and the timers and connections it creates. Modules loaded using `import()` in the code of a call belong to the call.

Such code is never stopped. A call that runs a loop processing jobs for other calls (e.g. a recursive `setTimeout`) owns the jobs it runs.

## Limitations

- Arguments and results are serialized as JSON. Use `JSValue` to pass values that JSON cannot represent, and `JSReference` to keep values in the Node.js process. See [Results](#results) for the conversion of results.
- A message (arguments or result) cannot exceed the maximum length of a JavaScript string (about 512 million UTF-16 characters). A larger message fails its call.
- `JSValue` and `JSReference` can only be used in the arguments of a call. A `JsonNode` created from them cannot be serialized or cloned, and they cannot be members of objects serialized using reflection.
- Cancelling a call only stops waiting for the result; the JavaScript code keeps running. Set `UnresponsiveTimeout` to kill the process when the code blocks its event loop (see [Cancellation](#cancellation)).
- JavaScript code cannot call back into .NET.
- A synchronous infinite loop blocks all the other calls, or all the other calls of its worker thread with `WorkerThreads`. Disposing the host kills the process.
