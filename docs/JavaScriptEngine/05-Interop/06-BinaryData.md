---
title: Binary data
description: Share a .NET buffer with the scripts without copying it, read the bytes of their typed arrays, and share memory between engines.
---

## Share a buffer with the scripts

`realm.CreateArrayBuffer(Memory<byte>, detachKey)` creates an `ArrayBuffer` whose bytes are the memory you give: the
script and .NET read and write the same bytes. `realm.CreateBufferView` creates a typed array or a `DataView` on it:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_Buffers;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();
var pixels = new byte[] { 10, 20, 30, 40 };
var detachKey = "pixels";
JsObject buffer;
using (engine.Enter())
{
    // The ArrayBuffer uses the memory of the array: nothing is copied. With a detach key, the scripts cannot
    // transfer or detach it
    buffer = realm.CreateArrayBuffer(pixels.AsMemory(), detachKey);
    realm.GlobalObject.SetProperty("pixels", realm.CreateBufferView(JsBufferKind.Uint8Array, buffer, byteOffset: 0, length: pixels.Length));
}

realm.Execute("for (let i = 0; i < pixels.length; i++) pixels[i] = 255 - pixels[i];");
Assert.Equal([245, 235, 225, 215], pixels);

// Before the memory is reused, detach the buffer: the scripts then see an empty buffer
JsBinaryData.DetachArrayBuffer(buffer, detachKey);
Assert.Equal(0, realm.Execute("pixels.length").AsNumber());

