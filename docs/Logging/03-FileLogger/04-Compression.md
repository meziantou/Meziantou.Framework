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
| [Compression](cref:Meziantou.Extensions.Logging.FileLoggerOptions.Compression) | [None](cref:Meziantou.Extensions.Logging.LogFileCompression.None) | [GZip](cref:Meziantou.Extensions.Logging.LogFileCompression.GZip) (`.gz`), [Brotli](cref:Meziantou.Extensions.Logging.LogFileCompression.Brotli) (`.br`), or [Zstandard](cref:Meziantou.Extensions.Logging.LogFileCompression.Zstandard) (`.zst`, .NET 11 and later only) |
| [CompressionMode](cref:Meziantou.Extensions.Logging.FileLoggerOptions.CompressionMode) | [Continuous](cref:Meziantou.Extensions.Logging.LogFileCompressionMode.Continuous) | When the files are compressed, see below |
| [CompressionLevel](cref:Meziantou.Extensions.Logging.FileLoggerOptions.CompressionLevel) | `Optimal` | The `System.IO.Compression.CompressionLevel` passed to the compression stream |

## When the files are compressed

| [CompressionMode](cref:Meziantou.Extensions.Logging.FileLoggerOptions.CompressionMode) | Behavior |
| --- | --- |
| [Continuous](cref:Meziantou.Extensions.Logging.LogFileCompressionMode.Continuous) | The messages are compressed as they are written: the file is never written uncompressed, and the extension of the algorithm is part of its name from the start (`2024-01-02-1234.log.gz`). |
| [OnRoll](cref:Meziantou.Extensions.Logging.LogFileCompressionMode.OnRoll) | The current file is a plain text file. Once it is rolled, it is compressed to a new file with the extension of the algorithm, and the plain text file is deleted. |

Choose according to how you read the current file:

- [Continuous](cref:Meziantou.Extensions.Logging.LogFileCompressionMode.Continuous) needs no extra disk space and never pauses the logging to compress a big file. But the compressed
  stream is only completed when the file is rolled or when the provider is disposed, so while the application runs,
  some tools cannot read the end of the current file.
- [OnRoll](cref:Meziantou.Extensions.Logging.LogFileCompressionMode.OnRoll) keeps the current file readable by the usual text tools. The compression runs on the thread that writes the
  messages, so rolling a big file delays the next messages. When a file cannot be compressed, the plain text file is
  kept. The file being written when the application stops is not rolled, so it stays a plain text file.

## Interactions with the other options

- [Append](cref:Meziantou.Extensions.Logging.FileLoggerOptions.Append) is ignored in [Continuous](cref:Meziantou.Extensions.Logging.LogFileCompressionMode.Continuous) mode: appending to a compressed file would produce a file that most tools cannot
  read entirely.
- [MaxFileSizeInBytes](cref:Meziantou.Extensions.Logging.FileLoggerOptions.MaxFileSizeInBytes) is compared with the size of the file on disk, so it accounts for the compression. As the
  compressed size is only known once the data is flushed, a file may be rolled slightly before the limit.
- [MaxRetainedFiles](cref:Meziantou.Extensions.Logging.FileLoggerOptions.MaxRetainedFiles) counts the compressed files, whatever their algorithm. See
  [Files, rolling and retention](03-FilesRollingAndRetention.md#keep-the-last-files).
