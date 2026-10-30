---
title: IBufferWriter
description: Use the stream as an IBufferWriter<byte>, for Utf8JsonWriter and other serializers, and where the data goes.
---

`PooledMemoryStream` implements `IBufferWriter<byte>`, the interface that `Utf8JsonWriter`, `System.IO.Pipelines` and
many serializers write to. Through it, they write directly into the pooled blocks of the stream, without an
intermediate buffer.

## Serialize into the stream

The interface is implemented explicitly: assign the stream to an `IBufferWriter<byte>` variable, or pass it to a method
that takes one.

<!-- ref:../../tests/Meziantou.Framework.PooledMemoryStream.Tests/PooledMemoryStreamTests.cs;region=PooledMemoryStream_BufferWriter;format=md-fenced-code-block;dedent=true -->
```csharp
using var stream = new PooledMemoryStream();

// PooledMemoryStream implements IBufferWriter<byte> (explicit), so it can be passed where one is expected.
IBufferWriter<byte> writer = stream;
await using (var json = new Utf8JsonWriter(writer))
{
    json.WriteStartObject();
    json.WriteString("message", "hello");
    json.WriteEndObject();
}

var bytes = stream.ToArray();
Assert.Equal("""{"message":"hello"}"""u8.ToArray(), bytes);
```
<!-- endref -->

`IBufferWriter<T>` is in the `System.Buffers` namespace, and `Utf8JsonWriter` in `System.Text.Json`.

## Where the data goes

Data written through `IBufferWriter<byte>` is always appended at the end of the stream, whatever the position, and
`Advance` moves the position to the new end:

<!-- ref:../../tests/Meziantou.Framework.PooledMemoryStream.Tests/PooledMemoryStreamTests.cs;region=PooledMemoryStream_BufferWriterAppends;format=md-fenced-code-block;dedent=true -->
```csharp
using var stream = new PooledMemoryStream();
stream.Write("Hello"u8);
stream.Position = 0;

// The IBufferWriter<byte> members always write at the end of the stream, whatever the position
IBufferWriter<byte> writer = stream;
var span = writer.GetSpan(sizeHint: 7);
", World"u8.CopyTo(span);
writer.Advance(7);

Assert.Equal("Hello, World"u8.ToArray(), stream.ToArray());
Assert.Equal(12, stream.Position); // Advance moves the position to the new end
```
<!-- endref -->

- `GetSpan(sizeHint)` and `GetMemory(sizeHint)` return a region of at least `sizeHint` bytes at the end of the stream.
  The region is uninitialized, as usual for a buffer writer: it can contain data left by another stream.
- `Advance(count)` adds the first `count` bytes of the region to the stream. Advancing past the region throws an
  `InvalidOperationException`.
- After `Advance`, ask for a new region to write more: the previous one is no longer valid.

To read what was written, set `Position` to 0, or use `ToArray` or `WriteTo`, which ignore the position.