// Read the bytes of a buffer or a view created by a script
var result = realm.Execute("Uint8Array.from([1, 2, 3])");
Assert.True(JsBinaryData.TryGetBufferSource(result, out var kind, out var isShared, out var bytes));
Assert.Equal(JsBufferKind.Uint8Array, kind);
Assert.False(isShared);
Assert.Equal([1, 2, 3], bytes.ToArray());
```
<!-- endref -->

- With a detach key other than `undefined`, the scripts cannot detach or transfer the buffer
  (`buffer.transfer()` throws). `JsBinaryData.DetachArrayBuffer(buffer, key)` detaches it with the key: do it before
  the memory is returned to a pool or reused, so that the scripts can no longer reach it.
- `realm.CreateArrayBuffer(ReadOnlySpan<byte>)` creates a buffer with a copy of the bytes.
- [CreateResizableArrayBuffer](cref:Meziantou.Framework.JavaScriptEngine.JsRealm.CreateResizableArrayBuffer) creates a resizable buffer on your memory: `buffer.resize()` calls your callback
  with the new byte length. The callback checks the length (throw a `RangeError` with
  [CreateException](cref:Meziantou.Framework.JavaScriptEngine.JsRealm.CreateException) to refuse it) and gives the new memory to
  [JsBinaryData.SetArrayBufferMemory](cref:Meziantou.Framework.JavaScriptEngine.JsBinaryData.SetArrayBufferMemory), which you can also call when the memory changes on the .NET side.
- [CreateBufferView](cref:Meziantou.Framework.JavaScriptEngine.JsRealm.CreateBufferView) takes a [JsBufferKind](cref:Meziantou.Framework.JavaScriptEngine.JsBufferKind): [Uint8Array](cref:Meziantou.Framework.JavaScriptEngine.JsBufferKind.Uint8Array), [Int32Array](cref:Meziantou.Framework.JavaScriptEngine.JsBufferKind.Int32Array), [Float64Array](cref:Meziantou.Framework.JavaScriptEngine.JsBufferKind.Float64Array), `DataView`... The length is a
  number of elements (of bytes for a `DataView`). The offset of a typed array must be a multiple of the size of its
  elements, and the view must fit in the buffer: otherwise it throws an `ArgumentException` or an
  `ArgumentOutOfRangeException`.

## Read the bytes of a script

[JsBinaryData.TryGetBufferSource](cref:Meziantou.Framework.JavaScriptEngine.JsBinaryData.TryGetBufferSource) gives the bytes of an `ArrayBuffer`, a `SharedArrayBuffer`, a typed array or a
`DataView`, with its kind and whether the memory is shared. It runs no code of the script and copies nothing: the
`Memory<byte>` is a view of the buffer, and your writes are visible to the scripts. It is empty when the buffer is
detached, and the method returns false for any other value. The bytes of a typed array are its elements, in the byte
order of the machine.

[JsBinaryData.IsResizable](cref:Meziantou.Framework.JavaScriptEngine.JsBinaryData.IsResizable) tells whether the memory can change size: a resizable `ArrayBuffer`, a
growable `SharedArrayBuffer`, or a view of one. An API that keeps the `Memory<byte>` can reject these buffers, as
the Web APIs do unless they allow them.

## Share memory between engines

A `SharedArrayBuffer` is the memory that several engines, each on its own thread, can use at the same time, with
`Atomics` to synchronize them. [JsBinaryData.GetSharedDataBlock](cref:Meziantou.Framework.JavaScriptEngine.JsBinaryData.GetSharedDataBlock) returns the memory of a `SharedArrayBuffer` as a
[JsSharedDataBlock](cref:Meziantou.Framework.JavaScriptEngine.JsSharedDataBlock), and `realm.CreateSharedArrayBuffer(block)` creates a `SharedArrayBuffer` on the same memory in
another engine. [JsSharedDataBlock.TryCreate](cref:Meziantou.Framework.JavaScriptEngine.JsSharedDataBlock.TryCreate) creates a block from .NET, and [GetSpan](cref:Meziantou.Framework.JavaScriptEngine.JsSharedDataBlock.GetSpan) and [GetMemory](cref:Meziantou.Framework.JavaScriptEngine.JsSharedDataBlock.GetMemory) read and write
it from .NET. [GetReference](cref:Meziantou.Framework.JavaScriptEngine.JsSharedDataBlock.GetReference), given a byte
index and a byte count, returns a reference to that range of bytes, to access a value with `Interlocked` or `Volatile`
as `Atomics` does: it checks that the whole range is in the block, and the first byte of the block is 16-byte aligned, so a value at an
index that is a multiple of its size is aligned.

`realm.CreateSharedArrayBuffer(block, byteLength)` creates a fixed-length `SharedArrayBuffer` on the first bytes of a
growable block: it keeps its length when the block grows. [CreateGrowableSharedArrayBuffer](cref:Meziantou.Framework.JavaScriptEngine.JsRealm.CreateGrowableSharedArrayBuffer) creates a growable
`SharedArrayBuffer` whose `grow()` calls your callback, which grows the block with
[TryGrow](cref:Meziantou.Framework.JavaScriptEngine.JsSharedDataBlock.TryGrow) or throws.
[TryGrowBy](cref:Meziantou.Framework.JavaScriptEngine.JsSharedDataBlock.TryGrowBy) adds a number of bytes to the block
and returns its previous length in one atomic step: when several engines grow the same block at the same time, each one
adds its own bytes, as the `grow(delta)` of a shared `WebAssembly.Memory` does.

### Share memory with another runtime

[JsSharedDataBlock.Address](cref:Meziantou.Framework.JavaScriptEngine.JsSharedDataBlock.Address) is the address of the first byte of a block, which never changes: the block is
allocated for its maximum byte length and pinned, so native code can read and write it while the block is alive (e.g.
the shared memory of a WebAssembly runtime).

When that runtime has its own waiters, set [Waiters](cref:Meziantou.Framework.JavaScriptEngine.JsSharedDataBlock.Waiters) before the scripts get a buffer of the block:
`Atomics.wait`, `Atomics.waitAsync` and `Atomics.notify` then call your [JsSharedDataBlockWaiters](cref:Meziantou.Framework.JavaScriptEngine.JsSharedDataBlockWaiters) instead of the
waiter lists of the engines, so that the waiters of both runtimes notify each other:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/Builtins/BinaryDataTests.cs;region=JsSharedDataBlock_WaitersClass;format=md-fenced-code-block;dedent=true -->
```csharp
// The waiters of another runtime that shares the memory of a block: here, they record the calls and notify every waiter at once
internal sealed class RecordingWaiters : JsSharedDataBlockWaiters
{
    public List<string> Calls { get; } = [];

