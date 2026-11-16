---
title: 7z archives
description: Read, create and update 7z archives, choose the compression method, the solid blocks and the filters, and protect them with a password.
---

## Read an archive

[SevenZipArchive](cref:Meziantou.Framework.Compression.SevenZipArchive) opens a 7z archive from a seekable stream:

<!-- ref:../../tests/Meziantou.Framework.Compression.Tests/SevenZipArchiveTests.cs;region=SevenZipArchive_Read;format=md-fenced-code-block;dedent=true -->
```csharp
var path = Path.Combine(AppContext.BaseDirectory, "TestData", "SevenZip", "lzma2.7z");
using (var archive = new SevenZipArchive(File.OpenRead(path)))
{
    var entry = archive.GetEntry("hello.txt");
    Assert.NotNull(entry);
    using var reader = new StreamReader(entry.Open());
    Assert.StartsWith("Hello, 7z!", reader.ReadToEnd(), StringComparison.Ordinal);
}
```
<!-- endref -->

The entries of a solid block are compressed together. Reading them in the order of the archive decodes the block once;
opening an entry before the last one read decodes the block again from its start.
[SevenZipFile.ExtractToDirectory](cref:Meziantou.Framework.Compression.SevenZipFile.ExtractToDirectory) reads them in order:

<!-- ref:../../tests/Meziantou.Framework.Compression.Tests/SevenZipFileTests.cs;region=SevenZipFile_ExtractToDirectory;format=md-fenced-code-block;dedent=true -->
```csharp
SevenZipFile.ExtractToDirectory(archivePath, destinationDirectory, overwriteFiles: true);

Assert.True(File.Exists(Path.Combine(destinationDirectory, "hello.txt")));
```
<!-- endref -->

[SevenZipFile.OpenRead](cref:Meziantou.Framework.Compression.SevenZipFile.OpenRead) opens the volumes of a multi-volume
archive when the path ends with `.001`.

## Create an archive

In [SevenZipArchiveMode.Create](cref:Meziantou.Framework.Compression.SevenZipArchiveMode.Create), each entry is compressed
as it is written, and the headers are written when the archive is disposed. The stream must be seekable, and one entry
can be written at a time:

<!-- ref:../../tests/Meziantou.Framework.Compression.Tests/SevenZipArchiveTests.cs;region=SevenZipArchive_Create;format=md-fenced-code-block;dedent=true -->
```csharp
using var stream = new MemoryStream();
using (var archive = new SevenZipArchive(stream, SevenZipArchiveMode.Create, leaveOpen: true))
{
    // Each entry is compressed as it is written, into the current solid folder
    var entry = archive.CreateEntry("docs/readme.txt");
    using var writer = new StreamWriter(entry.Open());
    writer.Write("Hello from 7z");
}

stream.Position = 0;
using (var archive = new SevenZipArchive(stream))
{
    Assert.Equal("docs/readme.txt", archive.Entries[0].FullName);
}
```
<!-- endref -->

[SevenZipFile.CreateFromDirectory](cref:Meziantou.Framework.Compression.SevenZipFile.CreateFromDirectory) compresses a
directory, with its empty directories, its symbolic links and the times and attributes of the files:

<!-- ref:../../tests/Meziantou.Framework.Compression.Tests/SevenZipFileTests.cs;region=SevenZipFile_CreateFromDirectory;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new SevenZipCompressionOptions
{
    Method = SevenZipCompressionMethod.Lzma2,
    Level = 9,
};

SevenZipFile.CreateFromDirectory(sourceDirectory, archivePath, options);

using var archive = SevenZipFile.OpenRead(archivePath);
Assert.Equal("file.txt", archive.Entries[0].FullName);
```
<!-- endref -->

[SevenZipCompressionOptions](cref:Meziantou.Framework.Compression.SevenZipCompressionOptions) sets how the entries are
compressed:

- [Method](cref:Meziantou.Framework.Compression.SevenZipCompressionOptions.Method): LZMA2 (the default), LZMA, PPMd (often
  the best for text), BZip2, Deflate, or Copy to store the files.
- [Level](cref:Meziantou.Framework.Compression.SevenZipCompressionOptions.Level): 0 to 9, as the `-mx` option of 7-Zip; 0
  stores the files. [DictionarySize](cref:Meziantou.Framework.Compression.SevenZipCompressionOptions.DictionarySize)
  replaces the size the level chooses.
- [Solid](cref:Meziantou.Framework.Compression.SevenZipCompressionOptions.Solid): compress the files together, which
  compresses better but makes reading one file slower.
  [SolidBlockSize](cref:Meziantou.Framework.Compression.SevenZipCompressionOptions.SolidBlockSize) and
  [SolidFileCount](cref:Meziantou.Framework.Compression.SevenZipCompressionOptions.SolidFileCount) limit the blocks.
- [Filter](cref:Meziantou.Framework.Compression.SevenZipCompressionOptions.Filter): the default,
  [SevenZipFilter.Auto](cref:Meziantou.Framework.Compression.SevenZipFilter.Auto), applies the x86 BCJ filter to the
  executables (`.exe`, `.dll`, ...), by their extension. Choose the filter of another processor, or
  [None](cref:Meziantou.Framework.Compression.SevenZipFilter.None).

## Protect an archive with a password

[Password](cref:Meziantou.Framework.Compression.SevenZipCompressionOptions.Password) encrypts the content with AES-256, and
[EncryptHeaders](cref:Meziantou.Framework.Compression.SevenZipCompressionOptions.EncryptHeaders) also encrypts the names
of the entries. Read the archive with
[ArchiveReaderOptions.Password](cref:Meziantou.Framework.Compression.ArchiveReaderOptions.Password):

<!-- ref:../../tests/Meziantou.Framework.Compression.Tests/SevenZipArchiveTests.cs;region=SevenZipArchive_Password;format=md-fenced-code-block;dedent=true -->
```csharp
using var stream = new MemoryStream();
var compressionOptions = new SevenZipCompressionOptions
{
    Password = "secret",
    EncryptHeaders = true, // Also encrypt the names of the entries
};

using (var archive = new SevenZipArchive(stream, SevenZipArchiveMode.Create, leaveOpen: true, readerOptions: null, compressionOptions))
{
    using var writer = new StreamWriter(archive.CreateEntry("secret.txt").Open());
    writer.Write("Top secret");
}

stream.Position = 0;
var readerOptions = new ArchiveReaderOptions { Password = "secret" };
using (var archive = new SevenZipArchive(stream, SevenZipArchiveMode.Read, leaveOpen: true, readerOptions, compressionOptions: null))
{
    using var reader = new StreamReader(archive.Entries[0].Open());
    Assert.Equal("Top secret", reader.ReadToEnd());
}
```
<!-- endref -->

## Update an archive

In [SevenZipArchiveMode.Update](cref:Meziantou.Framework.Compression.SevenZipArchiveMode.Update), add entries with
[CreateEntry](cref:Meziantou.Framework.Compression.SevenZipArchive.CreateEntry), remove them with
[Delete](cref:Meziantou.Framework.Compression.SevenZipArchiveEntry.Delete), and change their times and attributes. The
archive is rewritten when it is disposed: the solid blocks whose entries are all kept are copied as they are, and the
entries of the other blocks are compressed again.
