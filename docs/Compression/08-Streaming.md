---
title: Streaming and memory
description: Read and write archives without keeping their content in memory, which operations need a seekable stream, and what uses a temporary file.
---

The content of an entry is compressed as it is written and decompressed as it is read: the library does not keep the
content of an entry, or of an archive, in memory. An archive of several gigabytes can be read from the response of an
HTTP request, or written to a pipe, with a few megabytes of memory.

A zip archive written to a pipe can be read at the other end while it is being written:

<!-- ref:../../tests/Meziantou.Framework.Compression.Tests/ArchiveFileTests.cs;region=ArchiveReader_Pipe;format=md-fenced-code-block;dedent=true -->
```csharp
// A pipe is not seekable, like the body of an HTTP request or the standard output of a process
using var output = new AnonymousPipeServerStream(PipeDirection.Out);
using var input = new AnonymousPipeClientStream(PipeDirection.In, output.ClientSafePipeHandle);
var writer = Task.Run(() =>
{
    // Each entry is compressed into the pipe as it is written
    using var archive = new ZipArchive(output, ZipArchiveMode.Create);
    foreach (var name in new[] { "a.txt", "b.txt" })
    {
        using var stream = new StreamWriter(archive.CreateEntry(name).Open());
        stream.Write($"Content of {name}");
    }
});

// The entries are read while the archive is being written
using var reader = ArchiveReader.Create(input);
var contents = new Dictionary<string, string>(StringComparer.Ordinal);
while (reader.MoveToNextEntry())
{
    using var content = new StreamReader(reader.OpenEntryStream());
    contents[reader.Entry!.FullName] = content.ReadToEnd();
}

await writer;
Assert.Equal("Content of b.txt", contents["b.txt"]);
```
<!-- endref -->

## Streams that cannot seek

Some formats need to move back and forth in the archive: their index is at the end. The others can be read or written
from start to end, from a stream whose `CanSeek` is `false`, such as a network stream or a pipe:

| Operation | The stream may not be seekable | Temporary file |
| --- | --- | --- |
| Read a zip, RAR, tar or compressed tar archive with [ArchiveReader.Create](cref:Meziantou.Framework.Compression.ArchiveReader.Create) | Yes | No |
| Open an archive with [ArchiveFile.Open](cref:Meziantou.Framework.Compression.ArchiveFile.Open), [ZipArchive](cref:Meziantou.Framework.Compression.ZipArchive), [RarArchive](cref:Meziantou.Framework.Compression.RarArchive) or [SevenZipArchive](cref:Meziantou.Framework.Compression.SevenZipArchive) | No | A compressed tar archive is decompressed to one |
| Create a zip archive (`ZipArchiveMode.Create`) | Yes | No |
| Create a 7z archive ([SevenZipArchiveMode.Create](cref:Meziantou.Framework.Compression.SevenZipArchiveMode.Create)) | No | No |
| Update a zip or 7z archive | No | The new entries, and the rewritten archive |
| Compress or decompress with [XzStream](cref:Meziantou.Framework.Compression.XzStream), [LzmaStream](cref:Meziantou.Framework.Compression.LzmaStream), [LZipStream](cref:Meziantou.Framework.Compression.LZipStream), [BZip2Stream](cref:Meziantou.Framework.Compression.BZip2Stream) or [UnixCompressStream](cref:Meziantou.Framework.Compression.UnixCompressStream) | Yes | No |

The types that need a seekable stream throw an `ArgumentException` when it is not, and
[ArchiveReader.Create](cref:Meziantou.Framework.Compression.ArchiveReader.Create) throws a `NotSupportedException` for a
7z archive. To read one of these archives from the network, copy it to a temporary file first.

- A 7z archive needs a seekable stream to be read and to be written: its headers are at its end, and the header at its
  start, written last, gives their position.
- A zip archive read forward only does not see what only its central directory stores (the comments, the attributes and
  the symbolic links), and the length of an entry written with a data descriptor is known once its content is read: see
  [Read an archive from a stream that is not seekable](02-GettingStarted.md).
- A zip archive written to a stream that cannot seek gives the sizes of each entry in a data descriptor after its
  content, and its AES entries do not store the CRC of their content: see [Zip archives](05-Zip.md).
- [ArchiveReaderOptions.VolumeResolver](cref:Meziantou.Framework.Compression.ArchiveReaderOptions.VolumeResolver) can
  give the next volumes of a multi-volume archive read forward only as streams that cannot seek, too.

## What stays in memory

The memory does not grow with the size of the entries. The library keeps in memory:

- the state of the decoders and the encoders: the dictionary of LZMA and LZMA2, the model of PPMd, the window of RAR,
  the blocks of bzip2 (up to 900 kB). When reading,
  [ArchiveReaderOptions.MaxMemoryUsage](cref:Meziantou.Framework.Compression.ArchiveReaderOptions.MaxMemoryUsage) limits
  them: see [Untrusted archives](07-Security.md);
- the list of the entries of an archive opened for random access, and the headers of a 7z archive;
- before .NET 11, which has a streaming Zstandard decoder, the Zstandard data: a zip entry compressed with Zstandard and
  a `.tar.zst` archive are decoded whole in memory, within `MaxMemoryUsage`.

The content of the entries of a solid 7z or RAR block is decoded once when they are read in the order of the archive.
Opening an entry before the last one read decodes the block again from its start: it takes time, not memory.
