---
title: Keeping values in Node.js
description: Keep a class instance, a function or any value that cannot be converted to JSON in the Node.js process, and use it in later calls.
---

## Keep a value

Some values cannot be converted to JSON, or must be reused across calls: a class instance, a `Map`, a database client, a
function… [InvokeReferenceAsync](cref:Meziantou.Framework.NodeJs.NodeJsHost.InvokeReferenceAsync) and [EvaluateReferenceAsync](cref:Meziantou.Framework.NodeJs.NodeJsHost.EvaluateReferenceAsync) keep the result in the Node.js process and return a
[JSReference](cref:Meziantou.Framework.NodeJs.JSReference) to it, instead of its JSON representation:

<!-- ref:../../tests/Meziantou.Framework.NodeJs.Tests/NodeJsHostTests.cs;region=JSReference_Example;format=md-fenced-code-block;dedent=true -->
```csharp
await using var node = await NodeJsHost.StartAsync();

// A Hash object cannot be serialized as JSON, so it stays in the Node.js process
await using var hash = await node.InvokeReferenceAsync("node:crypto", "createHash", ["sha256"]);
await hash.InvokeVoidAsync("update", ["hello"]);

// A function receives the referenced value
await node.EvaluateVoidAsync("args[0].update(' world');", [hash]);

var digest = await hash.InvokeAsync("digest", ["hex"]);
Assert.Equal("b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9", digest.GetString());
```
<!-- endref -->

- [InvokeAsync](cref:Meziantou.Framework.NodeJs.JSReference.InvokeAsync), [InvokeVoidAsync](cref:Meziantou.Framework.NodeJs.JSReference.InvokeVoidAsync) and [InvokeReferenceAsync](cref:Meziantou.Framework.NodeJs.JSReference.InvokeReferenceAsync) on the reference call a method of the value, with the
  value as `this`. When the member is not a function, its value is returned. Pass `null` as the method name to call the
  value itself, when it is a function.
- A [JSReference](cref:Meziantou.Framework.NodeJs.JSReference) converts implicitly to `JsonNode`, so it can be an argument of any call, or be nested in a
  `JsonObject` or a `JsonArray` argument. The function receives the value itself.
- The reference is disposable: dispose it once you no longer need the value, so Node.js can collect it.

## Create an instance of a class

[CreateInstanceAsync](cref:Meziantou.Framework.NodeJs.JSReference.CreateInstanceAsync) creates an instance of an exported class (`new`), and keeps it in the process. On a reference, it
creates an instance of a class that is a member of the value or, when you pass `null`, of the referenced class:

<!-- ref:../../tests/Meziantou.Framework.NodeJs.Tests/NodeJsHostTests.cs;region=NodeJs_CreateInstance;format=md-fenced-code-block;dedent=true -->
```csharp
await using var node = await NodeJsHost.StartAsync();

// new URL(...): the instance stays in the Node.js process
await using var url = await node.CreateInstanceAsync("node:url", "URL", ["https://example.com/docs?page=2"]);

// A member that is not a function returns its value
var hostname = await url.InvokeAsync<string>("hostname");
Assert.Equal("example.com", hostname);

// Code that receives the reference can change the value
await node.EvaluateVoidAsync("args[0].searchParams.set('page', '3');", [url]);

// The JSON representation of the value: URL.toJSON returns the address
var address = await url.GetValueAsync<string>();
Assert.Equal("https://example.com/docs?page=3", address);
```
<!-- endref -->

[GetValueAsync](cref:Meziantou.Framework.NodeJs.JSReference.GetValueAsync) returns the JSON representation of the value, or deserializes it, as for the result of a call. When the
value is a promise, it is awaited.

## All the operations

The calls on a reference mirror the calls on the host. With modules of your own, a database client looks like this:

<!-- ref:../../samples/Meziantou.Framework.NodeJs.Samples/ReadmeExamples.cs;region=NodeJs_References;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

## Lifetime

- The value is kept until the reference is disposed or the process exits.
- A reference can only be used with the process that created it. Once the process exits, the calls that use it fail with
  a [NodeJsException](cref:Meziantou.Framework.NodeJs.NodeJsException) whose [ExitCode](cref:Meziantou.Framework.NodeJs.NodeJsException.ExitCode) is set. See [Handling errors](08-HandlingErrors.md).
- With a pool, a reference is bound to the process that created it, and the calls of the pool whose arguments contain
  it run on that process. With worker threads, the value is kept by the worker thread that created it, and is lost when
  this worker thread exits. A call cannot use references kept by different processes or worker threads. See
  [Running calls in parallel](10-ParallelCalls.md).
- A [JSReference](cref:Meziantou.Framework.NodeJs.JSReference) can only be used in the arguments of a call. It cannot be a member of an object serialized using
  reflection, and a `JsonNode` created from it cannot be serialized or cloned.
