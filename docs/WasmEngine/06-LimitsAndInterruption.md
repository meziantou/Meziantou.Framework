---
title: Limits, interruption and compilation
description: Interrupt long-running code, limit the stack, the memories and the tables, and compile the functions to .NET code.
---

The running code calls [CheckInterrupt](cref:Meziantou.Framework.WasmEngine.WasmStoreOptions.CheckInterrupt)
periodically. Throw from it to stop the code, for instance when a timeout elapses:

<!-- ref:../../tests/Meziantou.Framework.WasmEngine.Tests/WasmRuntimeTests.cs;region=WasmEngine_Interruption;format=md-fenced-code-block;dedent=true -->
```csharp
using var runtime = new WasmRuntime(new WasmRuntimeOptions
{
    InterruptionCheckInterval = TimeSpan.FromMilliseconds(10),
});
var module = runtime.Compile(WasmRuntime.WatToWasm("""(module (func (export "loop") (loop $l (br $l))))"""));

// The running code calls CheckInterrupt periodically: the exception it throws stops the code
using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
using var store = runtime.CreateStore(new WasmStoreOptions
{
    CheckInterrupt = cancellation.Token.ThrowIfCancellationRequested,
});

var loop = store.Instantiate(module).GetFunction("loop")!;
Assert.Throws<OperationCanceledException>(() => loop.Call());
```
<!-- endref -->

The exception is thrown by the call into WebAssembly; the code cannot catch it.

The runtime has one timer for its stores, which runs only while a store with a `CheckInterrupt` action exists: at
each interval, it asks the code of these stores to call their action. The code of the other stores never calls an
action, and its checks cost a read of a field.

| Option | Default | Effect |
| --- | --- | --- |
| [InterruptionCheckInterval](cref:Meziantou.Framework.WasmEngine.WasmRuntimeOptions.InterruptionCheckInterval) | 10 ms | How often the running code calls `CheckInterrupt` |
| [MaxStackSize](cref:Meziantou.Framework.WasmEngine.WasmRuntimeOptions.MaxStackSize) | 512 KiB | A deeper recursion traps with `StackOverflow` |
| [MaxMemorySize](cref:Meziantou.Framework.WasmEngine.WasmRuntimeOptions.MaxMemorySize) | The maximum length of a .NET array | A memory cannot grow beyond it |
| [MaxTableElements](cref:Meziantou.Framework.WasmEngine.WasmStoreOptions.MaxTableElements) | 10,000,000 | A table cannot grow beyond it (`table.grow` returns -1) |

## Compilation to .NET code

The functions are interpreted. When the platform supports dynamic code, a function is compiled to .NET code once it
is called [CompilationThreshold](cref:Meziantou.Framework.WasmEngine.WasmRuntimeOptions.CompilationThreshold) times
(100 by default): the JIT of .NET compiles it to native code, so the functions that run often are much faster. Set 0 to
compile the functions on their first call, or a negative value to always interpret them.

A call that runs a long loop does not wait for the next call: once the loops of the function ran 100 times
`CompilationThreshold` iterations, the running call continues in .NET code from the start of its loop (on-stack
replacement). The functions that run once, such as the main function of a program, are compiled too.

<!-- ref:../../tests/Meziantou.Framework.WasmEngine.Tests/WasmRuntimeTests.cs;region=WasmEngine_Compilation;format=md-fenced-code-block;dedent=true -->
```csharp
// A function is interpreted, and compiled to .NET code once it is called CompilationThreshold times (when the
// platform supports dynamic code)
using var runtime = new WasmRuntime(new WasmRuntimeOptions { CompilationThreshold = 10 });
var module = runtime.Compile(WasmRuntime.WatToWasm("""
    (module
      (func $fib (export "fib") (param i32) (result i32)
        (if (result i32) (i32.lt_u (local.get 0) (i32.const 2))
          (then (local.get 0))
          (else
            (i32.add
              (call $fib (i32.sub (local.get 0) (i32.const 1)))
              (call $fib (i32.sub (local.get 0) (i32.const 2))))))))
    """));
using var store = runtime.CreateStore();
var fib = store.Instantiate(module).GetFunction("fib")!;
Assert.Equal(6765, fib.Call(WasmValue.FromI32(20))[0].I32);
```
<!-- endref -->

- In a Native AOT application, the functions are always interpreted.
- A function that uses the tail calls (`return_call`, `return_call_indirect`, `return_call_ref`), or that has a very
  large number of locals, is interpreted.
- The compiled functions behave as the interpreted ones: the same results, traps and interruptions. A deep recursion
  traps with `StackOverflow` too, at another depth: the compiled functions use the stack of .NET.
- The calls between compiled functions are the fastest when the parameters and the result of the callee are numbers
  (`i32`, `i64`, `f32`, `f64`, at most 8 parameters and one result): the arguments are passed directly, without the
  stack of the interpreter. This applies to the direct calls, to the calls of the functions imported from another
  instance, and to the indirect calls (`call_indirect`, `call_ref`). The other calls, and the calls of host functions,
  go through the stack of the interpreter.
- The small functions of a module that make no calls and have no loops (up to 32 instructions) are inlined into
  their compiled callers of the same module: their calls cost nothing.
