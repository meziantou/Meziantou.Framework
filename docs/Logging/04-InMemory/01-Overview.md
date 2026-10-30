---
title: Overview
description: An ILogger that keeps the log entries in memory, so tests can assert on what the code logged.
---

`Meziantou.Extensions.Logging.InMemory` provides a logger that keeps every entry in a collection, with its level, its
category, its message, its parameters and its scopes. Tests pass the logger to the code under test, then assert on the
entries.

<!-- ref:../../../tests/Meziantou.Extensions.Logging.InMemory.Tests/InMemoryLoggerTests.cs;region=InMemoryLogger_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
using var loggerProvider = new InMemoryLoggerProvider();
var logger = loggerProvider.CreateLogger("sample");
var typedLogger = loggerProvider.CreateLogger<Sample>();

// Short version if you need a single logger instance:
var singleLogger = InMemoryLogger.CreateLogger("sample");
var singleTypedLogger = InMemoryLogger.CreateLogger<Sample>();

// do stuff with the logger
logger.LogInformation(new EventId(1), "This is a test");
typedLogger.LogWarning("Something to check");
singleLogger.LogInformation("Only in the logs of singleLogger");

Assert.Empty(loggerProvider.Logs.Errors);
Assert.Single(loggerProvider.Logs, log => log.Message.Contains("test", StringComparison.Ordinal) && log.EventId.Id == 1);
Assert.Single(loggerProvider.Logs.Warnings);
Assert.Single(singleLogger.Logs);
Assert.Empty(singleTypedLogger.Logs);
```
<!-- endref -->

There are two ways to create a logger:

- `InMemoryLogger.CreateLogger(category)` and `InMemoryLogger.CreateLogger<T>()` create a single logger with a
  collection of its own. They return an `IInMemoryLogger` or an `IInMemoryLogger<T>`, which are an `ILogger` or an
  `ILogger<T>` with a `Logs` property.
- `InMemoryLoggerProvider` is an `ILoggerProvider`: every logger it creates writes to the same collection,
  `provider.Logs`. Use it with a logger factory, a host or a web application. See
  [Testing an application](03-ApplicationTests.md).

The category of `CreateLogger<T>()` is the full name of `T`, as with `ILogger<T>`.

The logger accepts every level but `LogLevel.None`. When it is created by a logger factory or a host, the filters of
the logging configuration apply as for any provider.

## Pages

- [Asserting on the logs](02-AssertingOnLogs.md): find entries by level and content, read their parameters and
  scopes, and clear the collection.
- [Testing an application](03-ApplicationTests.md): collect the logs of a host or of an ASP.NET Core application.
