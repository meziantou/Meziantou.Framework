---
title: Asserting on the logs
description: Find log entries by level and content, read their parameters and scopes, share a collection between loggers and clear it.
---

## Find entries

[InMemoryLogCollection](cref:Meziantou.Extensions.Logging.InMemory.InMemoryLogCollection) is an `IEnumerable<InMemoryLogEntry>`, so the assertions and the LINQ methods that take a
collection work on it. It also has properties and methods for the common searches:

<!-- ref:../../../tests/Meziantou.Extensions.Logging.InMemory.Tests/InMemoryLoggerTests.cs;region=InMemoryLogCollection_Example;format=md-fenced-code-block;dedent=true -->
```csharp
var logger = InMemoryLogger.CreateLogger("sample");
logger.LogInformation("Application started");
logger.LogError("Error while processing the request");

// Filter by log level
var errors = logger.Logs.Errors;
var infos = logger.Logs.Informations;
Assert.Single(errors);
Assert.Single(infos);

// Search for specific entries
var entry = logger.Logs.Find(log => log.Message.StartsWith("Error", StringComparison.Ordinal));
Assert.NotNull(entry);
Assert.Equal(LogLevel.Error, entry.LogLevel);
```
<!-- endref -->

| Member | Returns |
| --- | --- |
| [Traces](cref:Meziantou.Extensions.Logging.InMemory.InMemoryLogCollection.Traces), [Debugs](cref:Meziantou.Extensions.Logging.InMemory.InMemoryLogCollection.Debugs), [Informations](cref:Meziantou.Extensions.Logging.InMemory.InMemoryLogCollection.Informations), [Warnings](cref:Meziantou.Extensions.Logging.InMemory.InMemoryLogCollection.Warnings), [Errors](cref:Meziantou.Extensions.Logging.InMemory.InMemoryLogCollection.Errors), [Criticals](cref:Meziantou.Extensions.Logging.InMemory.InMemoryLogCollection.Criticals) | The entries of that level |
| `Find(predicate)` | The first matching entry, or `null` |
| `Contains(predicate)` | Whether an entry matches |
| [Count](cref:Meziantou.Extensions.Logging.InMemory.InMemoryLogCollection.Count) | The number of entries |

The entries are in the order they were logged. The collection is thread-safe: code can log from several threads while a
test enumerates it.

## Read an entry

| Property | Value |
| --- | --- |
| [Message](cref:Meziantou.Extensions.Logging.InMemory.InMemoryLogEntry.Message) | The formatted message: `"User 123 logged in"` |
| `LogLevel` | The level |
| [Category](cref:Meziantou.Extensions.Logging.InMemory.InMemoryLogEntry.Category) | The category of the logger |
| [EventId](cref:Meziantou.Extensions.Logging.InMemory.InMemoryLogEntry.EventId) | The event identifier passed to the log call |
| [Exception](cref:Meziantou.Extensions.Logging.InMemory.InMemoryLogEntry.Exception) | The exception passed to the log call, or `null` |
| [State](cref:Meziantou.Extensions.Logging.InMemory.InMemoryLogEntry.State) | The state passed to the log call: for a message template, the list of its parameters |
| [Scopes](cref:Meziantou.Extensions.Logging.InMemory.InMemoryLogEntry.Scopes) | The scopes that were active when the entry was logged, outermost first |
| [CreatedAt](cref:Meziantou.Extensions.Logging.InMemory.InMemoryLogEntry.CreatedAt) | The time at which the entry was logged, in UTC |

[CreatedAt](cref:Meziantou.Extensions.Logging.InMemory.InMemoryLogEntry.CreatedAt) comes from `TimeProvider.System`, or from the `TimeProvider` passed to [CreateLogger](cref:Meziantou.Extensions.Logging.InMemory.InMemoryLoggerProvider.CreateLogger) or to the constructor
of [InMemoryLoggerProvider](cref:Meziantou.Extensions.Logging.InMemory.InMemoryLoggerProvider), so a test can control it.

## Read the parameters of the message

Assert on the values passed to the message template rather than on the formatted message, which depends on how the
values are formatted:

<!-- ref:../../../tests/Meziantou.Extensions.Logging.InMemory.Tests/InMemoryLoggerTests.cs;region=InMemoryLogEntry_Example;format=md-fenced-code-block;dedent=true -->
```csharp
var logger = InMemoryLogger.CreateLogger("sample");
logger.LogInformation("User {UserId} logged in", 123);

var log = logger.Logs.Informations.Single();
Assert.Equal("User 123 logged in", log.Message);
Assert.True(log.TryGetParameterValue("UserId", out var userId));
Assert.Equal(123, userId);
```
<!-- endref -->

