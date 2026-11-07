---
title: Getting started
description: Install a logging package and write your first entries to a file, to memory, or to the test output.
---

The examples of this documentation omit the `using` directives: see
[Namespaces](01-Introduction.md#namespaces).

## Write the logs of an application to files

Add the package:

<InstallNuGetPackage Id="Meziantou.Extensions.Logging.FileLogger" />

Register the provider on the logging builder of the host. `Host` is in the `Microsoft.Extensions.Hosting` namespace:

<!-- ref:../../samples/Meziantou.Extensions.Logging.FileLogger.Samples/Examples.cs;region=FileLoggerBuilderExtensions_AddFile;format=md-fenced-code-block;dedent=true -->
```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddFile(options =>
{
    options.Directory = "logs";
    options.RollInterval = RollInterval.Daily;
    options.MaxRetainedFiles = 7;
});
```
<!-- endref -->

The messages are written to the `logs` directory, in a new file every day, and only the last 7 files are kept. See
[File logger](03-FileLogger/01-Overview.md).

## Assert on the logs in a test

Add the package to the test project:

<InstallNuGetPackage Id="Meziantou.Extensions.Logging.InMemory" />

Create a logger, pass it to the code under test, and assert on the entries it collected:

<!-- ref:../../tests/Meziantou.Extensions.Logging.InMemory.Tests/InMemoryLoggerTests.cs;region=InMemoryLogger_Example;format=md-fenced-code-block;dedent=true -->
```csharp
// Create a logger directly
var logger = InMemoryLogger.CreateLogger("MyCategory");
logger.LogInformation("Test message");

// Access captured logs
Assert.Single(logger.Logs.Informations);
Assert.Contains(logger.Logs, log => log.Message == "Test message");
```
<!-- endref -->

See [In-memory logger](04-InMemory/01-Overview.md).

## Show the logs in the output of a test

Add the package to a test project that uses xUnit.net v3:

<InstallNuGetPackage Id="Meziantou.Extensions.Logging.Xunit.v3" />

Create a logger that writes to the output of the test:

<!-- ref:../../tests/Meziantou.Extensions.Logging.Xunit.v3.Tests/XunitLoggerTests.cs;region=XUnitLogger_Example;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class MyTests(ITestOutputHelper testOutputHelper)
{
    [Fact]
    public void MyTest()
    {
        var logger = XUnitLogger.CreateLogger(testOutputHelper);
        logger.LogInformation("This message will appear in the test output");
    }
}
```
<!-- endref -->

The message is shown with the result of the test. See [xUnit.net logger](05-Xunit/01-Overview.md).
