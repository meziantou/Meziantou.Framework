---
title: Running code
description: Run scripts and ES modules, reuse compiled scripts, and handle the errors they throw.
---

The pages of this group cover the two kinds of code that a realm runs, and what happens when they fail:

- A **script** is run by `realm.Execute`, which returns the value of its last statement. The scripts of a realm share
  their global variables.
- A **module** is parsed by `realm.ParseModule`, then loaded, linked and evaluated. Its `import` declarations go
  through the module loader of the engine, and its variables are its own.

In both cases, an uncaught error is thrown to .NET as a `JsException`, and a script stopped by a limit throws a
`JsTerminationException`.

## Pages of this group

- [Scripts](02-Scripts.md): run a script, give it a file name, compile it once and run it in several engines, check its
  syntax without running it.
- [Modules](03-Modules.md): write a module loader, load JSON, text and .NET modules, read the exports, use `import()`.
- [Handling errors](04-HandlingErrors.md): catch the errors of the scripts, find where they were thrown, throw
  JavaScript errors from .NET.
