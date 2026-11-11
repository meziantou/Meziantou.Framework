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

Add what your scripts need with .NET functions and objects: see
[Console, timers and other host APIs](05-Interop/07-HostApis.md), [Functions](05-Interop/04-Functions.md) and
[Exposing .NET objects](05-Interop/05-ExposingDotNetObjects.md). To run scripts in HTML documents
with the DOM API, use `Meziantou.Framework.WebPlatform` with `Meziantou.Framework.WebPlatform.JavaScript`, which runs
them with this engine.

## Modules

- Only ES modules are supported. A CommonJS package (`require`, `module.exports`) must be bundled or converted to an
  ES module first, or run by a `require` that your application provides: see
  [CommonJS modules](04-RunningCode/06-CommonJsModules.md).
- The engine resolves and reads no file: your module loader resolves each specifier and gives its source. There is no
  `node_modules` resolution and no import map. See [Modules](04-RunningCode/03-Modules.md).

## .NET interop

- No binding by reflection: a .NET class is exposed when you mark it with `[JsExport]`, whose source generator writes
  its JavaScript class at build time, or when you create the JavaScript objects and functions that represent it. The
  generated classes do not expose the events, the indexers, the generic methods and the overloads (one JavaScript name
  per member), and require a `partial` class that is not generic: expose the other classes, such as the classes of
  other libraries, by hand. See [Exposing .NET objects](05-Interop/05-ExposingDotNetObjects.md).
- The conversions of [Converting .NET values](05-Interop/08-DotNetValues.md) cover the primitives, the dates, the
  collections, the dictionaries, JSON and the classes marked with `[JsExport]`, not the other classes.
- The promises of tasks settle while the host runs the jobs of the engine. See
  [Async code and promises](06-AsyncAndPromises.md).

## Execution

- **An interpreter.** The code is compiled to bytecode and interpreted; there is no JIT compilation to machine code.
  Compile the scripts that run many times once, with [JsPreparedScript](cref:Meziantou.Framework.JavaScriptEngine.JsPreparedScript).
- **One thread at a time per engine.** Use one engine per thread to run scripts in parallel. Engines share memory only
  through `SharedArrayBuffer`.
- **No debugging protocol.** Chrome DevTools and Visual Studio Code cannot attach to the engine. Your application can
  set breakpoints, step through the scripts, read their variables and evaluate code in a paused script with the
  debugger API: see [Debugging and inspecting scripts](04-RunningCode/05-Debugging.md).
- **The memory limit counts allocations**, not the memory that the script keeps. See
  [Limits and sandboxing](07-LimitsAndSandboxing.md).
- **The nesting of the code is limited.** A source nested deeper than 1,500 levels (parentheses, brackets, blocks,
  unary operators, nested functions...) is a `SyntaxError` ("the code is nested too deeply"), and so is a shallower one
  when the stack of the thread is too small to compile it: the engine never overflows the stack. The long chains of
  generated code are not nested: `else if` chains and chains of operators, member accesses and calls are handled in loops.

## Results

- Some results are left to the implementation by the specification (the time zone names of `Date.prototype.toString`,
  the error messages, the precision of `Math.sin`...), and the Unicode, locale and time zone data have their own
  versions: they can differ from Node.js and the browsers. See
  [Data versions and output differences](11-DataAndOutputDifferences.md).