    public override int Notify(int byteIndex, int count)
    {
        Calls.Add($"notify {byteIndex} {count}");
        return Math.Min(count, 2);
    }

    public override bool Wait(int byteIndex, long expectedValue, int size, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        Calls.Add($"wait {byteIndex} {expectedValue} {size} {(timeout is { } duration ? duration.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-")}");
        return true;
    }

    public override bool WaitAsync(int byteIndex, long expectedValue, int size, TimeSpan? timeout, Action<bool> completed, CancellationToken cancellationToken)
    {
        Calls.Add($"waitAsync {byteIndex} {expectedValue} {size} {timeout?.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        if (RefuseAsyncWaits)
            return false; // Atomics.waitAsync throws a RangeError

        // The token is canceled when the engine is disposed
        LastAsyncWaitToken = cancellationToken;
        completed(true);
        return true;
    }

    public bool RefuseAsyncWaits { get; set; }

    public CancellationToken LastAsyncWaitToken { get; private set; }
}
```
<!-- endref -->

`WaitAsync` returns `false` when your runtime cannot add more waiters: `Atomics.waitAsync` then throws a `RangeError`. Its
cancellation token is canceled when the engine is disposed or when the cancellation token of its options is canceled:
stop the wait then.

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/Builtins/BinaryDataTests.cs;region=JsSharedDataBlock_Waiters;format=md-fenced-code-block;dedent=true -->
```csharp
// Atomics.wait, Atomics.waitAsync and Atomics.notify use the waiters of the runtime that shares the memory
var block = JsSharedDataBlock.TryCreate(16, maxByteLength: 32)!;
var waiters = new RecordingWaiters();
block.Waiters = waiters;

using var engine = new JsEngine();
var realm = engine.CreateRealm();
using (engine.Enter())
{
    realm.GlobalObject.SetProperty("sab", realm.CreateSharedArrayBuffer(block));
}

realm.Execute("""
    const i32 = new Int32Array(sab);
    i32[1] = 5;
    var results = [Atomics.notify(i32, 1), Atomics.notify(i32, 2, 3), Atomics.wait(i32, 1, 5, 20), Atomics.wait(i32, 1, 6), Atomics.wait(i32, 1, 5, 0), Atomics.wait(new BigInt64Array(sab), 1, 0n)];
    const { async, value } = Atomics.waitAsync(i32, 3, 0, 100);
    value.then(result => results.push(async, result));
    """);
engine.RunJobs();

Assert.Equal("2,2,ok,not-equal,timed-out,ok,true,ok", realm.Evaluate("results.join()").ToString());
Assert.Equal(["notify 4 2147483647", "notify 8 3", "wait 4 5 4 20", "wait 8 0 8 -", "waitAsync 12 0 4 100"], waiters.Calls);
```
<!-- endref -->

- The engine checks the value of the location before it calls `Wait` or `WaitAsync`: a value that differs returns
  `"not-equal"`, and a timeout of 0 returns `"timed-out"`, without a call. When another agent changes the value before
  your waiter is added, complete the wait as a notification.
- `Wait` blocks the thread of the script. Its timeout is at most the time left to the running entry, and the
  cancellation token is canceled with the tokens of the engine and of the execution scopes: stop waiting when it is.
- `WaitAsync` returns once the waiter is added (a notification that follows must reach it), and calls `completed` once,
  from any thread: the promise of `Atomics.waitAsync` is resolved on the thread of the engine.