## Read the scopes

[TryGetParameterValue](cref:Meziantou.Extensions.Logging.InMemory.InMemoryLogEntry.TryGetParameterValue) also looks for the parameter in the scopes:

<!-- ref:../../../tests/Meziantou.Extensions.Logging.InMemory.Tests/InMemoryLoggerTests.cs;region=InMemoryLogger_Scopes;format=md-fenced-code-block;dedent=true -->
```csharp
var logger = InMemoryLogger.CreateLogger("sample");
using (logger.BeginScope("Processing order {OrderId}", 42))
{
    logger.LogInformation("Item {ItemId} shipped", "A-1");
}

var log = Assert.Single(logger.Logs);
Assert.Equal("Processing order 42", Assert.Single(log.Scopes)?.ToString());

// The parameters are searched in the message first, then in the scopes
Assert.True(log.TryGetParameterValue("ItemId", out var itemId));
Assert.Equal("A-1", itemId);
Assert.True(log.TryGetParameterValue("OrderId", out var orderId));
Assert.Equal(42, orderId);
```
<!-- endref -->

- The names are case-sensitive. The parameters of the message are searched first, then the scopes, from the outermost
  one.
- A state or a scope that is a list of key/value pairs, such as a message template or a `Dictionary<string, object?>`,
  is searched by key. For any other object, such as an anonymous type, the value of the public property with that name
  is returned.
- `GetAllParameterValues(name)` returns the values found in the message and in every scope, when several of them use
  the same name.
- `{OriginalFormat}` is the message template itself: `TryGetParameterValue("{OriginalFormat}", out var template)`.

## Share a collection and clear it

Pass the same [InMemoryLogCollection](cref:Meziantou.Extensions.Logging.InMemory.InMemoryLogCollection) to several loggers to collect their entries together. [Clear](cref:Meziantou.Extensions.Logging.InMemory.InMemoryLogCollection.Clear) removes the entries
logged so far:

<!-- ref:../../../tests/Meziantou.Extensions.Logging.InMemory.Tests/InMemoryLoggerTests.cs;region=InMemoryLogger_SharedCollection;format=md-fenced-code-block;dedent=true -->
```csharp
var logs = new InMemoryLogCollection();
var orderLogger = InMemoryLogger.CreateLogger("Orders", logs);
var paymentLogger = InMemoryLogger.CreateLogger("Payments", logs);

orderLogger.LogInformation("Order created");
paymentLogger.LogWarning("Payment declined");

Assert.HasCount(2, logs);
Assert.Equal("Payments", Assert.Single(logs.Warnings).Category);

// Forget the entries logged so far, for instance after the setup of a test
logs.Clear();
Assert.Empty(logs);
```
<!-- endref -->

[InMemoryLoggerProvider](cref:Meziantou.Extensions.Logging.InMemory.InMemoryLoggerProvider) accepts a collection too, in its constructors.

## Show the entries in a failure message

`ToString` describes an entry: its category, its level, its event identifier, its message, its exception, its state and
its scopes serialized as JSON. [InMemoryLogCollection.ToString](cref:Meziantou.Extensions.Logging.InMemory.InMemoryLogCollection.ToString) describes every entry:

<!-- ref:../../../tests/Meziantou.Extensions.Logging.InMemory.Tests/InMemoryLoggerTests.cs;region=InMemoryLogger_ToString;format=md-fenced-code-block;dedent=true -->
```csharp
var logger = InMemoryLogger.CreateLogger("sample");
logger.LogWarning("Disk {Drive} is almost full", "C:");

// ToString describes the entry: use it in assertion messages, or to inspect the entries in the debugger
Assert.Equal("""
    [sample] Warning: Disk C: is almost full
      => [{"Key":"Drive","Value":"C:"},{"Key":"{OriginalFormat}","Value":"Disk {Drive} is almost full"}]
    """, Assert.Single(logger.Logs).ToString());
```
<!-- endref -->

`ToString` never throws: a value that cannot be serialized to JSON is written with its own `ToString`. To compare all the
entries with an expected text in one assertion, see [Snapshot testing](../../SnapshotTesting/01-Introduction.md).
