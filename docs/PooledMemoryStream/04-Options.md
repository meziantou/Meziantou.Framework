---
title: Options
description: Choose the block sizes, how much memory the pool keeps, and whether returned blocks are cleared.
---

[PooledMemoryStreamOptions](cref:Meziantou.Framework.PooledMemoryStreamOptions) configures the sizes of the blocks a stream rents and how the pool keeps them. Streams
created without options use [PooledMemoryStreamOptions.Default](cref:Meziantou.Framework.PooledMemoryStreamOptions.Default).

## Configure the options

<!-- ref:../../tests/Meziantou.Framework.PooledMemoryStream.Tests/PooledMemoryStreamTests.cs;region=PooledMemoryStream_Options;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new PooledMemoryStreamOptions
{
    // Any number of strictly ascending tiers (bytes). Small streams use the smallest size and
    // grow through the larger ones; buffers bigger than the largest tier round up to a multiple of it.
    BufferSizes = [4 * 1024, 128 * 1024, 1024 * 1024, 10 * 1024 * 1024], // 4 KiB / 128 KiB / 1 MiB / 10 MiB
    MaxRetainedBytesPerBucket = 64L * 1024 * 1024,
    ClearOnReturn = false,
};

using var stream = new PooledMemoryStream(options);
```
<!-- endref -->

| Property | Default | Meaning |
| --- | --- | --- |
| [BufferSizes](cref:Meziantou.Framework.PooledMemoryStreamOptions.BufferSizes) | 4 KiB, 64 KiB, 1 MiB | The sizes of the blocks, in bytes, in strictly ascending order |
| [MaxRetainedBytesPerBucket](cref:Meziantou.Framework.PooledMemoryStreamOptions.MaxRetainedBytesPerBucket) | 16 MiB | The number of bytes the pool keeps for each block size |
| [ClearOnReturn](cref:Meziantou.Framework.PooledMemoryStreamOptions.ClearOnReturn) | `false` | Whether blocks are zeroed when they go back to the pool |

## Block sizes

A stream only rents blocks of the sizes listed in [BufferSizes](cref:Meziantou.Framework.PooledMemoryStreamOptions.BufferSizes). A small stream uses the smallest size, and a growing
stream moves to the larger sizes, so a large stream has a bounded number of blocks. When a single contiguous buffer
larger than the largest size is needed, for instance for `GetBuffer` on a large stream, its size is rounded up to a
multiple of the largest size.

- [BufferSizes](cref:Meziantou.Framework.PooledMemoryStreamOptions.BufferSizes) must contain at least one size, every size must be greater than 0, and the sizes must be in strictly
  ascending order. Otherwise, setting the property throws.
- Choose the smallest size from the size of your typical small stream, and the largest from your typical large stream.
  Fewer sizes make the pooled arrays more reusable between streams.

## Pool size

[MaxRetainedBytesPerBucket](cref:Meziantou.Framework.PooledMemoryStreamOptions.MaxRetainedBytesPerBucket) caps the memory the pool keeps for each block size. A block returned when its size already
reaches the cap is left to the garbage collector. Raise it when many streams are alive at the same time, lower it to
keep less memory when streams are rare. The pool is shared by every stream of the process, whatever their options.

## Clear the blocks

The pool is shared, so a block can be rented by another stream after yours. With [ClearOnReturn](cref:Meziantou.Framework.PooledMemoryStreamOptions.ClearOnReturn), the blocks your
stream returns are zeroed first, so the next stream cannot see your data. This is slower, and only affects the blocks
this stream returns, not the ones it rents. Enable it for streams that hold sensitive data.

The members of the stream never expose data written by another stream, whatever this setting: reads stop at `Length`,
and `GetBuffer` zeroes the bytes past `Length`. The one exception is the uninitialized region returned by the
[`IBufferWriter<byte>` members](03-BufferWriter.md#where-the-data-goes).

## Share the options

An options instance is frozen the first time it is used to create a stream: setting a property afterwards throws an
`InvalidOperationException`. [PooledMemoryStreamOptions.Default](cref:Meziantou.Framework.PooledMemoryStreamOptions.Default) is always frozen. Configure every property first, then
share the instance between your streams:

<!-- ref:../../tests/Meziantou.Framework.PooledMemoryStream.Tests/PooledMemoryStreamTests.cs;region=PooledMemoryStream_SharedOptions;format=md-fenced-code-block;dedent=true -->
```csharp
// Create the options once, and share them between the streams
var options = new PooledMemoryStreamOptions
{
    BufferSizes = [16 * 1024, 256 * 1024],
    ClearOnReturn = true, // zero the buffers before another stream can rent them
};

using (var first = new PooledMemoryStream(options))
{
    first.Write("secret"u8);
}

using var second = new PooledMemoryStream(options);

// Creating a stream froze the options: they cannot change any more
Assert.True(options.IsFrozen);
Assert.Throws<InvalidOperationException>(() => options.ClearOnReturn = false);
```
<!-- endref -->

[IsFrozen](cref:Meziantou.Framework.PooledMemoryStreamOptions.IsFrozen) tells whether an instance can still be changed.
