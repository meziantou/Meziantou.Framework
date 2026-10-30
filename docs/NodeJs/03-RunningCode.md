---
title: Running JavaScript code
description: Evaluate JavaScript code with arguments, await promises, use require, and keep values across calls.
---

## Evaluate code

`EvaluateAsync` runs code and returns its result:

<!-- ref:../../tests/Meziantou.Framework.NodeJs.Tests/NodeJsHostTests.cs;region=NodeJs_Evaluate;format=md-fenced-code-block;dedent=true -->
```csharp
await using var node = await NodeJsHost.StartAsync();

// The code is the body of an async function: return a value, and await promises
var answer = await node.EvaluateAsync("await new Promise(resolve => setTimeout(resolve, 10)); return 6 * 7;");
Assert.Equal(42, answer.GetInt32());

// Pass values in the args array instead of inserting them in the code
var length = await node.EvaluateAsync<int>("return args[0].length;", ["hello"]);
Assert.Equal(5, length);

// require is available, and the values stored on globalThis persist across calls
await node.EvaluateVoidAsync("globalThis.path = require('node:path');");
var joined = await node.EvaluateAsync<string>("return path.posix.join(...args);", ["docs", "index.md"]);
Assert.Equal("docs/index.md", joined);

// undefined is returned as null
var nothing = await node.EvaluateAsync("const unused = 1;");
Assert.Equal(JsonValueKind.Null, nothing.ValueKind);
```
<!-- endref -->

- The code is the body of an async function: `return` gives the result, and `await` waits for a promise. When the code
  returns a promise, its value is the result.
- The arguments are in the `args` array. Pass the values your code works on as arguments instead of inserting them in
  the code: they need no escaping, and an untrusted value cannot change the code that runs (see
  [Security](11-Security.md)).
- `require` follows the rules of CommonJS: it loads built-in modules (`node:path`), npm packages and files, and the file
  extension is optional.
- Values stored on `globalThis` persist across calls of the same process, so a call can prepare a value that later calls
  use. With [worker threads](10-ParallelCalls.md#worker-threads) or a [pool](10-ParallelCalls.md#a-pool-of-processes),
  each thread or process has its own `globalThis`.
- A result of `undefined`, or no `return`, gives a JSON `null`.

## Get a typed result

The generic overloads deserialize the result, here `EvaluateAsync<int>` and `EvaluateAsync<string>`. The overloads
that take a `JsonTypeInfo<T>` work with trimming and Native AOT. See
[Arguments and results](05-ArgumentsAndResults.md).

## Ignore the result

`EvaluateVoidAsync` waits for the code to complete, and ignores its result: the result does not need to be
serializable. Use it for code that only has an effect, or that returns a value JSON cannot represent, such as a
function, a class instance or a server:

<!-- ref:../../samples/Meziantou.Framework.NodeJs.Samples/ReadmeExamples.cs;region=NodeJs_IgnoreResult;format=md-fenced-code-block;dedent=true -->
```csharp
await node.InvokeVoidAsync("./cache.mjs", "warmUp");
await node.EvaluateVoidAsync("globalThis.server = require('node:http').createServer();");
```
<!-- endref -->

To use such a value in later calls, keep a reference to it instead. See [Keeping values in Node.js](06-References.md).

## Evaluate code or call a module

Evaluating code suits a few lines. For more, put the code in a module and call its exports with `InvokeAsync`: the
module is loaded once, your editor and your linters check it, and you can test it with JavaScript tools. See
[Calling modules](04-CallingModules.md).
