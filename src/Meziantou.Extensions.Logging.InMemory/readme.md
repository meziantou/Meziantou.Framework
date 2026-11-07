# Meziantou.Extensions.Logging.InMemory

An `ILogger` that keeps the log entries in memory, so tests can assert on them. The types are in the
`Meziantou.Extensions.Logging.InMemory` namespace.

<!-- ref:../../tests/Meziantou.Extensions.Logging.InMemory.Tests/InMemoryLoggerTests.cs;region=InMemoryLogger_Usage;format=md-fenced-code-block;dedent=true -->
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

If you are using a `WebApplicationFactory` (from the `Microsoft.AspNetCore.Mvc.Testing` package):

<!-- ref:../../samples/Meziantou.Extensions.Logging.InMemory.Samples/ReadmeExamples.cs;region=InMemoryLogger_WebApplicationFactory;format=md-fenced-code-block;dedent=true -->
```csharp
using var loggerProvider = new InMemoryLoggerProvider();
using var application = new WebApplicationFactory<Program>();
using var factory = application.WithWebHostBuilder(builder =>
{
    builder.ConfigureLogging(builder =>
    {
        // You can override the logging configuration if needed
        //builder.SetMinimumLevel(LogLevel.Trace);
        //builder.AddFilter(_ => true);

        builder.Services.AddSingleton<ILoggerProvider>(loggerProvider);
    });
});
```
<!-- endref -->

Blog post about testing logging: [How to test the logs from ILogger in .NET](https://www.meziantou.net/how-to-test-the-logs-from-ilogger-in-dotnet.htm)