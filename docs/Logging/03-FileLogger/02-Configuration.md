---
title: Configuration
description: Register the file logger on a host, set its options in code or in the configuration, and know which options can change at run time.
---

## Register the provider on a host

`AddFile` registers the provider on the `ILoggingBuilder` of a host:

<!-- ref:../../../samples/Meziantou.Extensions.Logging.FileLogger.Samples/ReadmeExamples.cs;region=FileLogger_AddFileWithOptions;format=md-fenced-code-block;dedent=true -->
```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddFile(options =>
{
    options.Directory = "logs";
    options.RollInterval = RollInterval.Daily;
    options.MaxRetainedFiles = 7;
    options.Compression = LogFileCompression.GZip;
    options.CompressionMode = LogFileCompressionMode.OnRoll;
    options.IncludeScopes = true;
});
```
<!-- endref -->

| Overload | Options |
| --- | --- |
| `AddFile()` | Read from the configuration only |
| `AddFile(logsDirectory)` | The directory, then the configuration |
| `AddFile(configure)` | The configuration, then the delegate |
| `AddFile(logsDirectory, configure)` | The configuration, then the directory and the delegate |

Calling `AddFile` several times registers a single provider; the delegates of every call apply to its options.
`Directory` is required: the provider throws an `InvalidOperationException` when it is created without it.

When a `TimeProvider` is registered in the services, the provider uses it for the timestamps and to roll the files, so
a test can control the time.

## Set the options in the configuration

The options are read from the `Logging:File` section of the configuration, `File` being the alias of the provider. The
`LogLevel` subsection filters the messages of this provider, as for any other provider:

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

With these settings, the parameterless overload is enough:

<!-- ref:../../../samples/Meziantou.Extensions.Logging.FileLogger.Samples/ReadmeExamples.cs;region=FileLogger_AddFileFromConfiguration;format=md-fenced-code-block;dedent=true -->
```csharp
builder.Logging.AddFile();
```
<!-- endref -->

## Options that change at run time

When the configuration changes, the provider uses the new options for the next messages, but only for some of them:

- **Read once**, when the provider is created: everything about the files and the queue. That is `Directory`,
  `FileNamePrefix`, `FileNameExtension`, `IncludeProcessIdInFileName`, `Append`, `RollInterval`,
  `MaxFileSizeInBytes`, `MaxRetainedFiles`, `UnixCreateMode`, the compression options, `MaxQueueLength` and
  `QueueFullMode`. Restart the application to change them.
- **Updated**: `MinLevel`, `FlushInterval`, the formatter, and the options that set the content of the messages (see
  [Message format](05-MessageFormat.md)).

## Create the provider without a host

Without a host, create the provider with a `FileLoggerOptions` and add it to a `LoggerFactory`:

<!-- ref:../../../samples/Meziantou.Extensions.Logging.FileLogger.Samples/Examples.cs;region=FileLoggerProvider_Create;format=md-fenced-code-block;dedent=true -->
```csharp
using var provider = new FileLoggerProvider(new FileLoggerOptions
{
    Directory = "logs",
    RollInterval = RollInterval.Daily,
    MaxRetainedFiles = 7,
});

using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(provider));
```
<!-- endref -->

The constructors also accept an `IOptionsMonitor<FileLoggerOptions>`, to follow the changes of the options, and a
`TimeProvider`. Dispose the provider before the application exits: disposing it writes the queued messages. See
[Queue and flushing](06-QueueAndFlushing.md).
