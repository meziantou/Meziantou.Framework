---
title: Zip archives
description: Read, create and update zip archives with the methods and the encryptions System.IO.Compression does not support, BZip2, LZMA, PPMd, XZ, Zstandard, WinZip AES and ZipCrypto.
---

The library has its own [ZipArchive](cref:Meziantou.Framework.Compression.ZipArchive),
[ZipArchiveEntry](cref:Meziantou.Framework.Compression.ZipArchiveEntry) and [ZipFile](cref:Meziantou.Framework.Compression.ZipFile),
with the API of `System.IO.Compression`: it reads and writes the archives that 7-Zip, WinZip and the old PKZIP write with
other methods, and the encrypted ones, on every version of .NET the library supports. The types have the names of the
ones of `System.IO.Compression`: in a file that imports both namespaces, qualify them or use an alias. The modes are the
values of `System.IO.Compression.ZipArchiveMode`.

## Read an archive

<!-- ref:../../tests/Meziantou.Framework.Compression.Tests/ZipArchiveTests.cs;region=ZipArchive_Read;format=md-fenced-code-block;dedent=true -->
```csharp
// A zip archive written by System.IO.Compression.ZipArchive
using var stream = new MemoryStream();
using (var writer = new System.IO.Compression.ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
{
    using var entryStream = writer.CreateEntry("docs/readme.txt").Open();
    entryStream.Write("Hello, world!"u8);
}

stream.Position = 0;
using var archive = new ZipArchive(stream);
Assert.HasCount(1, archive.Entries);

var entry = archive.GetEntry("docs/readme.txt")!;
Assert.Equal(ZipCompressionMethod.Deflate, entry.CompressionMethod);
Assert.Equal(13L, entry.Length);

// The stream checks the length and the CRC-32 of the content
using var reader = new StreamReader(entry.Open());
Assert.Equal("Hello, world!", reader.ReadToEnd());
```
<!-- endref -->

- Methods: Stored, Deflate, Deflate64, BZip2, LZMA, PPMd, XZ, Zstandard, Shrink, Reduce and Implode. The method of an
  entry is [CompressionMethod](cref:Meziantou.Framework.Compression.ZipArchiveEntry.CompressionMethod).
- Zip64 archives, and the archives that follow an executable (self-extracting archives).
- [ZipFile.OpenRead](cref:Meziantou.Framework.Compression.ZipFile.OpenRead) finds the volumes of a split archive:
  `name.z01`, `name.z02`, ..., `name.zip` (Info-ZIP, WinZip), or `name.zip.001`, `name.zip.002`, ... (7-Zip).
- The names are UTF-8 when the archive says so, or decoded with
  [ArchiveReaderOptions.EntryNameEncoding](cref:Meziantou.Framework.Compression.ArchiveReaderOptions.EntryNameEncoding),
  by default with code page 437, the encoding of the old zip tools.

## Encrypted archives

[ArchiveReaderOptions.Password](cref:Meziantou.Framework.Compression.ArchiveReaderOptions.Password) decrypts the entries
encrypted with WinZip AES (128, 192 or 256 bits) or with the traditional PKWARE encryption (ZipCrypto). The method of an
entry is [EncryptionMethod](cref:Meziantou.Framework.Compression.ZipArchiveEntry.EncryptionMethod):

<!-- ref:../../tests/Meziantou.Framework.Compression.Tests/ZipArchiveTests.cs;region=ZipArchive_ReadPassword;format=md-fenced-code-block;dedent=true -->
```csharp
// An archive created by 7-Zip: 7zz a -tzip -mem=AES256 -ppassword aes256.zip ...
var path = Path.Combine(AppContext.BaseDirectory, "TestData", "Zip", "aes256.zip");
using var archive = ZipFile.OpenRead(path, new ArchiveReaderOptions { Password = "password" });

var entry = archive.GetEntry("hello.txt")!;
Assert.True(entry.IsEncrypted);
Assert.Equal(ZipEncryption.Aes256, entry.EncryptionMethod);

using var reader = new StreamReader(entry.Open());
Assert.Equal("Hello, world!\n", reader.ReadToEnd());
```
<!-- endref -->

The PKWARE strong encryption and the encryption of the central directory throw a `NotSupportedException`.

