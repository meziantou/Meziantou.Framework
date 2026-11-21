---
title: Exceptions and traps
description: Catch the exceptions and the traps of WebAssembly in .NET, and throw WebAssembly exceptions from host functions.
---

<!-- ref:../../tests/Meziantou.Framework.WasmEngine.Tests/WasmRuntimeTests.cs;region=WasmEngine_Exceptions;format=md-fenced-code-block;dedent=true -->
```csharp
using var runtime = new WasmRuntime();
var module = runtime.Compile(WasmRuntime.WatToWasm("""
    (module
      (import "env" "fail" (func $fail))
      (tag $error (export "error") (param i32))
      (func (export "throw") (param i32) (throw $error (local.get 0)))
      (func (export "catch") (result i32)
        (block $handler (result i32)
          (try_table (catch $error $handler) (call $fail))
          (i32.const -1)))
      (func (export "divide") (param i32 i32) (result i32) (i32.div_s (local.get 0) (local.get 1))))
    """));

using var store = runtime.CreateStore();
WasmInstance? instance = null;

// A host function throws an exception in WebAssembly with a WasmThrowException
var fail = store.CreateFunction(new WasmFunctionType([], []), (arguments, results) =>
    throw new WasmThrowException(store.CreateException(instance!.GetTag("error")!, [WasmValue.FromI32(42)])));
instance = store.Instantiate(module, [fail]);
Assert.Equal(42, instance.GetFunction("catch")!.Call()[0].I32);

// An exception thrown by WebAssembly is a WasmThrowException
var thrown = Assert.Throws<WasmThrowException>(() => instance.GetFunction("throw")!.Call(WasmValue.FromI32(7)));
Assert.Same(instance.GetTag("error"), thrown.Exception.Tag);
Assert.Equal(7, thrown.Exception.GetArgument(0).I32);

// A trap is a WasmTrapException
var trap = Assert.Throws<WasmTrapException>(() => instance.GetFunction("divide")!.Call(WasmValue.FromI32(1), WasmValue.FromI32(0)));
Assert.Equal(WasmTrapKind.Other, trap.TrapKind);
```
<!-- endref -->

| Exception | Thrown when |
| --- | --- |
| [WasmCompileException](cref:Meziantou.Framework.WasmEngine.WasmCompileException) | A module is not valid |
| [WasmLinkException](cref:Meziantou.Framework.WasmEngine.WasmLinkException) | An import is missing or does not match its type |
| [WasmTrapException](cref:Meziantou.Framework.WasmEngine.WasmTrapException) | The code trapped (`unreachable`, an out-of-bounds access, a division by zero, a stack overflow...) |
| [WasmThrowException](cref:Meziantou.Framework.WasmEngine.WasmThrowException) | The code threw a WebAssembly exception that it did not catch |
| [WasmAllocationException](cref:Meziantou.Framework.WasmEngine.WasmAllocationException) | A memory or a table cannot be allocated or grow, or an index is out of bounds |
| [WasmTypeMismatchException](cref:Meziantou.Framework.WasmEngine.WasmTypeMismatchException) | A value does not match the type of its destination |

They derive from [WasmException](cref:Meziantou.Framework.WasmEngine.WasmException).

- [TrapKind](cref:Meziantou.Framework.WasmEngine.WasmTrapException.TrapKind) is
  [StackOverflow](cref:Meziantou.Framework.WasmEngine.WasmTrapKind.StackOverflow) when the recursion exceeds
  [MaxStackSize](cref:Meziantou.Framework.WasmEngine.WasmRuntimeOptions.MaxStackSize).
- The [Exception](cref:Meziantou.Framework.WasmEngine.WasmThrowException.Exception) of a
  [WasmThrowException](cref:Meziantou.Framework.WasmEngine.WasmThrowException) gives the tag and the values of the
  WebAssembly exception. A host function throws one with an exception of
  [CreateException](cref:Meziantou.Framework.WasmEngine.WasmStore.CreateException): the module can catch it.
- Any other exception of a host function, or of
  [CheckInterrupt](cref:Meziantou.Framework.WasmEngine.WasmStoreOptions.CheckInterrupt), stops the execution and is
  thrown as is by the call into WebAssembly: WebAssembly cannot catch it.
