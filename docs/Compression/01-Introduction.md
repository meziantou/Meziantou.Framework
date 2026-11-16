---
title: Introduction
description: What Meziantou.Framework.Compression reads and writes, and how it relates to System.IO.Compression.
---

`Meziantou.Framework.Compression` reads and writes the archive formats and the compressed streams that .NET does not
support, with an API that looks like [ZipArchive](https://learn.microsoft.com/dotnet/api/system.io.compression.ziparchive)
and [ZipFile](https://learn.microsoft.com/dotnet/api/system.io.compression.zipfile). It is written in C# only: it needs no
native library and works with Native AOT.

## Archives

| Format | Read | Write | Type |
| --- | --- | --- | --- |
| 7z | Yes | Yes (create and update) | [SevenZipArchive](cref:Meziantou.Framework.Compression.SevenZipArchive), [SevenZipFile](cref:Meziantou.Framework.Compression.SevenZipFile) |
| RAR (RAR5, RAR 2.0 to 4.x) | Yes | No | [RarArchive](cref:Meziantou.Framework.Compression.RarArchive), [RarFile](cref:Meziantou.Framework.Compression.RarFile) |
| zip | Yes | Yes (create and update) | [ZipArchive](cref:Meziantou.Framework.Compression.ZipArchive), [ZipFile](cref:Meziantou.Framework.Compression.ZipFile) |
| tar, .tar.gz, .tar.bz2, .tar.xz, .tar.lzma, .tar.lz, .tar.zst, .tar.Z | Yes | No: use `System.Formats.Tar` | [ArchiveFile](cref:Meziantou.Framework.Compression.ArchiveFile), [ArchiveReader](cref:Meziantou.Framework.Compression.ArchiveReader) |

- 7z: LZMA, LZMA2, PPMd, BZip2, Deflate, Deflate64 and Copy, the BCJ, BCJ2 and Delta filters, AES-256 encryption of the
  content and of the names, solid blocks and multi-volume archives (`.7z.001`, `.7z.002`, ...).
- RAR: the compression of RAR5 (including the larger dictionaries of RAR 7) and of RAR 2.9/4.x (LZ, PPMd and the standard
  filters), AES encryption of the content and of the headers, solid and multi-volume archives, self-extracting archives,
  symbolic and hard links.
- zip: Stored, Deflate, Deflate64, BZip2, LZMA, PPMd, XZ, Zstandard, and the old Shrink, Reduce and Implode
  methods; ZipCrypto and WinZip AES encryption; Zip64; split archives (`.z01`, ..., `.zip`, and `.zip.001`, ...).
  Writing supports Stored, Deflate, BZip2, LZMA, PPMd, XZ and Zstandard (on .NET 11), ZipCrypto and WinZip AES
  encryption, and Zip64, to streams that do not need to be seekable: see [Zip archives](05-Zip.md).

[ArchiveFile](cref:Meziantou.Framework.Compression.ArchiveFile) opens an archive of any of these formats, detected from
its content, and [ArchiveReader](cref:Meziantou.Framework.Compression.ArchiveReader) reads one forward only, from a stream
that does not need to be seekable: see [Getting started](02-GettingStarted.md).

## Compressed streams

| Format | Compress | Decompress | Type |
| --- | --- | --- | --- |
| XZ (.xz) | Yes | Yes | [XzStream](cref:Meziantou.Framework.Compression.XzStream) |
| LZMA alone (.lzma) | Yes | Yes | [LzmaStream](cref:Meziantou.Framework.Compression.LzmaStream) |
| lzip (.lz) | Yes | Yes | [LZipStream](cref:Meziantou.Framework.Compression.LZipStream) |
| bzip2 (.bz2) | Yes | Yes | [BZip2Stream](cref:Meziantou.Framework.Compression.BZip2Stream) |
| Unix compress (.Z) | No | Yes | [UnixCompressStream](cref:Meziantou.Framework.Compression.UnixCompressStream) |

They work like `GZipStream`: see [Compressed streams](06-Streams.md).
