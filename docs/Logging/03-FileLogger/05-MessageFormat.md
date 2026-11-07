---
title: Message format
description: Choose what an entry contains, write JSON instead of text, or write your own formatter.
---

## The text format

By default, [SimpleFileFormatter](cref:Meziantou.Extensions.Logging.SimpleFileFormatter) writes one human-readable line per entry. The options choose the segments of the
line:

<!-- ref:../../../tests/Meziantou.Extensions.Logging.FileLogger.Tests/FileLoggerProviderTests.cs;region=FileLogger_SimpleFormat;format=md-fenced-code-block;dedent=true -->
```csharp
var logsDirectory = Directory.CreateTempSubdirectory("logs").FullName;
var timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero));
var options = new FileLoggerOptions
{
    Directory = logsDirectory,
    IncludeScopes = true,
    IncludeEventId = true,
    UseShortCategoryName = true,
};

string? logFilePath;
await using (var provider = new FileLoggerProvider(options, timeProvider))
{
    using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(provider));
    var logger = loggerFactory.CreateLogger("Sample.Program");
    using (logger.BeginScope("Request {RequestId}", 42))
    {
        logger.LogWarning(new EventId(7, "Retry"), "Retrying {Url}", "https://example.com");
    }

    logFilePath = provider.LogFilePath;
}

// Disposing the provider writes the pending messages and closes the file
Assert.NotNull(logFilePath);
Assert.Equal(["[2024-01-02 03:04:05.000] [WARN] [Program] [EventId:7:Retry] => Request 42 Retrying https://example.com"], File.ReadAllLines(logFilePath));
```
<!-- endref -->

| Option | Default | Segment |
| --- | --- | --- |
| [TimestampFormat](cref:Meziantou.Extensions.Logging.FileLoggerOptions.TimestampFormat) | `yyyy-MM-dd HH:mm:ss.fff` | `[2024-01-02 03:04:05.000]`; `null` omits the timestamp |
| [UseUtcTimestamp](cref:Meziantou.Extensions.Logging.FileLoggerOptions.UseUtcTimestamp) | `true` | The timestamp is in UTC; `false` uses the local time |
| [IncludeLogLevel](cref:Meziantou.Extensions.Logging.FileLoggerOptions.IncludeLogLevel) | `true` | `[TRCE]`, `[DBUG]`, `[INFO]`, `[WARN]`, `[FAIL]` or `[CRIT]` |
| [IncludeCategory](cref:Meziantou.Extensions.Logging.FileLoggerOptions.IncludeCategory) | `true` | `[Sample.Program]` |
| [UseShortCategoryName](cref:Meziantou.Extensions.Logging.FileLoggerOptions.UseShortCategoryName) | `false` | Only the last segment of the category: `[Program]` |
| [IncludeEventId](cref:Meziantou.Extensions.Logging.FileLoggerOptions.IncludeEventId) | `false` | `[EventId:7:Retry]`, the name being omitted when the event has none |
| [IncludeThreadId](cref:Meziantou.Extensions.Logging.FileLoggerOptions.IncludeThreadId) | `false` | `[ThreadId:12]`, the managed identifier of the thread that logged the message |
| [IncludeActivityTracking](cref:Meziantou.Extensions.Logging.FileLoggerOptions.IncludeActivityTracking) | `false` | `[TraceId:… SpanId:…]` of `Activity.Current`, when there is one |
| [IncludeScopes](cref:Meziantou.Extensions.Logging.FileLoggerOptions.IncludeScopes) | `false` | `=> Request 42`, one segment per active scope |

