# Meziantou.Framework.Compression

A .NET library, written in C# only, that reads and writes the archive formats and the compressed streams that
`System.IO.Compression` does not support, with an API similar to `ZipArchive` and `ZipFile`.

## Features

- **7z**: read, create and update archives (LZMA, LZMA2, PPMd, BZip2, Deflate, Deflate64, BCJ/BCJ2/Delta filters, AES-256
  encryption, solid blocks, multi-volume archives) with `SevenZipArchive` and `SevenZipFile`
- **RAR**: read RAR5 and RAR 2.0 to 4.x archives (solid, multi-volume, encrypted, self-extracting) with `RarArchive` and `RarFile`
- **zip**: read the archives that `ZipArchive` cannot (BZip2, LZMA, PPMd, XZ, Zstandard, Shrink, Reduce, Implode, WinZip AES,
  ZipCrypto, split archives) with `ReadOnlyZipArchive`
- **Any archive**: open zip, 7z, RAR and tar archives (compressed with gzip, bzip2, XZ, LZMA, lzip, Zstandard or Unix
  compress) whose format is detected from their content with `ArchiveFile`, or read them forward only from a stream that
  is not seekable with `ArchiveReader`
- **Streams**: `XzStream`, `LzmaStream`, `LZipStream` and `BZip2Stream` compress and decompress like `GZipStream`;
  `UnixCompressStream` decompresses `.Z` files
- **Safe extraction**: `ExtractToDirectory` rejects the paths and the links that leave the destination directory, and
  `ArchiveReaderOptions.MaxMemoryUsage` limits the memory of the decoders

## Usage

The types are in the `Meziantou.Framework.Compression` namespace.

### Open any archive

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

### Read an archive from a stream that is not seekable

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
        Assert.Equal(entry.Length, ReadAll(content).Length);
    }
}
```
<!-- endref -->

### Create a 7z archive

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

### Compress with XZ

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
