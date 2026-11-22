---
title: Shared memories
description: Share a memory between the stores of several threads, and wait and notify with the atomic instructions.
---

A shared memory can be imported by the stores of several threads, and its methods can be called from any thread:

<!-- ref:../../tests/Meziantou.Framework.WasmEngine.Tests/WasmRuntimeTests.cs;region=WasmEngine_SharedMemory;format=md-fenced-code-block;dedent=true -->
```csharp
using var runtime = new WasmRuntime();
var module = runtime.Compile(WasmRuntime.WatToWasm("""
    (module
      (import "env" "memory" (memory 1 1 shared))
      (func (export "wait") (result i32)
        (memory.atomic.wait32 (i32.const 0) (i32.const 0) (i64.const -1))))
    """));

// A shared memory can be imported by the stores of several threads
using var store = runtime.CreateStore();
var memory = store.CreateMemory(new WasmMemoryType(minimum: 1, maximum: 1, shared: true));

var waiter = Task.Run(() =>
{
    using var waiterStore = runtime.CreateStore();
    var instance = waiterStore.Instantiate(module, [memory]);
    return instance.GetFunction("wait")!.Call()[0].I32; // 0: notified
});

// Wait until the other thread waits, then wake it
while (!waiter.IsCompleted && memory.AtomicNotify(address: 0, count: 1) == 0)
{
    await Task.Delay(10);
}

Assert.Equal(0, await waiter);
```
<!-- endref -->

- The bytes of a shared memory are allocated once for its maximum size (limited by
  [MaxMemorySize](cref:Meziantou.Framework.WasmEngine.WasmRuntimeOptions.MaxMemorySize)), and never move:
  [SharedBlock](cref:Meziantou.Framework.WasmEngine.WasmMemory.SharedBlock) gives their address, and
  [GetSpan](cref:Meziantou.Framework.WasmEngine.WasmMemory.GetSpan) a span that stays valid.
  [Data](cref:Meziantou.Framework.WasmEngine.WasmMemory.Data) throws a `NotSupportedException`.
- [AtomicWait](cref:Meziantou.Framework.WasmEngine.WasmMemory.AtomicWait) and
  [AtomicNotify](cref:Meziantou.Framework.WasmEngine.WasmMemory.AtomicNotify) are the `memory.atomic.wait32`,
  `memory.atomic.wait64` and `memory.atomic.notify` instructions: they wait for, and notify, the waiters of the
  instructions of the modules.
- [GrowTo](cref:Meziantou.Framework.WasmEngine.WasmMemory.GrowTo) grows a memory to a size, never beyond it, when
  several threads grow it at the same time.

## Providing the bytes

By default, the bytes are a pinned .NET array. Set
[SharedMemoryAllocator](cref:Meziantou.Framework.WasmEngine.WasmStoreOptions.SharedMemoryAllocator) to provide them,
for instance to share them with the `SharedArrayBuffer` objects of a script engine: it returns a
[WasmSharedMemoryBlock](cref:Meziantou.Framework.WasmEngine.WasmSharedMemoryBlock) whose address never changes while it
is alive, and whose bytes grow within its capacity. An exception it throws is thrown by the operation that creates the
memory.
