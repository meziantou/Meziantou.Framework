---
title: Zip archives
description: Read the zip archives that System.IO.Compression cannot read, compressed with BZip2, LZMA, PPMd or XZ, encrypted, or split.
---

## Read an archive

`System.IO.Compression.ZipArchive` reads the Stored, Deflate and Deflate64 methods.
[ReadOnlyZipArchive](cref:Meziantou.Framework.Compression.ReadOnlyZipArchive) also reads the archives that 7-Zip, WinZip
and the old PKZIP write with other methods, on every version of .NET the library supports:

<!-- ref:../../tests/Meziantou.Framework.Compression.Tests/ReadOnlyZipArchiveTests.cs;region=ReadOnlyZipArchive_Read;format=md-fenced-code-block;dedent=true -->
```csharp
// A zip archive written by System.IO.Compression.ZipArchive
using var stream = new MemoryStream();
using (var writer = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
{
    using var entryStream = writer.CreateEntry("docs/readme.txt").Open();
    entryStream.Write("Hello, world!"u8);
}

stream.Position = 0;
using var archive = new ReadOnlyZipArchive(stream);
Assert.HasCount(1, archive.Entries);

var entry = archive.GetEntry("docs/readme.txt")!;
Assert.Equal(ZipCompressionMethod.Deflate, entry.CompressionMethod);
Assert.Equal(13L, entry.Length);

// The stream checks the length and the CRC-32 of the content
using var reader = new StreamReader(entry.Open());
Assert.Equal("Hello, world!", reader.ReadToEnd());
```
<!-- endref -->

- Methods: Stored, Deflate, Deflate64, BZip2, LZMA, PPMd, XZ, Zstandard (on .NET 11 only), Shrink, Reduce and Implode. The
  method of an entry is [CompressionMethod](cref:Meziantou.Framework.Compression.ReadOnlyZipArchiveEntry.CompressionMethod).
- Zip64 archives, and the archives that follow an executable (self-extracting archives).
- [ReadOnlyZipArchive.Open](cref:Meziantou.Framework.Compression.ReadOnlyZipArchive.Open) finds the volumes of a split
  archive: `name.z01`, `name.z02`, ..., `name.zip` (Info-ZIP, WinZip), or `name.zip.001`, `name.zip.002`, ... (7-Zip).
- The names are UTF-8 when the archive says so, or decoded with
  [ArchiveReaderOptions.EntryNameEncoding](cref:Meziantou.Framework.Compression.ArchiveReaderOptions.EntryNameEncoding),
  by default with code page 437, the encoding of the old zip tools.

## Encrypted archives

[ArchiveReaderOptions.Password](cref:Meziantou.Framework.Compression.ArchiveReaderOptions.Password) decrypts the entries
encrypted with WinZip AES (128, 192 or 256 bits) or with the traditional PKWARE encryption (ZipCrypto). The method of an
entry is [EncryptionMethod](cref:Meziantou.Framework.Compression.ReadOnlyZipArchiveEntry.EncryptionMethod):

<!-- ref:../../tests/Meziantou.Framework.Compression.Tests/ReadOnlyZipArchiveTests.cs;region=ReadOnlyZipArchive_Password;format=md-fenced-code-block;dedent=true -->
```csharp
// An archive created by 7-Zip: 7zz a -tzip -mem=AES256 -ppassword aes256.zip ...
var path = Path.Combine(AppContext.BaseDirectory, "TestData", "Zip", "aes256.zip");
using var archive = ReadOnlyZipArchive.Open(path, new ArchiveReaderOptions { Password = "password" });

var entry = archive.GetEntry("hello.txt")!;
Assert.True(entry.IsEncrypted);
Assert.Equal(ZipEncryption.Aes256, entry.EncryptionMethod);

using var reader = new StreamReader(entry.Open());
Assert.Equal("Hello, world!\n", reader.ReadToEnd());
```
<!-- endref -->

The PKWARE strong encryption and the encryption of the central directory throw a `NotSupportedException`.

## Read forward only

[ArchiveReader.Create](cref:Meziantou.Framework.Compression.ArchiveReader.Create) reads a zip archive from a stream that is
not seekable, from its local headers (see [Getting started](02-GettingStarted.md)). The comments, the attributes and the
symbolic links are stored in the central directory, at the end of the archive, so this reader does not see them.
