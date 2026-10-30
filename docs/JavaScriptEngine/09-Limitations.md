---
title: Limitations
description: What the engine does not provide, so you can plan around it.
---

The engine implements the ECMAScript language and its standard library. What a browser or Node.js adds on top of it
is not part of the engine.

## No host APIs

A realm has the built-ins of ECMAScript only. These are not provided:

- `console`, `setTimeout`, `setInterval`, `queueMicrotask`, `structuredClone`;
- `fetch`, `URL`, `TextEncoder`, `TextDecoder`, `atob`, `btoa`, and the other APIs of the web platform: the DOM,
  `window`, `document`, workers...;
- Node.js: `require`, `process`, `Buffer`, and the built-in modules (`fs`, `path`...);
- `WebAssembly`.

Add what your scripts need with .NET functions and objects: see [Functions](05-Interop/04-Functions.md),
[Exposing .NET objects](05-Interop/05-ExposingDotNetObjects.md) and, for timers,
[Async code and promises](06-AsyncAndPromises.md#await-a-net-task-from-a-script). To run scripts in HTML documents
with the DOM API, use `Meziantou.Framework.WebPlatform` with `Meziantou.Framework.WebPlatform.JavaScript`, which runs
them with this engine.

## Modules

- Only ES modules are supported. A CommonJS package (`require`, `module.exports`) must be bundled or converted to an
  ES module first.
- The engine resolves and reads no file: your module loader resolves each specifier and gives its source. There is no
  `node_modules` resolution and no import map. See [Modules](04-RunningCode/03-Modules.md).
- The `export defer` proposal is not implemented (`import defer` is).

## .NET interop

- No automatic binding: .NET objects, methods, properties, events and enumerables are not exposed by reflection. You
  create the JavaScript objects and functions that represent them. See
  [Exposing .NET objects](05-Interop/05-ExposingDotNetObjects.md).
- No conversion of `Task` to promises or of promises to `Task`: settle the promises from your event loop, and read
  their state with `TryGetPromiseState`. See [Async code and promises](06-AsyncAndPromises.md).
- No conversion of .NET collections: build arrays with `CreateArray`, or exchange JSON. See
  [Objects and arrays](05-Interop/03-ObjectsAndArrays.md).

## Execution

- **An interpreter.** The code is compiled to bytecode and interpreted; there is no JIT compilation to machine code.
  Compile the scripts that run many times once, with `JsPreparedScript`.
- **One thread at a time per engine.** Use one engine per thread to run scripts in parallel. Engines share memory only
  through `SharedArrayBuffer`.
- **No debugger.** There is no debugging protocol and no breakpoints; the errors give their location and their stack.
- **The memory limit counts allocations**, not the memory that the script keeps. See
  [Limits and sandboxing](07-LimitsAndSandboxing.md).

## Platform

