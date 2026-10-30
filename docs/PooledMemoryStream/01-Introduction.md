---
title: Introduction
description: A MemoryStream whose storage is a chain of pooled byte arrays, for code that creates many temporary streams.
---

`Meziantou.Framework.PooledMemoryStream` provides `PooledMemoryStream`: a `MemoryStream` whose storage is a chain of byte
arrays rented from a pool that every instance of the process shares. Use it where an application creates many temporary
in-memory streams, such as to buffer HTTP bodies, serialize messages or build files before sending them.

A `MemoryStream` stores its data in one array. When the data outgrows it, it allocates an array twice as large and
copies the data, and the large arrays it leaves behind put pressure on the garbage collector. A `PooledMemoryStream`
appends a new pooled block to its chain instead: growing never copies the data, and the blocks go back to the pool
when the stream is disposed, ready for the next stream.

## What you can do with it

- **Replace a `MemoryStream`**: `PooledMemoryStream` derives from `MemoryStream`, so it works with every API that takes
  a `Stream` or a `MemoryStream`. See [Getting started](02-GettingStarted.md).
- **Write with `IBufferWriter<byte>`**, the interface of `Utf8JsonWriter` and other serializers. See
  [IBufferWriter](03-BufferWriter.md).
- **Configure the pool**: the sizes of the blocks, how much memory the pool keeps, and whether blocks are cleared. See
  [Options](04-Options.md).
- **Access the underlying arrays** with `GetBuffer` and `TryGetBuffer`, knowing how long they stay valid. See
  [Buffers and lifetime](05-BuffersAndLifetime.md).

## Principles

- **Discrete block sizes.** Blocks are only rented in a few configured sizes (4 KiB, 64 KiB and 1 MiB by default), so
  the pool keeps a few kinds of arrays that every stream can reuse.
- **Owned buffers.** Every array the stream exposes belongs to the stream and goes back to the pool when it is
  disposed.
- **One shared pool.** The pool is process-wide and thread-safe. A stream instance is not thread-safe, like a
  `MemoryStream`.

## Namespace

The types are in the [Meziantou.Framework](cref:Meziantou.Framework.PooledMemoryStream!N:Meziantou.Framework) namespace:

| Type | Contains |
| --- | --- |
| `PooledMemoryStream` | The stream, its `MemoryStream` members and its `IBufferWriter<byte>` implementation |
| `PooledMemoryStreamOptions` | The block sizes and the pooling settings |

## Next steps

- [Getting started](02-GettingStarted.md): install the package and write your first stream.
- [Buffers and lifetime](05-BuffersAndLifetime.md): what not to do after the stream is disposed.
