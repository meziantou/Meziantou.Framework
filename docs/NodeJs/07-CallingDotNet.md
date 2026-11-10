---
title: Calling .NET from JavaScript
description: Pass .NET delegates to JavaScript code as async functions, to report progress, log, resolve resources from the host, or give a callback to a library.
---

## Pass a delegate

Wrap a delegate in a [JSCallback](cref:Meziantou.Framework.NodeJs.JSCallback) and pass it as an argument. The JavaScript code receives an async function:
calling it runs the delegate in your .NET process, and the returned promise resolves with the result of the delegate.

<!-- ref:../../tests/Meziantou.Framework.NodeJs.Tests/NodeJsHostTests.cs;region=JSCallback_Example;format=md-fenced-code-block;dedent=true -->
```csharp
await using var node = await NodeJsHost.StartAsync();

// A .NET delegate that JavaScript code calls as an async function, e.g. to report progress
var progress = new ConcurrentQueue<int>();
var onProgress = JSCallback.Create(args => progress.Enqueue(args[0].GetInt32()));

// An asynchronous delegate: the promise resolves with its result
var getPrice = JSCallback.Create(async (args, cancellationToken) =>
{
    await Task.Delay(10, cancellationToken);
    return args[0].GetString() is "apple" ? 3 : 5;
});

var total = await node.EvaluateAsync("""
    const [items, onProgress, getPrice] = args;
    let total = 0;
    for (let i = 0; i < items.length; i++) {
        total += await getPrice(items[i]);
        await onProgress(i + 1);
    }

    return total;
    """, [new JsonArray("apple", "pear"), onProgress, getPrice]);

Assert.Equal(8, total.GetInt32());
Assert.Equal(new[] { 1, 2 }, progress.ToArray());
```
<!-- endref -->

[JSCallback.Create](cref:Meziantou.Framework.NodeJs.JSCallback.Create) accepts four kinds of delegates:

| Delegate | The JavaScript function resolves with |
| --- | --- |
| `Action<IReadOnlyList<JsonElement>>` | `undefined` |
| `Func<IReadOnlyList<JsonElement>, JsonNode?>` | the result |
| `Func<IReadOnlyList<JsonElement>, CancellationToken, Task>` | `undefined`, once the task completes |
| `Func<IReadOnlyList<JsonElement>, CancellationToken, Task<JsonNode?>>` | the result, once the task completes |

A [JSCallback](cref:Meziantou.Framework.NodeJs.JSCallback) converts implicitly to `JsonNode`, so it can be an argument of any call, or be nested in a `JsonObject` or
a `JsonArray` argument. The reflection-based overloads, which take an array of objects, accept it too.

## Arguments and result

- The delegate receives the JSON representation of the arguments of the function. They are converted as the result of
  a call is (see [Arguments and results](05-ArgumentsAndResults.md#read-the-values-that-json-cannot-represent)): a
  `Map` becomes an object, a `Uint8Array` a base64 string, `undefined` and functions `null`... When the arguments cannot
  be converted, for instance a cyclic object, the promise is rejected with a `TypeError` and the delegate is not called.
- The result of the delegate is converted as the arguments of a call are, so it can contain a [JSValue](cref:Meziantou.Framework.NodeJs.JSValue), a
  [JSReference](cref:Meziantou.Framework.NodeJs.JSReference), or another [JSCallback](cref:Meziantou.Framework.NodeJs.JSCallback).
- When the delegate throws, or its result cannot be converted, the promise is rejected with an `Error` whose `name` is
  the name of the type of the exception (e.g. `InvalidOperationException`) and whose `message` is its message. When the
  JavaScript code does not catch it, the call fails with a [NodeJsException](cref:Meziantou.Framework.NodeJs.NodeJsException) whose [JavaScriptErrorName](cref:Meziantou.Framework.NodeJs.NodeJsException.JavaScriptErrorName) is
  this name.

## When the delegate runs

- The delegate runs on the thread pool. The JavaScript code can call the function several times concurrently, so the
  delegate must be thread-safe.
- The function stays valid once the call that passed it completes. The JavaScript code can keep it, for instance as an
  event handler, and call it during later calls.
- The `CancellationToken` of the asynchronous delegates is canceled when the host is disposed, when the process exits,
  or, with [WorkerThreads](cref:Meziantou.Framework.NodeJs.NodeJsHostOptions.WorkerThreads), when the worker thread that called the function exits.
- In a call, passing the same [JSCallback](cref:Meziantou.Framework.NodeJs.JSCallback) instance several times gives the same function. In different calls, it gives
  different functions.

## Lifetime

The host keeps the delegate until Node.js garbage collects the function, or until the process exits. A
[JSCallback](cref:Meziantou.Framework.NodeJs.JSCallback) is not bound to a process: the same instance can be passed to the calls of several hosts, or of a
[pool](11-ParallelCalls.md), and does not need to be disposed.
