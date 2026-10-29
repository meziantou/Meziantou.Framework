---
title: Compression
description: Compress the log files with gzip, Brotli or Zstandard, either while they are written or once they are rolled.
---

<!-- ref:../../../tests/Meziantou.Extensions.Logging.FileLogger.Tests/FileLoggerProviderTests.cs;region=FileLogger_Compression;format=md-fenced-code-block;dedent=true -->
```csharp
options.Compression = LogFileCompression.GZip;      // GZip, Brotli, or Zstandard (.NET 11+)
options.CompressionLevel = CompressionLevel.SmallestSize;
options.CompressionMode = LogFileCompressionMode.Continuous;
```
<!-- endref -->

| Option | Default | Description |
| --- | --- | --- |
| `Compression` | `None` | `GZip` (`.gz`), `Brotli` (`.br`), or `Zstandard` (`.zst`, .NET 11 and later only) |
| `CompressionMode` | `Continuous` | When the files are compressed, see below |
| `CompressionLevel` | `Optimal` | The `System.IO.Compression.CompressionLevel` passed to the compression stream |

## When the files are compressed

| `CompressionMode` | Behavior |
| --- | --- |
| `Continuous` | The messages are compressed as they are written: the file is never written uncompressed, and the extension of the algorithm is part of its name from the start (`2024-01-02-1234.log.gz`). |
| `OnRoll` | The current file is a plain text file. Once it is rolled, it is compressed to a new file with the extension of the algorithm, and the plain text file is deleted. |

Choose according to how you read the current file:

- `Continuous` needs no extra disk space and never pauses the logging to compress a big file. But the compressed
  stream is only completed when the file is rolled or when the provider is disposed, so while the application runs,
  some tools cannot read the end of the current file.
- `OnRoll` keeps the current file readable by the usual text tools. The compression runs on the thread that writes the
  messages, so rolling a big file delays the next messages. When a file cannot be compressed, the plain text file is
  kept. The file being written when the application stops is not rolled, so it stays a plain text file.

## Interactions with the other options

- `Append` is ignored in `Continuous` mode: appending to a compressed file would produce a file that most tools cannot
  read entirely.
- `MaxFileSizeInBytes` is compared with the size of the file on disk, so it accounts for the compression. As the
  compressed size is only known once the data is flushed, a file may be rolled slightly before the limit.
- `MaxRetainedFiles` counts the compressed files, whatever their algorithm. See
  [Files, rolling and retention](03-FilesRollingAndRetention.md#keep-the-last-files).
