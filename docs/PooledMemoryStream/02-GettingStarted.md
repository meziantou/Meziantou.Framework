---
title: Getting started
description: Install the package, write and read a pooled stream, copy its contents and dispose it.
---

## Install the package

Add the `Meziantou.Framework.PooledMemoryStream` package to a project:

<InstallNuGetPackage Id="Meziantou.Framework.PooledMemoryStream" />

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework;`.

## Write and read a stream

Use a [PooledMemoryStream](cref:Meziantou.Framework.PooledMemoryStream) like a `MemoryStream`, and dispose it to give its blocks back to the pool:

<!-- ref:../../tests/Meziantou.Framework.PooledMemoryStream.Tests/PooledMemoryStreamTests.cs;region=PooledMemoryStream_BasicUsage;format=md-fenced-code-block;dedent=true -->
```csharp
using var stream = new PooledMemoryStream();
stream.Write("Hello, "u8);
stream.Write("World!"u8);

stream.Position = 0;
using var reader = new StreamReader(stream);
Assert.Equal("Hello, World!", reader.ReadToEnd());

// The pooled buffers are returned to the shared pool when the stream is disposed.
```
<!-- endref -->

`Read`, `Write`, `Seek`, `Position`, `Length`, `SetLength` and their asynchronous versions behave as they do on a
`MemoryStream`. Disposing the `StreamReader` here disposes the stream too.

Always dispose a [PooledMemoryStream](cref:Meziantou.Framework.PooledMemoryStream), with `using` or `await using`. A stream that is not disposed never returns its
blocks: the garbage collector reclaims them, but the next streams have to allocate new ones.

## Copy the contents

<!-- ref:../../tests/Meziantou.Framework.PooledMemoryStream.Tests/PooledMemoryStreamTests.cs;region=PooledMemoryStream_CopyContents;format=md-fenced-code-block;dedent=true -->
```csharp
using var stream = new PooledMemoryStream(initialCapacity: 64 * 1024); // reserves the capacity up front
stream.Write("Hello, World!"u8);

// WriteTo copies the whole stream, whatever the position
using var file = new MemoryStream();
stream.WriteTo(file);
Assert.Equal("Hello, World!"u8.ToArray(), file.ToArray());

// CopyTo copies from the current position, like with any stream
stream.Position = 7;
using var rest = new MemoryStream();
stream.CopyTo(rest);
Assert.Equal("World!"u8.ToArray(), rest.ToArray());

// ToArray returns a new array of Length bytes, which you can keep after the stream is disposed
byte[] copy = stream.ToArray();
Assert.HasCount(13, copy);
```
<!-- endref -->

- `new PooledMemoryStream(initialCapacity)` reserves capacity up front, when you know the approximate size of the data.
- `WriteTo` copies the whole stream to another stream, whatever the position. `CopyTo` and `CopyToAsync` copy from the
  current position to the end, like with any stream.
- `ToArray` returns a new array of `Length` bytes. It is an ordinary array: you can keep it after the stream is
  disposed.

To read the contents without copying them, see [Buffers and lifetime](05-BuffersAndLifetime.md).

## Next steps

- [IBufferWriter](03-BufferWriter.md): serialize JSON directly into the stream.
- [Options](04-Options.md): choose the block sizes.
