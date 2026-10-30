---
title: Overview
description: An ILogger provider that writes to files from a background thread, with rolling, retention and compression.
---

`Meziantou.Extensions.Logging.FileLogger` provides `FileLoggerProvider`, an `ILoggerProvider` that writes the log
entries to files. The entries are formatted on the thread that logs them, queued, and written by a background thread, so
logging does not wait for the disk.

<!-- ref:../../../tests/Meziantou.Extensions.Logging.FileLogger.Tests/FileLoggerProviderTests.cs;region=FileLogger_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var logsDirectory = Directory.CreateTempSubdirectory("logs").FullName;
await using var provider = new FileLoggerProvider(logsDirectory);

using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(provider));
var logger = loggerFactory.CreateLogger("Sample");

logger.LogInformation("Hello from file logger");

// Wait for the background thread to write the pending messages
await provider.FlushAsync();

var logFilePath = provider.LogFilePath;
Assert.NotNull(logFilePath);
Assert.StartsWith(logsDirectory, logFilePath);
```
<!-- endref -->

- `new FileLoggerProvider(directory)` creates the provider directly. In an application with a host, register it with
  `AddFile` instead: see [Configuration](02-Configuration.md).
- `FlushAsync` waits for the background thread to write the pending messages. Disposing the provider writes them too,
  then closes the file.
- `LogFilePath` is the path of the file the messages are written to. It changes when the file is rolled.

## Pages

- [Configuration](02-Configuration.md): register the provider, and set the options in code or in the configuration.
- [Files, rolling and retention](03-FilesRollingAndRetention.md): file names, a new file per period or per size, the
  number of files to keep, and appending to an existing file.
- [Compression](04-Compression.md): compress the files with gzip, Brotli or Zstandard, while they are written or
  once they are rolled.
- [Message format](05-MessageFormat.md): the content of an entry, the JSON format, and custom formatters.
- [Queue and flushing](06-QueueAndFlushing.md): what happens when the disk is slow, when the messages reach the disk,
  and how errors are handled.
- [Security](07-Security.md): the permissions of the files, and messages that contain untrusted data.
