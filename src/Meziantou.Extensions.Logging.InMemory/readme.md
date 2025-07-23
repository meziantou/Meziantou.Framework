# Meziantou.Extensions.Logging.InMemory

```c#
using var loggerProvider = new InMemoryLoggerProvider();
var logger = loggerProvider.CreateLogger("sample");
var typedLogger = loggerProvider.CreateLogger<Sample>();

// Short version if you need a single logger instance:
var singleLogger = InMemoryLogger.CreateLogger("sample");
var singleTypedLogger = InMemoryLogger.CreateLogger<Sample>();

// do stuff with the logger

// Assert
Assert.Empty(loggerProvider.Logs.Errors);
Assert.Single(loggerProvider.Logs, log => log.Message.Contains("test") && log.EventId.Id == 1);
```

If you are using a `WebApplicationFactory`:

```c#
using var loggerProvider = new InMemoryLoggerProvider();
using var factory = new WebApplicationFactory<Program>()
    .WithWebHostBuilder(builder =>
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

Blog post about testing logging: [How to test the logs from ILogger in .NET](https://www.meziantou.net/how-to-test-the-logs-from-ilogger-in-dotnet.htm)