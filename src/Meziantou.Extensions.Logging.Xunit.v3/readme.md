# Meziantou.Extensions.Logging.Xunit.v3

## Statically create XUnitLogger or XUnitLogger\<T\>

```c#
ILogger logger = XUnitLogger.CreateLogger();
ILogger<MyType> logger = XUnitLogger.CreateLogger<MyType>();
```

## Statically create XUnitLogger or XUnitLogger\<T\> by passing an existing ITestOutputHelper

```c#
ILogger logger = XUnitLogger.CreateLogger(testOutputHelper);
ILogger<MyType> logger = XUnitLogger.CreateLogger<MyType>(testOutputHelper);
```

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
                    builder.Services.AddSingleton<ILoggerProvider>(new XUnitLoggerProvider(testOutputHelper, appendScope: false));
                });
            });
    }
}
```

Blog post about this package: [How to write logs from ILogger to xUnit.net ITestOutputHelper](https://www.meziantou.net/how-to-view-logs-from-ilogger-in-xunitdotnet.htm)
