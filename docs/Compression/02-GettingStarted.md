---
title: Getting started
description: Install the package, open an archive whatever its format, and read an archive from a stream that is not seekable.
---

## Install the package

Add the `Meziantou.Framework.Compression` package to a project:

<InstallNuGetPackage Id="Meziantou.Framework.Compression" />

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework.Compression;`.

## Open an archive

[ArchiveFile.Open](cref:Meziantou.Framework.Compression.ArchiveFile.Open) detects the format of an archive from its
content, so that the extension of the file does not matter. It returns an
[Archive](cref:Meziantou.Framework.Compression.Archive): its [Entries](cref:Meziantou.Framework.Compression.Archive.Entries)
are known when the archive is opened, and can be read in any order.
[ArchiveFile.ExtractToDirectory](cref:Meziantou.Framework.Compression.ArchiveFile.ExtractToDirectory) extracts all of them:

<!-- ref:../../tests/Meziantou.Framework.Compression.Tests/ArchiveFileTests.cs;region=ArchiveFile_Open;format=md-fenced-code-block;dedent=true -->
```csharp
// The format is detected from the content: zip, 7z, RAR, or tar compressed with gzip, bzip2, XZ, ...
using (var archive = ArchiveFile.Open(path))
{
    foreach (var entry in archive.Entries)
    {
        if (entry.IsDirectory)
            continue;

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        Assert.NotNull(text);
    }

    var hello = archive.GetEntry("hello.txt");
    Assert.Equal(16, hello?.Length);
}

ArchiveFile.ExtractToDirectory(path, destination);
```
<!-- endref -->

- The other volumes of a multi-volume archive are found next to the first one. Open a stream with
  [ArchiveReaderOptions.VolumeResolver](cref:Meziantou.Framework.Compression.ArchiveReaderOptions.VolumeResolver) to give
  them yourself.
- A compressed tar archive is decompressed to a temporary file, which is deleted when the archive is disposed.
- [ArchiveFile.DetectFormat](cref:Meziantou.Framework.Compression.ArchiveFile.DetectFormat) tells the format of a file
  without opening it, as an [ArchiveFormatInfo](cref:Meziantou.Framework.Compression.ArchiveFormatInfo): the
  [ArchiveFormat](cref:Meziantou.Framework.Compression.ArchiveFormat) and the
  [CompressionFormat](cref:Meziantou.Framework.Compression.CompressionFormat) around it.

Every entry is an [ArchiveEntry](cref:Meziantou.Framework.Compression.ArchiveEntry), with its name, its length, its times,
its attributes and the target of a link. The stream that
[Open](cref:Meziantou.Framework.Compression.ArchiveEntry.Open) returns checks the CRC or the hash of the content, and throws
an `InvalidDataException` at its end when they do not match.

## Read an archive from a stream that is not seekable

[ArchiveReader.Create](cref:Meziantou.Framework.Compression.ArchiveReader.Create) reads the entries one after the other,
from a stream that does not need to be seekable, such as the response of an HTTP request. It supports zip, RAR, tar and
the compressed tar archives; a 7z archive needs to be seekable, so it throws a `NotSupportedException`:

<!-- ref:../../tests/Meziantou.Framework.Compression.Tests/ArchiveFileTests.cs;region=ArchiveReader_ForwardOnly;format=md-fenced-code-block;dedent=true -->
```csharp
// The stream does not need to be seekable: a network response, a pipe, ...
using var input = File.OpenRead(path);
using var archiveReader = ArchiveReader.Create(input);
Assert.Equal(ArchiveFormat.Tar, archiveReader.Format);
Assert.Equal(CompressionFormat.GZip, archiveReader.CompressionFormat);
while (archiveReader.MoveToNextEntry())
{
    var entry = archiveReader.Entry!;
    if (entry.FullName.EndsWith(".txt", StringComparison.Ordinal))
    {
        // The content can be read until the reader moves to the next entry
        using var content = archiveReader.OpenEntryStream();
        using var copy = new MemoryStream();
        content.CopyTo(copy);
        Assert.Equal(entry.Length, copy.Length);
    }
}
```
<!-- endref -->

The content of an entry can be read until the reader moves to the next entry, and the content that is not read is
skipped. In a zip archive read forward only, the length and the CRC of an entry written with a data descriptor are known
only once its content is read: until then, [Length](cref:Meziantou.Framework.Compression.ArchiveEntry.Length) is -1.

## Errors

- `InvalidDataException`: the data are not an archive of a supported format, or they are corrupted.
- [ArchivePasswordException](cref:Meziantou.Framework.Compression.ArchivePasswordException): the archive is encrypted,
  and [ArchiveReaderOptions.Password](cref:Meziantou.Framework.Compression.ArchiveReaderOptions.Password) is missing or wrong.
- `NotSupportedException`: the archive uses a method that the library does not support (RAR 1.5, the PKWARE strong
  encryption of zip, ...).
