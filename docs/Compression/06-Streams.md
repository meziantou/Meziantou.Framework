---
title: Compressed streams
description: Compress and decompress XZ, LZMA, lzip and bzip2 data, and decompress Unix compress data, like GZipStream.
---

[XzStream](cref:Meziantou.Framework.Compression.XzStream),
[LzmaStream](cref:Meziantou.Framework.Compression.LzmaStream),
[LZipStream](cref:Meziantou.Framework.Compression.LZipStream) and
[BZip2Stream](cref:Meziantou.Framework.Compression.BZip2Stream) work like `GZipStream`: create them with
`CompressionMode.Decompress` to read, or with a `CompressionLevel` or their options to write, and dispose them to finish
the compressed data.

## XZ

<!-- ref:../../tests/Meziantou.Framework.Compression.Tests/XzStreamTests.cs;region=XzStream_RoundTrip;format=md-fenced-code-block;dedent=true -->
```csharp
var data = "Hello, XZ! Hello, XZ! Hello, XZ!"u8.ToArray();

using var compressed = new MemoryStream();
using (var xz = new XzStream(compressed, new XzCompressionOptions { Preset = 9, Check = XzCheck.Sha256 }, leaveOpen: true))
{
    xz.Write(data);
}

compressed.Position = 0;
using var decompress = new XzStream(compressed, CompressionMode.Decompress);
using var result = new MemoryStream();
decompress.CopyTo(result);
Assert.Equal(data, result.ToArray());
```
<!-- endref -->

[XzCompressionOptions](cref:Meziantou.Framework.Compression.XzCompressionOptions) sets the preset (0 to 9, as `xz -0` to
`xz -9`), the dictionary size, the integrity check
([XzCheck](cref:Meziantou.Framework.Compression.XzCheck): CRC-64 by default) and the size of the blocks. Decompressing
reads the files of several streams (`cat a.xz b.xz`), checks the integrity checks, and applies the filters of the file
(x86, ARM, ARM64, RISC-V, Delta, ...).

## bzip2

<!-- ref:../../tests/Meziantou.Framework.Compression.Tests/BZip2StreamTests.cs;region=BZip2Stream_RoundTrip;format=md-fenced-code-block;dedent=true -->
```csharp
var data = "Hello, bzip2! Hello, bzip2!"u8.ToArray();

using var compressed = new MemoryStream();
using (var bzip2 = new BZip2Stream(compressed, new BZip2CompressionOptions { BlockSize = 9 }, leaveOpen: true))
{
    bzip2.Write(data);
}

compressed.Position = 0;
using var decompress = new BZip2Stream(compressed, CompressionMode.Decompress);
using var result = new MemoryStream();
decompress.CopyTo(result);
Assert.Equal(data, result.ToArray());
```
<!-- endref -->

The block size of [BZip2CompressionOptions](cref:Meziantou.Framework.Compression.BZip2CompressionOptions) is 1 to 9, as
`bzip2 -1` to `bzip2 -9`. Decompressing reads the files of several streams, such as the files of `pbzip2`.

## LZMA and lzip

[LzmaStream](cref:Meziantou.Framework.Compression.LzmaStream) reads and writes the `.lzma` files of the LZMA SDK and of
`xz --format=lzma`, and [LZipStream](cref:Meziantou.Framework.Compression.LZipStream) the `.lz` files of lzip. Their
options are [LzmaCompressionOptions](cref:Meziantou.Framework.Compression.LzmaCompressionOptions) and
[LZipCompressionOptions](cref:Meziantou.Framework.Compression.LZipCompressionOptions).

## Unix compress

[UnixCompressStream](cref:Meziantou.Framework.Compression.UnixCompressStream) decompresses the `.Z` files of the
`compress` command. It cannot compress.
