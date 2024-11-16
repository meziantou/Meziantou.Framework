# Meziantou.Extensions.Logging.Xunit

```c#
using var loggerProvider = new InMemoryLoggerProvider();
var logger = loggerProvider.CreateLogger("MyLogger");

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
            builder.Services.AddSingleton<ILoggerProvider>(loggerProvider);
        });
    });
```

Blog post about testing logging: [How to test the logs from ILogger in .NET](https://www.meziantou.net/how-to-test-the-logs-from-ilogger-in-dotnet.htm)