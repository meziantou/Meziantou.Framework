# Meziantou.Extensions.Logging.FileLogger

An `ILogger` implementation that writes to a file. The messages are queued and written by a background thread, so logging doesn't block the application.

## Usage

```c#
using Microsoft.Extensions.Logging;

var logsDirectory = Path.Combine(Path.GetTempPath(), "logs");
using var provider = new FileLoggerProvider(logsDirectory);

using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(provider));
var logger = loggerFactory.CreateLogger("Sample");

logger.LogInformation("Hello from file logger");
Console.WriteLine($"Log file: {provider.LogFilePath}");
```

## Dependency Injection

```c#
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddFile(options =>
{
    options.Directory = "logs";
    options.RollInterval = RollInterval.Daily;
    options.MaxRetainedFiles = 7;
    options.CompressRolledFiles = true;
    options.IncludeScopes = true;
});
```

The options can also be set from the configuration, using the `File` provider alias:

```json
{
  "Logging": {
    "File": {
      "Directory": "logs",
      "RollInterval": "Daily",
      "MaxFileSizeInBytes": 10485760,
      "MaxRetainedFiles": 7,
      "FormatterName": "json",
      "LogLevel": {
        "Default": "Information"
      }
    }
  }
}
```

```c#
builder.Logging.AddFile();
```

Note that the options related to the log file itself (`Directory`, file name, `Append`, rolling, `MaxQueueLength`, `QueueFullMode`) are read when the provider is created. The other options are re-read when the configuration changes.

## Rolling and retention

| Option | Description |
| --- | --- |
| `RollInterval` | Creates a new file every hour / day / month |
| `MaxFileSizeInBytes` | Creates a new file when the current one reaches the size limit |
| `MaxRetainedFiles` | Deletes the oldest files when a new file is created |
| `Compression` | Compresses the log files using gzip, Brotli, or Zstandard (.NET 11+) |
| `Append` | Reuses an existing file instead of creating a new one at startup |

The log files are named `{FileNamePrefix}{timestamp}-{processId}{FileNameExtension}`, so they are ordered chronologically by name. When the name is already used, a suffix is added (`_001`, `_002`, …).

## Compression

```c#
options.Compression = LogFileCompression.GZip;      // GZip, Brotli, or Zstandard (.NET 11+)
options.CompressionLevel = CompressionLevel.SmallestSize;
options.CompressionMode = LogFileCompressionMode.Continuous;
```

| `CompressionMode` | Description |
| --- | --- |
| `Continuous` (default) | The messages are compressed as they are written, so the file is never written uncompressed. The extension of the compression algorithm is part of the file name (`2024-01-02-1234.log.gz`). |
| `OnRoll` | The current log file is a plain text file, and it is compressed once it is rolled. |

`Continuous` doesn't need any extra disk space and doesn't pause the logging to compress a big file, but the compressed stream is only finalized when the file is rolled or when the provider is disposed, so the current log file may not be readable by all the tools while the application is running. Use `OnRoll` when you need to read the current log file with the usual text tools.

`Append` is ignored when the messages are compressed continuously, as appending to a compressed file would produce a file that most tools cannot read entirely.

`MaxFileSizeInBytes` is compared to the size of the file on disk, so it accounts for the compression. As the compressed size is only known once the data is flushed, the file may be rolled slightly before the limit.

## Permissions

On Unix, the log files are created with the default mode of the platform, which usually makes them readable by every local user. Set `UnixCreateMode` when the messages can contain sensitive data:

```c#
options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
```

The value is ignored on Windows, and it is still filtered by the umask of the process. The mode of the directory is not changed, so create the directory yourself when it must be private too.

## Formatters

`SimpleFileFormatter` (default) writes one human-readable line per entry:

```
[2024-01-02 03:04:05.006] [INFO] [Sample.Program] => Scope1 Hello world
```

`JsonFileFormatter` writes one JSON object per entry, including the message template and its named parameters:

```c#
builder.Logging.AddFile("logs", options => options.FormatterName = FileFormatterNames.Json);
```

```json
{"Timestamp":"2024-01-02 03:04:05.006","LogLevel":"INFO","Category":"Sample.Program","Message":"Hello world","State":{"Name":"world","{OriginalFormat}":"Hello {Name}"},"Scopes":["Scope1"]}
```

You can also write a custom formatter by inheriting from `FileFormatter` and setting `FileLoggerOptions.Formatter`.

## Content of the messages

| Option | Default | Description |
| --- | --- | --- |
| `MinLevel` | `Trace` | Minimum level of the messages written to the file |
| `TimestampFormat` | `yyyy-MM-dd HH:mm:ss.fff` | Format of the timestamp, `null` to omit it |
| `UseUtcTimestamp` | `true` | Use UTC instead of the local time |
| `IncludeLogLevel` | `true` | Write the log level |
| `IncludeCategory` | `true` | Write the category |
| `UseShortCategoryName` | `false` | Write only the last segment of the category |
| `IncludeScopes` | `false` | Write the scopes |
| `IncludeEventId` | `false` | Write the event id |
| `IncludeThreadId` | `false` | Write the id of the thread that logged the message |
| `IncludeActivityTracking` | `false` | Write the trace id and the span id of `Activity.Current` |

## Reliability

- The messages are queued in a bounded queue. When it is full, `QueueFullMode` determines if the caller waits for room (default), or if the message is dropped.
- The pending messages are flushed as soon as the queue is empty, and at most every `FlushInterval` when the logger cannot keep up.
- `FlushAsync` waits for the pending messages to be written.
- When the log file cannot be created or written, a warning is written on the standard error stream and the application keeps running.
