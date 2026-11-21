---
title: Memories, tables and globals
description: Read and write the memories of the instances, grow them, and access their tables and globals.
---

The bytes of a memory are a .NET array: read and write them with
[GetSpan](cref:Meziantou.Framework.WasmEngine.WasmMemory.GetSpan) or
[Data](cref:Meziantou.Framework.WasmEngine.WasmMemory.Data).

<!-- ref:../../tests/Meziantou.Framework.WasmEngine.Tests/WasmRuntimeTests.cs;region=WasmEngine_Memory;format=md-fenced-code-block;dedent=true -->
```csharp
using var runtime = new WasmRuntime();
var module = runtime.Compile(WasmRuntime.WatToWasm("""
    (module
      (memory (export "memory") 1 4)
      (data (i32.const 0) "Hello")
      (func (export "grow") (result i32) (memory.grow (i32.const 1))))
    """));

using var store = runtime.CreateStore();
var instance = store.Instantiate(module);
var memory = instance.GetMemory("memory")!;
Assert.Equal("Hello", Encoding.ASCII.GetString(memory.GetSpan()[..5]));

// The bytes of a memory are a .NET array, replaced when the memory grows
var changes = 0;
memory.DataChanged += (sender, e) => changes++;
instance.GetFunction("grow")!.Call();
Assert.Equal(2UL, memory.Size); // pages of 64 KiB
Assert.HasCount(2 * 65536, memory.Data);
Assert.Equal(1, changes);
```
<!-- endref -->

- The sizes are in pages of 64 KiB ([PageSize](cref:Meziantou.Framework.WasmEngine.WasmMemoryType.PageSize)).
- The array is replaced when the memory grows, by [Grow](cref:Meziantou.Framework.WasmEngine.WasmMemory.Grow) or by a
  `memory.grow` instruction: do not keep it, or a span of it, across calls into WebAssembly.
  [DataChanged](cref:Meziantou.Framework.WasmEngine.WasmMemory.DataChanged) is raised after it is replaced.
- A memory cannot grow beyond its maximum size or
  [MaxMemorySize](cref:Meziantou.Framework.WasmEngine.WasmRuntimeOptions.MaxMemorySize): `memory.grow` returns -1, and
  [Grow](cref:Meziantou.Framework.WasmEngine.WasmMemory.Grow) throws a
  [WasmAllocationException](cref:Meziantou.Framework.WasmEngine.WasmAllocationException).

The bytes of a shared memory never move: see [Shared memories](07-SharedMemories.md).

## Tables and globals

<!-- ref:../../tests/Meziantou.Framework.WasmEngine.Tests/WasmRuntimeTests.cs;region=WasmEngine_GlobalsAndTables;format=md-fenced-code-block;dedent=true -->
```csharp
using var runtime = new WasmRuntime();
var module = runtime.Compile(WasmRuntime.WatToWasm("""
    (module
      (global (export "counter") (mut i32) (i32.const 0))
      (table (export "callbacks") 1 funcref)
      (func (export "increment")
        (global.set 0 (i32.add (global.get 0) (i32.const 1)))))
    """));

using var store = runtime.CreateStore();
var instance = store.Instantiate(module);
var counter = instance.GetGlobal("counter")!;
var callbacks = instance.GetTable("callbacks")!;

// A table contains references: put the exported function in it
callbacks.Set(0, WasmValue.FromReference(instance.GetFunction("increment")));
var increment = (WasmFunction)callbacks.Get(0).Reference!;
increment.Call();
increment.Call();
Assert.Equal(2, counter.Get().I32);

counter.Set(WasmValue.FromI32(40));
Assert.Equal(40, counter.Get().I32);
```
<!-- endref -->

- A [WasmTable](cref:Meziantou.Framework.WasmEngine.WasmTable) contains references of its element type. An index out
  of its bounds throws a [WasmAllocationException](cref:Meziantou.Framework.WasmEngine.WasmAllocationException), and so
  does a growth beyond its maximum size or
  [MaxTableElements](cref:Meziantou.Framework.WasmEngine.WasmStoreOptions.MaxTableElements).
- Setting an immutable [WasmGlobal](cref:Meziantou.Framework.WasmEngine.WasmGlobal), or a value of another type, throws
  a [WasmTypeMismatchException](cref:Meziantou.Framework.WasmEngine.WasmTypeMismatchException).
