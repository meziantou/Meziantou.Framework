---
title: Trimming and Native AOT
description: The executor relies on reflection and runtime code generation, so it is not compatible with trimming and Native AOT.
---

[ObjectMethodExecutor](cref:Meziantou.Framework.ObjectMethodExecutor) calls any method of any type: it reflects over the type of the method, compiles expression
trees, and may construct generic types at run time, for instance to adapt an F# `Async<T>`. The class is marked with
`[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`, so the compiler warns where you use it in a project that is
trimmed or published with Native AOT.

- The trimmer cannot see the methods you call through a `MethodInfo`, nor the awaiter members the executor looks for, and
  may remove them.
- Native AOT cannot create, at run time, the generic instantiations that were not compiled ahead of time.

Use the executor in applications that run with the JIT and are not trimmed. For trimmed or Native AOT applications, call
the methods through delegates or interfaces known at compile time instead.
