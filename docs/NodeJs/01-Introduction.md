---
title: Introduction
description: Run JavaScript code and call Node.js modules, local files or npm packages, from .NET.
---

`Meziantou.Framework.NodeJs` runs JavaScript code and calls Node.js modules from .NET. Use it to reuse a JavaScript
library that has no .NET equivalent, such as a Markdown renderer, a code formatter or a bundler, or your own JavaScript
code, without writing a web service around it.

## What you can do with it

- **Run JavaScript code**: evaluate a few lines of code, with arguments, and get the result. See
  [Running JavaScript code](03-RunningCode.md).
- **Call modules**: call a function exported by your own files, by an npm package or by a built-in module of Node.js,
  in ES module or CommonJS syntax. See [Calling modules](04-CallingModules.md).
- **Exchange data**: arguments and results are converted to and from JSON, with typed results that work with trimming
  and Native AOT, and the JavaScript values that JSON cannot represent, such as `undefined`, `BigInt` or `Date`. See
  [Arguments and results](05-ArgumentsAndResults.md).
- **Keep JavaScript objects alive**: keep a value that cannot be converted to JSON, such as a class instance or a
  database client, in Node.js, and call its methods. See [Keeping values in Node.js](06-References.md).
- **Run calls in parallel**: on the worker threads of one process, or on a pool of processes. See
  [Running calls in parallel](10-ParallelCalls.md).
- **Stop runaway code**: kill a process blocked by a canceled call, such as an infinite loop. See
  [Cancellation and timeouts](09-CancellationAndTimeouts.md).

## How it works

`NodeJsHost` starts the `node` executable and communicates with it using JSON messages over a private local socket (a
named pipe on Windows, a Unix domain socket elsewhere). This has a few consequences:

- **Node.js must be installed.** The package does not include it: it uses Node.js 16.9 or later, found in the `PATH` or
  at the path you set. Returning `BigInt` values requires Node.js 21 or later. See
  [Configuring the Node.js process](07-ConfiguringTheProcess.md).
- **The code runs in another process.** A JavaScript error, or the exit of the Node.js process, fails the calls with a
  `NodeJsException`, not your application. See [Handling errors](08-HandlingErrors.md).
- **Data crosses the boundary as JSON.** Arguments and results are serialized, and JavaScript code cannot call back into
  .NET.
- **The process belongs to the host.** It exits when the host is disposed, and everything it keeps (global variables,
  loaded modules, references) is lost with it.

The JavaScript code runs with the permissions of your application: only run code you trust. See [Security](11-Security.md).

## Namespaces

All the types are in the `Meziantou.Framework.NodeJs` namespace. Results are returned as `JsonElement`
(`System.Text.Json`) or deserialized to your types, and arguments are `JsonNode` values (`System.Text.Json.Nodes`) or
objects serialized using reflection.

| Type | Use |
| --- | --- |
| `NodeJsHost` | Starts a Node.js process and runs code in it |
| `NodeJsHostOptions` | The path of `node`, the working directory, timeouts, worker threads… |
| `NodeJsHostPool` | Runs calls on several Node.js processes |
| `JSValue` | An argument that JSON cannot represent, such as `undefined` or a `BigInt` |
| `JSReference` | A value kept in the Node.js process |
| `NodeJsException` | A JavaScript error, or a failure of the Node.js process |

## Next steps

- [Getting started](02-GettingStarted.md): install the package and call your first JavaScript function.
- [Calling modules](04-CallingModules.md): call your own files and npm packages.
- [Limitations](12-Limitations.md): what the library does not do.
