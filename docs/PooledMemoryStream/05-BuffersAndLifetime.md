---
title: Buffers and lifetime
description: Read the contents without a copy with GetBuffer and TryGetBuffer, how long the arrays stay valid, and the size limits.
---

## Get the underlying array

`TryGetBuffer` and `GetBuffer` return the contents of the stream as a single array, owned by the stream:

<!-- ref:../../tests/Meziantou.Framework.PooledMemoryStream.Tests/PooledMemoryStreamTests.cs;region=PooledMemoryStream_GetBuffer;format=md-fenced-code-block;dedent=true -->
```csharp
using var stream = new PooledMemoryStream();
stream.Write("Hello"u8);

// The contents as one array, without a copy when they fit in one pooled block
Assert.True(stream.TryGetBuffer(out var buffer));
Assert.Equal("Hello"u8.ToArray(), buffer.ToArray()); // the segment is limited to Length

// GetBuffer returns the whole array: only the first Length bytes are the contents
byte[] array = stream.GetBuffer();
Assert.Equal("Hello"u8.ToArray(), array.AsSpan(0, (int)stream.Length).ToArray());
```
<!-- endref -->

- When the contents fit in one block, the methods return that block without a copy. Otherwise they consolidate the
  blocks into one new pooled array, which becomes the storage of the stream.
- `TryGetBuffer` always returns `true`, with an `ArraySegment<byte>` limited to `Length`.
- `GetBuffer` returns the whole array, which is usually larger than the contents: only the first `Length` bytes are
  meaningful. The rest is zeroed, so it never shows data of another stream.

## How long the array stays valid

The array is owned by the stream:

- **Until the stream is disposed.** The stream never returns the array to the pool before it is disposed, so no other
  stream can rent it while you hold it. **Do not use it after the stream is disposed**: it may then belong to another
  stream.
- **While the stream keeps it as storage.** As with a `MemoryStream`, the array reflects the writes that follow, and
  shrinking the stream with `SetLength` or `Capacity` keeps it, so later writes overwrite it. A later `GetBuffer` or
  `TryGetBuffer` call that has to consolidate several blocks replaces it: the old array stays valid until the stream is
  disposed, but no longer reflects the contents.

The same rule applies to the spans and memory returned by the [`IBufferWriter<byte>` members](03-BufferWriter.md). To
keep the data after the stream is disposed, copy it with `ToArray`.

## Size limits

The storage is a chain of blocks, so a stream is not limited by `Array.MaxLength`: it can hold as much data as memory
allows. The members that must produce a single array, `ToArray`, `GetBuffer` and `TryGetBuffer`, are still limited by
it, and throw an `InvalidOperationException` on a longer stream. `Capacity` is an `int`, inherited from `MemoryStream`:
for a larger stream, it returns `int.MaxValue`.

## Threads

A [PooledMemoryStream](cref:Meziantou.Framework.PooledMemoryStream) is not thread-safe, like a `MemoryStream`: use an instance from one thread at a time. The pool
the streams share is thread-safe, so streams used by different threads need no synchronization.