## Write an archive

When the [Mode](cref:Meziantou.Framework.Compression.ZipArchive.Mode) of the archive is `ZipArchiveMode.Create`,
[CreateEntry](cref:Meziantou.Framework.Compression.ZipArchive.CreateEntry) adds an entry, and
[Open](cref:Meziantou.Framework.Compression.ZipArchiveEntry.Open) returns the stream its content is written to:

<!-- ref:../../tests/Meziantou.Framework.Compression.Tests/ZipArchiveWriteTests.cs;region=ZipArchive_Create;format=md-fenced-code-block;dedent=true -->
```csharp
using var stream = new MemoryStream();
using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
{
    // One entry is written at a time; the stream does not need to be seekable
    var entry = archive.CreateEntry("docs/readme.txt", new ZipEntryOptions { CompressionMethod = ZipCompressionMethod.Lzma });
    using var writer = new StreamWriter(entry.Open());
    writer.Write("Hello from zip");
}

stream.Position = 0;
using (var archive = new ZipArchive(stream))
{
    var entry = archive.GetEntry("docs/readme.txt")!;
    Assert.Equal(ZipCompressionMethod.Lzma, entry.CompressionMethod);
    using var reader = new StreamReader(entry.Open());
    Assert.Equal("Hello from zip", reader.ReadToEnd());
}
```
<!-- endref -->

- One entry is written at a time: creating an entry while the content of another one is open throws an
  `InvalidOperationException`. An entry that is never opened is an empty file, and a name that ends with `/` is a directory.
- The stream does not need to be seekable: the content of an entry is followed by a data descriptor that gives its sizes,
  as Info-ZIP does when it writes to a pipe. On a seekable stream, the local header of each entry is rewritten with its
  sizes once its content is written, which more tools read.
- The central directory is written when the archive is disposed.
- [ZipCompressionOptions](cref:Meziantou.Framework.Compression.ZipCompressionOptions) sets the defaults of the entries, and
  [ZipEntryOptions](cref:Meziantou.Framework.Compression.ZipEntryOptions) overrides them for one entry:
  - [CompressionMethod](cref:Meziantou.Framework.Compression.ZipCompressionOptions.CompressionMethod): Deflate (the
    default), Stored, BZip2, LZMA, PPMd, XZ or Zstandard (on .NET 11 only, whose `System.IO.Compression` has a Zstandard
    encoder). Deflate64 and the old methods cannot be written. An empty file is always stored, as 7-Zip and
    `System.IO.Compression` write it.
  - [CompressionLevel](cref:Meziantou.Framework.Compression.ZipCompressionOptions.CompressionLevel): the level does not
    change the method; LZMA, XZ and PPMd use the presets of 7-Zip and xz for the level.
  - [Zip64](cref:Meziantou.Framework.Compression.ZipCompressionOptions.Zip64): with
    [Zip64Mode.Auto](cref:Meziantou.Framework.Compression.Zip64Mode.Auto), the zip64 fields are written only when a size,
    an offset or the number of entries needs them; [Zip64Mode.Always](cref:Meziantou.Framework.Compression.Zip64Mode.Always)
    writes them for every entry, which the forward-only readers of other tools need for an entry of more than 4 GiB
    written to a stream that is not seekable.
- The names and the comments are ASCII, or UTF-8 with the flag that says so. The time of an entry is written as an
  MS-DOS time in the local time zone, as `System.IO.Compression` writes it, and as a UTC time in the extended timestamp
  extra field. [ZipEntryOptions.UnixFileMode](cref:Meziantou.Framework.Compression.ZipEntryOptions.UnixFileMode) marks the
  entry as created on Unix, with its permissions; otherwise the entry has MS-DOS attributes.
- [CreateSymbolicLinkEntry](cref:Meziantou.Framework.Compression.ZipArchive.CreateSymbolicLinkEntry) writes a symbolic
  link as Info-ZIP does: a Unix mode of a link, and the target as the content.
- [ZipFile.CreateFromDirectory](cref:Meziantou.Framework.Compression.ZipFile.CreateFromDirectory) writes the files and the
  directories of a directory, with their times and their permissions (on Windows, their attributes), and stores the
  symbolic links as links:

