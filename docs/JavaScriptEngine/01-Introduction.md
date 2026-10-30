---
title: Introduction
description: An ECMAScript engine written in .NET, to run JavaScript and ES modules inside your application.
---

`Meziantou.Framework.JavaScriptEngine` runs JavaScript inside a .NET application. It is written entirely in C#: a
parser, a bytecode compiler and an interpreter, with no native dependency. It is trimming- and Native AOT-compatible,
because it uses no reflection.

## What you can do with it

- **Run scripts and ES modules**, with your own module loader for imports, JSON, text and bytes modules. See
  [Running code](04-RunningCode/01-Overview.md).
- **Exchange values with .NET**: read the results of scripts, give them objects, arrays, dates and binary data, and call
  JavaScript functions from .NET. See [Exchanging values](05-Interop/01-Overview.md).
- **Expose .NET functions and objects** to the scripts, from a single function to a class with a prototype. See
  [Functions](05-Interop/04-Functions.md) and [Exposing .NET objects](05-Interop/05-ExposingDotNetObjects.md).
- **Run asynchronous code**: promises and `async` functions run from a job queue that your application drives, and a
  promise can wait for a .NET `Task`. See [Async code and promises](06-AsyncAndPromises.md).
- **Run untrusted code** with a timeout, a statement limit, a memory limit, a recursion limit and cancellation. See
  [Limits and sandboxing](07-LimitsAndSandboxing.md).
- **Format numbers, dates and lists for any locale** with `Intl`, and work with dates and time zones with `Date` and
  `Temporal`. See [Intl, dates and time zones](08-IntlDatesAndTimeZones.md).

## Language support

The engine implements ECMAScript 2025 (including the proper tail calls of strict code), ECMA-402 (`Intl`) and
`Temporal`. It also implements the stage 3 proposals decorators (with the `accessor` fields and `Symbol.metadata`),
`import defer`, source phase imports, immutable `ArrayBuffer`, `Promise.allKeyed` and `Promise.allSettledKeyed`, and
the `ShadowRealm`, Import Text and Import Bytes proposals.

It is validated by [test262](https://github.com/tc39/test262), the conformance suite of ECMAScript, by the build tools
of the Web Tooling Benchmark (babel, TypeScript, prettier, terser... give the same results as in Node.js), and by
about 60 npm packages compared with Chromium.

## Principles

- **Only the language.** A realm has the built-ins of ECMAScript and nothing else: no `console`, no timers, no file or
  network access. The scripts reach your application only through what you give them.
- **Your application drives the execution.** A script runs on the thread that calls [Execute](cref:Meziantou.Framework.JavaScriptEngine.JsRealm.Execute), and the promise jobs
  run when you call [RunJobs](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.RunJobs): nothing runs in the background.
- **Limits stop a script for good.** An exceeded limit or a cancellation throws an exception that the script cannot
  catch.
- **Same results everywhere.** The Unicode, time zone and locale data are part of the library: the results do not
  depend on the operating system or on the globalization settings of the process.
- **The vocabulary of the specification.** `Call`, `Construct`, `ToNumber`, realms, jobs... the API uses the names of
  the ECMAScript specification, so the specification describes what they do.

## Namespace

Every type is in the [Meziantou.Framework.JavaScriptEngine](cref:Meziantou.Framework.JavaScriptEngine) namespace.

## Next steps

- [Getting started](02-GettingStarted.md): install the package and run your first script.
- [Engines and realms](03-EnginesAndRealms.md): the model behind the API.
- [Limitations](09-Limitations.md): what the engine does not do.
