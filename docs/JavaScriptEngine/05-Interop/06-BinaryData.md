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

## Share memory between engines

A `SharedArrayBuffer` is the memory that several engines, each on its own thread, can use at the same time, with
`Atomics` to synchronize them. [JsBinaryData.GetSharedDataBlock](cref:Meziantou.Framework.JavaScriptEngine.JsBinaryData.GetSharedDataBlock) returns the memory of a `SharedArrayBuffer` as a
[JsSharedDataBlock](cref:Meziantou.Framework.JavaScriptEngine.JsSharedDataBlock), and `realm.CreateSharedArrayBuffer(block)` creates a `SharedArrayBuffer` on the same memory in
another engine. [JsSharedDataBlock.TryCreate](cref:Meziantou.Framework.JavaScriptEngine.JsSharedDataBlock.TryCreate) creates a block from .NET, and [GetSpan](cref:Meziantou.Framework.JavaScriptEngine.JsSharedDataBlock.GetSpan) and [GetMemory](cref:Meziantou.Framework.JavaScriptEngine.JsSharedDataBlock.GetMemory) read and write
it from .NET.
