---
title: Getting started
description: Install the package and Node.js, run JavaScript code, and call a function of your own module.
---

## Install the package and Node.js

Add the `Meziantou.Framework.NodeJs` package to a project:

```bash
dotnet add package Meziantou.Framework.NodeJs
```

The package does not include Node.js. Install Node.js 16.9 or later on the machines that run your application, and
check that the `node` executable is in the `PATH`:

```bash
node --version
```

When `node` is not in the `PATH`, set [NodeJsHostOptions.NodeExecutablePath](cref:Meziantou.Framework.NodeJs.NodeJsHostOptions.NodeExecutablePath), as described in
[Configuring the Node.js process](07-ConfiguringTheProcess.md#locate-nodejs).

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework.NodeJs;`, and
`using System.Text.Json;` or `using System.Text.Json.Nodes;` for the JSON types.

## Run JavaScript code

Start a Node.js process, evaluate code, and call a function of a built-in module:

<!-- ref:../../tests/Meziantou.Framework.NodeJs.Tests/NodeJsHostTests.cs;region=NodeJsHost_Example;format=md-fenced-code-block;dedent=true -->
```csharp
await using var node = await NodeJsHost.StartAsync();

// Evaluate code as the body of an async function
var sum = await node.EvaluateAsync("return 1 + 2;");
Assert.Equal(3, sum.GetInt32());

// Call a function exported by a module: a file, a package, or a built-in module
var text = await node.InvokeAsync("node:util", "format", ["%s has %d items", "cart", 3]);
Assert.Equal("cart has 3 items", text.GetString());
```
<!-- endref -->

- [NodeJsHost.StartAsync](cref:Meziantou.Framework.NodeJs.NodeJsHost.StartAsync) starts the `node` process. The host is disposable: the `await using` declaration stops the
  process.
- [EvaluateAsync](cref:Meziantou.Framework.NodeJs.NodeJsHost.EvaluateAsync) runs the code as the body of an async function and returns the JSON representation of its result, as
  a `JsonElement`.
- [InvokeAsync](cref:Meziantou.Framework.NodeJs.NodeJsHost.InvokeAsync) imports a module and calls one of its exports with the arguments. `node:util` is a built-in module of
  Node.js; the next example calls a file of your own.

## Call your own JavaScript

Put your JavaScript code in a module, such as `scripts/slug.mjs`:

<!-- ref:../../tests/Meziantou.Framework.NodeJs.Tests/scripts/slug.mjs;format=md-fenced-code-block;language=javascript -->
```javascript
// Turns a title into a URL segment: "Hello, World!" becomes "hello-world"
export function slugify(title) {
  return title.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
}

// An export that is not a function
export const separator = '-';
```
<!-- endref -->

Copy the folder to the output directory of your application, for instance with
`<None Update="scripts/**" CopyToOutputDirectory="PreserveNewest" />` in the project file. Then use it as the working
directory of the process, and call the function:

<!-- ref:../../tests/Meziantou.Framework.NodeJs.Tests/NodeJsHostTests.cs;region=NodeJs_CallModule;format=md-fenced-code-block;dedent=true -->
```csharp
await using var node = await NodeJsHost.StartAsync(new NodeJsHostOptions
{
    // The folder of the JavaScript files, copied to the output directory of the application
    WorkingDirectory = Path.Combine(AppContext.BaseDirectory, "scripts"),
});

var slug = await node.InvokeAsync<string>("./slug.mjs", "slugify", ["Hello, World!"]);
Assert.Equal("hello-world", slug);
```
<!-- endref -->

- A relative module path starts with `./` and includes the file extension. It is resolved from the working directory.
- [InvokeAsync\<string\>](cref:Meziantou.Framework.NodeJs.NodeJsHost.InvokeAsync) deserializes the result to a .NET type. See
  [Arguments and results](05-ArgumentsAndResults.md) for the types you can use, and for the overloads that work with
  trimming and Native AOT.
- Start one host and reuse it for many calls: starting a process is slow, and a module is loaded once per process.
  [NodeJsHost](cref:Meziantou.Framework.NodeJs.NodeJsHost) is thread-safe.

## Next steps

- [Running JavaScript code](03-RunningCode.md) and [Calling modules](04-CallingModules.md): the details of the two kinds
  of calls, and how npm packages are found.
- [Handling errors](08-HandlingErrors.md): what happens when the JavaScript code throws.
- [Running calls in parallel](10-ParallelCalls.md): run CPU-bound code on several threads or processes.
