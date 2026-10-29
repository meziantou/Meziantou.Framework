---
title: Introduction
description: Three ILogger providers, to write the logs to files, to keep them in memory for tests, or to write them to the xUnit.net test output.
---

The logging packages of Meziantou.Framework are providers for `Microsoft.Extensions.Logging`. Your code keeps logging
through `ILogger` and `ILogger<T>`; the provider decides where the entries go.

| Package | Writes the entries to | Typical use |
| --- | --- | --- |
| `Meziantou.Extensions.Logging.FileLogger` | Files on disk, written by a background thread | Applications and services |
| `Meziantou.Extensions.Logging.InMemory` | A collection in memory | Tests that assert on the logs |
| `Meziantou.Extensions.Logging.Xunit.v3` | The output of the running xUnit.net v3 test | Tests that show the logs of the code under test |

Each package is independent: install only the ones you need. The test packages are often used together: the in-memory
logger to assert on the entries, and the xUnit.net logger to read them when a test fails.

## What you can do with them

- **Write the logs to files** without blocking the application, with one file per hour, day or month, a size limit,
  retention and compression. See [File logger](03-FileLogger/01-Overview.md).
- **Assert on the logs in tests**: find the entries by level, message, category or parameter value. See
  [In-memory logger](04-InMemory/01-Overview.md).
- **Show the logs in the test output**, for the test that is running, including from a host or a web application
  started by the test. See [xUnit.net logger](05-Xunit/01-Overview.md).

## Namespaces

| Package | Namespace |
| --- | --- |
| `Meziantou.Extensions.Logging.FileLogger` | `Meziantou.Extensions.Logging` |
| `Meziantou.Extensions.Logging.InMemory` | `Meziantou.Extensions.Logging.InMemory` |
| `Meziantou.Extensions.Logging.Xunit.v3` | `Meziantou.Extensions.Logging.Xunit.v3` |

`LoggerFactory`, `ILogger` and the `LogInformation` extension methods are in the `Microsoft.Extensions.Logging`
namespace.

## Related sections

- [Snapshot testing](../14-SnapshotTesting/01-Introduction.md) and
  [Inline snapshot testing](../15-InlineSnapshotTesting/01-Introduction.md) compare the entries collected by the
  in-memory logger with a snapshot instead of writing one assertion per entry.
- [OpenTelemetry collector](../83-OpenTelemetryCollector/01-Introduction.md) receives the logs, traces and metrics that
  an application exports with OpenTelemetry, to test them.

## Next steps

- [Getting started](02-GettingStarted.md): install a package and write your first log entry.
