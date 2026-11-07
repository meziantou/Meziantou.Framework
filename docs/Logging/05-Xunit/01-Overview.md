---
title: Overview
description: An ILogger that writes to the output of the xUnit.net v3 test that is running.
---

`Meziantou.Extensions.Logging.Xunit.v3` provides a logger that writes each entry to the output of an xUnit.net v3 test.
The logs of the code under test are then shown with the result of the test, which helps understand why it failed.

## Write to the test that is running

The parameterless overloads create a logger that writes to the test that is running when the entry is logged:

<!-- ref:../../../tests/Meziantou.Extensions.Logging.Xunit.v3.Tests/XunitLoggerTests.cs;region=XUnitLogger_CreateLogger;format=md-fenced-code-block;dedent=true -->
```csharp
ILogger logger = XUnitLogger.CreateLogger();
ILogger<MyType> typedLogger = XUnitLogger.CreateLogger<MyType>();
```
<!-- endref -->

The logger reads `TestContext.Current.TestOutputHelper` every time it writes. This is the recommended way to log from
code whose lifetime is not tied to a single test, such as a host or a fixture shared by several tests: each entry goes
to the test that is running. An entry logged while no test runs is dropped.

The category of [CreateLogger\<T\>()](cref:Meziantou.Extensions.Logging.Xunit.v3.XUnitLogger.CreateLogger) is the full name of `T`.

## Write to a given test

Pass an `ITestOutputHelper` to write to that test only:

<!-- ref:../../../tests/Meziantou.Extensions.Logging.Xunit.v3.Tests/XunitLoggerTests.cs;region=XUnitLogger_CreateLoggerWithTestOutputHelper;format=md-fenced-code-block;dedent=true -->
```csharp
ILogger logger = XUnitLogger.CreateLogger(testOutputHelper);
ILogger<MyType> typedLogger = XUnitLogger.CreateLogger<MyType>(testOutputHelper);
```
<!-- endref -->

xUnit.net injects the `ITestOutputHelper` of the test into the constructor of the test class, as in
[Getting started](../02-GettingStarted.md#show-the-logs-in-the-output-of-a-test). When the test the helper belongs to
has already finished, the entry is dropped instead of throwing.

## Pages

- [Output options](02-Options.md): add the level, the category, the scopes and a timestamp to the entries.
- [Dependency injection](03-DependencyInjection.md): register the logger in a host or an ASP.NET Core application under
  test.

The logger writes text for people to read. To assert on the entries, use the
[in-memory logger](../04-InMemory/01-Overview.md).
