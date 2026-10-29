---
title: Output options
description: Add the log level, the category, the scopes and a timestamp to the entries written to the test output.
---

By default, an entry is its message, followed by its exception. Every way to create a logger accepts an optional
`XUnitLoggerOptions` to add more:

<!-- ref:../../../tests/Meziantou.Extensions.Logging.Xunit.v3.Tests/XunitLoggerTests.cs;region=XUnitLogger_Options;format=md-fenced-code-block;dedent=true -->
```csharp
ILogger<MyType> logger = XUnitLogger.CreateLogger<MyType>(testOutputHelper, new XUnitLoggerOptions
{
    IncludeLogLevel = true,
    IncludeCategory = true,
    IncludeScopes = true,
    TimestampFormat = "HH:mm:ss.fff",
    UseUtcTimestamp = true,
});

using (logger.BeginScope("TheScope"))
{
    logger.LogWarning("Something happened");
}

// 12:20:41.812 warn [MyNamespace.MyType] Something happened
//  => TheScope
```
<!-- endref -->

| Property | Default | Effect |
| --- | --- | --- |
| `TimestampFormat` | `null` | A date and time format string; the timestamp is written first. No timestamp when `null` |
| `UseUtcTimestamp` | `false` | Formats the timestamp in UTC instead of the local time |
| `IncludeLogLevel` | `false` | Writes `trce`, `dbug`, `info`, `warn`, `fail` or `crit` |
| `IncludeCategory` | `false` | Writes `[CategoryName]` |
| `IncludeScopes` | `false` | Writes the active scopes after the message, one per line, prefixed with ` => ` |

The segments are always in the same order: timestamp, level, category, message, scopes, then the exception on the next
lines. The scopes come before the exception, so a long stack trace does not push them out of view.
