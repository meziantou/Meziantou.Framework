---
title: Calling modules
description: Call the exports of your own files, of npm packages and of built-in modules, in ES module or CommonJS syntax.
---

## Call an export

`InvokeAsync` imports a module and calls one of its exports. The examples below use these three files, in the `scripts`
folder of the output directory.

`slug.mjs`, an ES module with named exports:

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

`greet.mjs`, an ES module with a default export that returns a promise:

<!-- ref:../../tests/Meziantou.Framework.NodeJs.Tests/scripts/greet.mjs;format=md-fenced-code-block;language=javascript -->
```javascript
// The default export
export default async function greet(name) {
  await new Promise(resolve => setTimeout(resolve, 10));
  return `Hello, ${name}!`;
}
```
<!-- endref -->

`cart.cjs`, a CommonJS module:

<!-- ref:../../tests/Meziantou.Framework.NodeJs.Tests/scripts/cart.cjs;format=md-fenced-code-block;language=javascript -->
```javascript
// A CommonJS module: its exports are the properties of module.exports
exports.total = items => items.reduce((sum, item) => sum + item.price * item.quantity, 0);
```
<!-- endref -->

Call them from .NET:

<!-- ref:../../tests/Meziantou.Framework.NodeJs.Tests/NodeJsHostTests.cs;region=NodeJs_ModuleSpecifiers;format=md-fenced-code-block;dedent=true -->
```csharp
var scripts = Path.Combine(AppContext.BaseDirectory, "scripts");
await using var node = await NodeJsHost.StartAsync(new NodeJsHostOptions { WorkingDirectory = scripts });

// A path relative to the working directory starts with ./ or ../ and includes the extension
var slug = await node.InvokeAsync<string>("./slug.mjs", "slugify", ["Modules and exports"]);
Assert.Equal("modules-and-exports", slug);

// An export that is not a function returns its value
var separator = await node.InvokeAsync<string>("./slug.mjs", "separator");
Assert.Equal("-", separator);

// null calls the default export, and the promise it returns is awaited
var greeting = await node.InvokeAsync<string>("./greet.mjs", exportName: null, ["World"]);
Assert.Equal("Hello, World!", greeting);

// A CommonJS module
var total = await node.InvokeAsync<int>("./cart.cjs", "total", [new[] { new { Price = 3, Quantity = 2 }, new { Price = 5, Quantity = 1 } }]);
Assert.Equal(11, total);

// A file: URL (or an absolute path), and a built-in module
var fromUrl = await node.InvokeAsync<string>(new Uri(Path.Combine(scripts, "greet.mjs")).AbsoluteUri, exportName: null, ["file"]);
Assert.Equal("Hello, file!", fromUrl);
var extension = await node.InvokeAsync<string>("node:path", "extname", ["index.html"]);
Assert.Equal(".html", extension);
```
<!-- endref -->

- When the export is a function, it is called with the arguments, and its result is awaited when it is a promise.
  Otherwise, the value of the export is returned.
- Pass `null` as the export name to use the default export.
- ES modules and CommonJS modules are called the same way: the exports of a CommonJS module are the properties of
  `module.exports`.

## Module specifiers

Modules are loaded using `import()`, so the module specifier follows the rules of ES modules, even for a CommonJS module:

- **A relative path** starts with `./` or `../`, uses `/` as separator, and includes the file extension, such as
  `./scripts/math.mjs`. It is resolved from the working directory of the process. `scripts/math.mjs` is the name of a
  package (`scripts`), and `./scripts/math` is not found.
- **An absolute path**, or a `file:` URL.
- **A built-in module**, such as `node:path` or `node:fs/promises`.
- **An npm package name**, such as `marked`.

`InvokeAsync` accepts no other URL: a `data:` URL, which contains the code to run, is rejected.

The working directory is `NodeJsHostOptions.WorkingDirectory`, or the current directory of your application when it is
not set. See [Configuring the Node.js process](07-ConfiguringTheProcess.md).

## Use npm packages

An npm package is resolved from the `node_modules` folders of the working directory and of its parents, as Node.js does.
Install the packages in the folder you use as working directory:

```bash
npm install marked
```

Then call the package by its name, such as `InvokeAsync("marked", "parse", ["# Hello"])`. The process must be able to
read the `node_modules` folder at run time: deploy it with your application, or install the packages on the machine.

## Modules are loaded once

A module is loaded the first time a call imports it, and kept for the lifetime of the process (or of the worker thread,
with [worker threads](10-ParallelCalls.md#worker-threads)):

- Its top-level code runs once, so a module can create a value once and reuse it in every call, such as a cache or a
  connection pool.
- A change to its file has no effect until a new host is started.
- A module whose top-level code throws keeps failing.
