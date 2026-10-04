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

In results, `BigInt` values are exact JSON numbers (Node.js 21 or later): read them with `GetInt64()`, deserialize them as `Int128` or `decimal`, or use `BigInteger.Parse(element.GetRawText())`.

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

// Get the JSON representation of the referenced value
JsonElement value = await client.GetValueAsync();
````

The value is kept until the reference is disposed or the process exits. A reference can only be used with the process that created it.

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

A reference returned by the pool is bound to the process that created it. Calls of the pool whose arguments contain a reference run on that process.

## Limitations

- Arguments and results are serialized as JSON. Use `JSValue` to pass values that JSON cannot represent, and `JSReference` to keep values in the Node.js process. In results, `undefined` becomes `null`.
- `JSValue` and `JSReference` can only be used in the arguments of a call. A `JsonNode` created from them cannot be serialized or cloned, and they cannot be members of objects serialized using reflection.
- Cancelling a call only stops waiting for the result; the JavaScript code keeps running.
- JavaScript code cannot call back into .NET.
- A synchronous infinite loop blocks all the other calls. Disposing the host kills the process.
