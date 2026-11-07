# Meziantou.Extensions.Logging.Xunit.v3

An `ILogger` that writes to the xUnit.net v3 test output. The types are in the `Meziantou.Extensions.Logging.Xunit.v3`
namespace.

## Statically create XUnitLogger or XUnitLogger\<T\>

<!-- ref:../../tests/Meziantou.Extensions.Logging.Xunit.v3.Tests/XunitLoggerTests.cs;region=XUnitLogger_CreateLogger;format=md-fenced-code-block;dedent=true -->
```csharp
ILogger logger = XUnitLogger.CreateLogger();
ILogger<MyType> typedLogger = XUnitLogger.CreateLogger<MyType>();
```
<!-- endref -->

These overloads do not take an `ITestOutputHelper`. The logger resolves `TestContext.Current.TestOutputHelper`
each time it writes, so it always targets the test that is currently running. This is the recommended way to log
from code whose lifetime is not tied to a single test, such as a host or a fixture shared by several tests.

## Statically create XUnitLogger or XUnitLogger\<T\> by passing an existing ITestOutputHelper

<!-- ref:../../tests/Meziantou.Extensions.Logging.Xunit.v3.Tests/XunitLoggerTests.cs;region=XUnitLogger_CreateLoggerWithTestOutputHelper;format=md-fenced-code-block;dedent=true -->
```csharp
ILogger logger = XUnitLogger.CreateLogger(testOutputHelper);
ILogger<MyType> typedLogger = XUnitLogger.CreateLogger<MyType>(testOutputHelper);
```
<!-- endref -->

A logger created this way writes to that specific helper. If the helper belongs to a test that has already
finished, the log record is dropped instead of throwing.

## Configure the output

Every entry point accepts an optional `XUnitLoggerOptions`:

<!-- ref:../../tests/Meziantou.Extensions.Logging.Xunit.v3.Tests/XunitLoggerTests.cs;region=XUnitLogger_Options;format=md-fenced-code-block;dedent=true -->
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
| `IncludeLogLevel` | `false` | Prefixes the record with `trce`, `dbug`, `info`, `warn`, `fail` or `crit`. |
| `IncludeCategory` | `false` | Prefixes the record with `[CategoryName]`. |
| `IncludeScopes` | `false` | Appends the active scopes, one per line, prefixed with ` => `. |
| `TimestampFormat` | `null` | Date and time format string. No timestamp is written when `null`. |
| `UseUtcTimestamp` | `false` | Formats the timestamp in UTC instead of local time. |

## Register the logger with dependency injection

<!-- ref:../../tests/Meziantou.Extensions.Logging.Xunit.v3.Tests/XunitLoggerTests.cs;region=XUnitLogger_AddXunit;format=md-fenced-code-block;dedent=true -->
```csharp
using var host = new HostBuilder()
    .ConfigureLogging(builder =>
    {
        builder.AddXunit();
    })
    .Build();
```
<!-- endref -->

`AddXunit` has four overloads: no argument, an `ITestOutputHelper`, an `XUnitLoggerOptions`, or both. Use the
parameterless one to write to whichever test is running, and pass an `ITestOutputHelper` only when you need to
target a specific one.

Because `ITestOutputHelper` and `XUnitLoggerOptions` are both optional reference types, `AddXunit(null)` is
ambiguous and does not compile. Call `AddXunit()` to say "no test output helper", or name the argument
(`AddXunit(testOutputHelper: null)`) if you are passing one through. The same applies to the
`XUnitLoggerProvider` constructors.

## Using WebApplicationFactory

If you are using a `WebApplicationFactory` (from the `Microsoft.AspNetCore.Mvc.Testing` package):

<!-- ref:../../samples/Meziantou.Extensions.Logging.Xunit.v3.Samples/UnitTest1.cs;region=XUnitLogger_WebApplicationFactory;format=md-fenced-code-block;dedent=true -->
```csharp
public class UnitTest1(ITestOutputHelper testOutputHelper)
{
    [Fact]
    public async Task Test1()
    {
        using var application = new WebApplicationFactory<Program>();
        using var factory = application.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(builder =>
            {
                // You can override the logging configuration if needed
                //builder.SetMinimumLevel(LogLevel.Trace);
                //builder.AddFilter(_ => true);

                // Register the xUnit logger provider
                builder.AddXunit(testOutputHelper, new XUnitLoggerOptions { IncludeScopes = true });
            });
        });

        using var client = factory.CreateClient();
        _ = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);
    }
}
```
<!-- endref -->

If the factory outlives the test that created it, prefer `builder.AddXunit()` without the helper so each record
goes to the test that is running when it is written.

Blog post about this package: [How to write logs from ILogger to xUnit.net ITestOutputHelper](https://www.meziantou.net/how-to-view-logs-from-ilogger-in-xunitdotnet.htm)