The message follows the segments. The exception, when there is one, starts on the next line. To keep every entry on a
single line, set [EscapeControlCharacters](cref:Meziantou.Extensions.Logging.FileLoggerOptions.EscapeControlCharacters): see [Security](07-Security.md#untrusted-data-in-the-messages).

[MinLevel](cref:Meziantou.Extensions.Logging.FileLoggerOptions.MinLevel) (default `Trace`) drops the messages below a level before they are formatted. The filters of the logging
configuration, such as `Logging:File:LogLevel`, apply too. An entry whose message is empty and that has no exception is
not written.

## The JSON format

[JsonFileFormatter](cref:Meziantou.Extensions.Logging.JsonFileFormatter) writes one JSON object per line. Select it with `FormatterName = FileFormatterNames.Json`, or with
`"FormatterName": "json"` in the configuration:

<!-- ref:../../../tests/Meziantou.Extensions.Logging.FileLogger.Tests/FileLoggerProviderTests.cs;region=FileLogger_JsonFormat;format=md-fenced-code-block;dedent=true -->
```csharp
var logsDirectory = Directory.CreateTempSubdirectory("logs").FullName;
var timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero));
var options = new FileLoggerOptions
{
    Directory = logsDirectory,
    FormatterName = FileFormatterNames.Json,
    IncludeScopes = true,
};

string? logFilePath;
await using (var provider = new FileLoggerProvider(options, timeProvider))
{
    var logger = provider.CreateLogger("Sample.Program");
    using (logger.BeginScope("Request {RequestId}", 42))
    {
        logger.LogInformation("Hello {Name}", "world");
    }

    logFilePath = provider.LogFilePath;
}

Assert.NotNull(logFilePath);
Assert.Equal(["""{"Timestamp":"2024-01-02 03:04:05.000","LogLevel":"INFO","Category":"Sample.Program","Message":"Hello world","State":{"Name":"world","{OriginalFormat}":"Hello {Name}"},"Scopes":[{"Message":"Request 42","RequestId":42,"{OriginalFormat}":"Request {RequestId}"}]}"""], File.ReadAllLines(logFilePath));
```
<!-- endref -->

- The options above choose the properties: `Timestamp`, `LogLevel`, `Category`, `EventId` and `EventName`, `ThreadId`,
  `TraceId` and `SpanId`, and `Scopes`.
- `Message` is the formatted message, and `Exception` the exception, when there is one.
- `State` holds the parameters of the message template, including the template itself in `{OriginalFormat}`. Numbers,
  Booleans, strings, dates and GUIDs keep their JSON type; the other values are written as strings.
- A scope created from a message template is an object with its `Message` and its parameters; another scope is written
  as a string.

The JSON writer escapes the control characters, so an entry always spans a single line.

## Write your own formatter

Derive from [FileFormatter](cref:Meziantou.Extensions.Logging.FileFormatter) and write the entry to the `TextWriter` it receives. The provider adds the line terminator:

<!-- ref:../../../tests/Meziantou.Extensions.Logging.FileLogger.Tests/FileLoggerProviderTests.cs;region=FileFormatter_Example;format=md-fenced-code-block;dedent=true -->
```csharp
internal sealed class CsvFileFormatter() : FileFormatter("csv")
{
    public override void Write<TState>(in LogEntry<TState> logEntry, DateTimeOffset timestamp, FileLoggerOptions options, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        textWriter.Write(logEntry.LogLevel);
        textWriter.Write(',');
        textWriter.Write(logEntry.Formatter(logEntry.State, logEntry.Exception));
    }
}
```
<!-- endref -->

Set an instance in [FileLoggerOptions.Formatter](cref:Meziantou.Extensions.Logging.FileLoggerOptions.Formatter); it takes precedence over [FormatterName](cref:Meziantou.Extensions.Logging.FileLoggerOptions.FormatterName):

<!-- ref:../../../tests/Meziantou.Extensions.Logging.FileLogger.Tests/FileLoggerProviderTests.cs;region=FileLogger_CustomFormatter;format=md-fenced-code-block;dedent=true -->
```csharp
options.Formatter = new CsvFileFormatter();
```
<!-- endref -->

- [Write](cref:Meziantou.Extensions.Logging.FileFormatter.Write) runs on the thread that logs the message, so it can read the ambient state, such as `Activity.Current`.
- `timestamp` is already in the time zone selected by [UseUtcTimestamp](cref:Meziantou.Extensions.Logging.FileLoggerOptions.UseUtcTimestamp). `scopeProvider` is `null` when
  [IncludeScopes](cref:Meziantou.Extensions.Logging.FileLoggerOptions.IncludeScopes) is `false`.
- Writing nothing skips the entry.
- [GetLogLevelString](cref:Meziantou.Extensions.Logging.FileFormatter.GetLogLevelString) returns the four-letter names of the levels used by the built-in formatters.
