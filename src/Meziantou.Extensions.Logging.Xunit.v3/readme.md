# Meziantou.Extensions.Logging.Xunit.v3

## Statically create XUnitLogger or XUnitLogger\<T\>

```c#
ILogger logger = XUnitLogger.CreateLogger();
ILogger<MyType> logger = XUnitLogger.CreateLogger<MyType>();
```

These overloads do not take an `ITestOutputHelper`. The logger resolves `TestContext.Current.TestOutputHelper`
each time it writes, so it always targets the test that is currently running. This is the recommended way to log
from code whose lifetime is not tied to a single test, such as a host or a fixture shared by several tests.

## Statically create XUnitLogger or XUnitLogger\<T\> by passing an existing ITestOutputHelper

```c#
ILogger logger = XUnitLogger.CreateLogger(testOutputHelper);
ILogger<MyType> logger = XUnitLogger.CreateLogger<MyType>(testOutputHelper);
```

A logger created this way writes to that specific helper. If the helper belongs to a test that has already
finished, the log record is dropped instead of throwing.

## Configure the output

Every entry point accepts an optional `XUnitLoggerOptions`:

```c#
ILogger logger = XUnitLogger.CreateLogger(testOutputHelper, new XUnitLoggerOptions
{
    IncludeLogLevel = true,
    IncludeCategory = true,
    IncludeScopes = true,
    TimestampFormat = "HH:mm:ss.fff",
    UseUtcTimestamp = true,
});

// 12:20:41.812 warn [MyNamespace.MyType] Something happened
//  => TheScope
```

| Property | Default | Effect |
| --- | --- | --- |
| `IncludeLogLevel` | `false` | Prefixes the record with `trce`, `dbug`, `info`, `warn`, `fail` or `crit`. |
| `IncludeCategory` | `false` | Prefixes the record with `[CategoryName]`. |
| `IncludeScopes` | `false` | Appends the active scopes, one per line, prefixed with ` => `. |
| `TimestampFormat` | `null` | Date and time format string. No timestamp is written when `null`. |
| `UseUtcTimestamp` | `false` | Formats the timestamp in UTC instead of local time. |

## Register the logger with dependency injection

```c#
var host = new HostBuilder()
    .ConfigureLogging(builder =>
    {
        builder.AddXunit();
    })
    .Build();
```

`AddXunit` has four overloads: no argument, an `ITestOutputHelper`, an `XUnitLoggerOptions`, or both. Use the
parameterless one to write to whichever test is running, and pass an `ITestOutputHelper` only when you need to
target a specific one.

Because `ITestOutputHelper` and `XUnitLoggerOptions` are both optional reference types, `AddXunit(null)` is
ambiguous and does not compile. Call `AddXunit()` to say "no test output helper", or name the argument
(`AddXunit(testOutputHelper: null)`) if you are passing one through. The same applies to the
`XUnitLoggerProvider` constructors.

## Using WebApplicationFactory

If you are using a `WebApplicationFactory`:

```c#
public class UnitTest1(ITestOutputHelper testOutputHelper)
{
    [Fact]
    public async Task Test1()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
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
    }
}
```

If the factory outlives the test that created it, prefer `builder.AddXunit()` without the helper so each record
goes to the test that is running when it is written.

Blog post about this package: [How to write logs from ILogger to xUnit.net ITestOutputHelper](https://www.meziantou.net/how-to-view-logs-from-ilogger-in-xunitdotnet.htm)
