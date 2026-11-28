---
title: Asynchronous functions and streams
description: Give asynchronous .NET functions to the components, exchange streams and futures, and run their threads.
---

The component model has asynchronous functions (`async func`), streams (`stream<T>`) and futures (`future<T>`), on
which WASI 0.3 is built. The engine runs them with a store-wide event loop: a call of a component returns when its task
completes, while the other tasks and threads of the store, and the continuations of the host, run.

## Asynchronous host functions

[ComponentFunction.FromAsyncCallback](cref:Meziantou.Framework.WasmEngine.Components.ComponentFunction.FromAsyncCallback)
creates a function whose steps are an asynchronous .NET callback. The component waits for its result, either by blocking
its task (a call with the synchronous ABI) or by waiting for its subtask (a call with the asynchronous ABI):

<!-- ref:../../../tests/Meziantou.Framework.WasmEngine.Tests/WasmComponentTests.cs;region=Component_AsyncHostFunctions;format=md-fenced-code-block;dedent=true -->
```csharp
using var runtime = new WasmRuntime();

// A component that calls the asynchronous function double of the host: the call blocks the asynchronous task of
// the component until the function completes, while the store runs the other tasks and the continuations of the host
var component = runtime.CompileComponent(WasmRuntime.WatToWasm("""
    (component
      (import "double" (func $double async (param "value" u32) (result u32)))
      (core func $double-core (canon lower (func $double)))
      (core module $M
        (import "host" "double" (func $double (param i32) (result i32)))
        (func (export "run") (result i32)
          (call $double (i32.const 21))))
      (core instance $m (instantiate $M (with "host" (instance (export "double" (func $double-core))))))
      (func (export "run") async (result u32) (canon lift (core func $m "run"))))
    """));

var imports = new ComponentImports()
    .Define("double", ComponentFunction.FromAsyncCallback(async (context, arguments) =>
    {
        await Task.Delay(TimeSpan.FromMilliseconds(10), context.CancellationToken);
        return ComponentValue.FromU32(arguments[0].AsU32() * 2);
    }));

using var store = runtime.CreateStore();
var instance = store.InstantiateComponent(component, imports);
var result = instance.GetFunction("run")!.Call();

Assert.Equal(42u, result?.AsU32());
```
<!-- endref -->

- The continuations of the callback run on the thread that called the component, between the steps of the components.
- [CancellationToken](cref:Meziantou.Framework.WasmEngine.Components.ComponentCallContext.CancellationToken) is
  cancelled when the component cancels the call.
- [RunInBackground](cref:Meziantou.Framework.WasmEngine.Components.ComponentCallContext.RunInBackground) runs an
  operation that continues after the function returns, such as a copy between a stream of the component and a .NET
  stream: the store waits for it when the components wait.

A component whose tasks all wait for something that will never happen traps (`deadlock detected`).

## Streams and futures

The host reads the streams and the futures that a component gives it, and writes the ones it gives to a component:

- [AsStreamReader](cref:Meziantou.Framework.WasmEngine.Components.ComponentValue.AsStreamReader) returns a
  [ComponentStreamReader](cref:Meziantou.Framework.WasmEngine.Components.ComponentStreamReader): `ReadAsync` returns
  the values that the component writes, and the bytes of a `stream<u8>`.
- [ComponentStreamWriter.Create](cref:Meziantou.Framework.WasmEngine.Components.ComponentStreamWriter.Create) creates
  a stream: the host writes to the writer, and gives the readable end to the component.
- [AsFutureReader](cref:Meziantou.Framework.WasmEngine.Components.ComponentValue.AsFutureReader) and
  [ComponentFutureWriter.Create](cref:Meziantou.Framework.WasmEngine.Components.ComponentFutureWriter.Create) do the
  same for the futures.

Use the readers and the writers in the functions of the host, or in the operations of `RunInBackground`: their
continuations run on the thread of the store. Dispose the readers and the stream writers to drop their ends.
[ReaderDroppedToken](cref:Meziantou.Framework.WasmEngine.Components.ComponentStreamWriter.ReaderDroppedToken) is
cancelled when the component drops the readable end of a stream: an operation that produces the values (such as the
reception of a socket) stops then.

Outside of a call, [WasmComponentInstance.Run](cref:Meziantou.Framework.WasmEngine.Components.WasmComponentInstance.Run)
runs an asynchronous operation of the host on the current thread, as the thread of the store: the store runs the
components while the operation waits for them, such as the tasks that write the body of a response after a call
returned. The operation can call the functions of the components and use their streams.

## Threads

The threads of a component (`thread.new-indirect`, `thread.yield`, `thread.suspend`, ...) and the tasks of its
asynchronous functions run on dedicated threads of the operating system, one at a time: the engine passes the control
from one to the other as the component model specifies. A synchronous call runs on the thread of its caller.

These threads are not available on the browser (WebAssembly): there, the asynchronous functions and the threads of the
components throw a `PlatformNotSupportedException`.