<!-- ref:../../tests/Meziantou.Framework.Compression.Tests/ZipArchiveWriteTests.cs;region=ZipFile_CreateFromDirectory;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new ZipCompressionOptions
{
    CompressionMethod = ZipCompressionMethod.Xz,
    CompressionLevel = CompressionLevel.SmallestSize,
};

ZipFile.CreateFromDirectory(sourceDirectory, archivePath, options);

using var archive = ZipFile.OpenRead(archivePath);
Assert.Equal("file.txt", archive.Entries[0].FullName);
```
<!-- endref -->

## Write an encrypted archive

[ZipCompressionOptions.Password](cref:Meziantou.Framework.Compression.ZipCompressionOptions.Password) encrypts the content
of the entries, with WinZip AES-256 by default; [Encryption](cref:Meziantou.Framework.Compression.ZipCompressionOptions.Encryption)
selects AES-128, AES-192, or ZipCrypto for the tools that do not read AES:

<!-- ref:../../tests/Meziantou.Framework.Compression.Tests/ZipArchiveWriteTests.cs;region=ZipArchive_Password;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new ZipCompressionOptions
{
    CompressionMethod = ZipCompressionMethod.BZip2,
    Password = "secret",
    Encryption = ZipEncryption.Aes256,
};

using var stream = new MemoryStream();
using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true, readerOptions: null, options))
{
    using var writer = new StreamWriter(archive.CreateEntry("secret.txt").Open());
    writer.Write("Encrypted with WinZip AES");
}

stream.Position = 0;
using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false, new ArchiveReaderOptions { Password = "secret" }, compressionOptions: null))
{
    var entry = archive.GetEntry("secret.txt")!;
    Assert.Equal(ZipEncryption.Aes256, entry.EncryptionMethod);
    using var reader = new StreamReader(entry.Open());
    Assert.Equal("Encrypted with WinZip AES", reader.ReadToEnd());
}
```
<!-- endref -->

- The names, the sizes and the times of the entries are not encrypted.
- AES uses a random salt for each entry. The entries of fewer than 20 bytes, and all the entries written to a stream that
  is not seekable, do not store the CRC of their content (WinZip AE-2), so that it tells nothing about the content: the
  authentication code of AES checks it.
- ZipCrypto is weak: anybody can decrypt it. A ZipCrypto entry is always followed by a data descriptor, since its
  encryption header depends on the CRC of the content otherwise.
- The password is encoded in UTF-8.

## Update an archive

When the [Mode](cref:Meziantou.Framework.Compression.ZipArchive.Mode) of the archive is `ZipArchiveMode.Update`, the entries can be added,
[deleted](cref:Meziantou.Framework.Compression.ZipArchiveEntry.Delete) and replaced (delete the entry, then create one with
its name):

<!-- ref:../../tests/Meziantou.Framework.Compression.Tests/ZipArchiveWriteTests.cs;region=ZipArchive_Update;format=md-fenced-code-block;dedent=true -->
```csharp
// The archive is rewritten when it is disposed: the entries that are kept are copied without being decoded
using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
{
    archive.GetEntry("old.txt")!.Delete();
    using var writer = new StreamWriter(archive.CreateEntry("new.txt").Open());
    writer.Write("new");
}

using (var archive = ZipFile.OpenRead(path))
{
    Assert.Equal("new.txt", Assert.Single(archive.Entries).FullName);
}
```
<!-- endref -->

- The archive is rewritten when it is disposed, through a temporary file, if it is modified; otherwise the stream is not
  written to.
- The entries that are kept are copied as they are, without being decoded: the encrypted entries need no password.
- The new entries are written to a temporary file until the archive is disposed.
- The comment, the attributes and the Unix permissions of an entry can be changed, since they are stored in the central
  directory only. The time of an existing entry cannot be changed: it is stored in its local header too.

## Read forward only

[ArchiveReader.Create](cref:Meziantou.Framework.Compression.ArchiveReader.Create) reads a zip archive from a stream that is
not seekable, from its local headers (see [Getting started](02-GettingStarted.md)). The comments, the attributes and the
symbolic links are stored in the central directory, at the end of the archive, so this reader does not see them.
