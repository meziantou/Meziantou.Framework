---
title: Exchanging values
description: Pass values, objects, functions and binary data between .NET and JavaScript.
---

The pages of this group cover what crosses the boundary between .NET and JavaScript. Nothing is converted
automatically: the engine uses no reflection, so a .NET object becomes visible to the scripts only through the
objects and functions you create for it.

A few rules hold for all of them:

- Every JavaScript value is a `JsValue`: `undefined`, `null`, a boolean, a number, a string, a symbol, a BigInt or an
  object. The numbers, the strings and the booleans of .NET convert to it implicitly.
- The objects are `JsObject` instances, created by the factories of the realm (`CreateObject`, `CreateArray`,
  `CreateFunction`...) or by the scripts.
- Reading and writing properties happens inside `engine.Enter()`, or inside a .NET function called by a script. See
  [The scope of an engine](../03-EnginesAndRealms.md#the-scope-of-an-engine).
- An object belongs to its realm, and its realm to its engine.

## Pages of this group

- [Values](02-Values.md): read the kind of a value, convert it, create values, and exchange dates.
- [Objects and arrays](03-ObjectsAndArrays.md): create objects and arrays, read the objects of a script, define
  read-only and lazy properties, exchange data as JSON.
- [Functions](04-Functions.md): expose .NET functions, read their arguments, call JavaScript functions and
  constructors from .NET.
- [Exposing .NET objects](05-ExposingDotNetObjects.md): expose a .NET class with a constructor and a prototype, or
  implement an object whose properties come from .NET.
- [Binary data](06-BinaryData.md): share a .NET buffer with the scripts, read their typed arrays, share memory between
  engines.
