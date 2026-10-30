---
title: Logging and services
description: Send the logs of the mock to the test output, add services and configuration to the mock's application, and pick a constructor.
---

The mock is an ASP.NET Core application. Its constructors configure the logging and the services of that application.

## Log to the test output

The logs of the mock, such as the requests it receives, help understand why a request did not match. With the
`Meziantou.Extensions.Logging.Xunit.v3` package, send them to the output of the current xUnit test:

<!-- ref:../../tests/Meziantou.Framework.HttpClientMock.Tests/MockTests.cs;region=HttpClientMock_LogToTestOutput;format=md-fenced-code-block;dedent=true -->
```csharp
// The logs of the mock are written to the output of the current xUnit test
using var loggerProvider = new XUnitLoggerProvider(TestContext.Current.TestOutputHelper);
await using var mock = new HttpClientMock(loggerProvider);
mock.MapGet("/", () => "ok");

using var httpClient = mock.CreateHttpClient();
Assert.Equal("ok", await httpClient.GetStringAsync(new Uri("/", UriKind.Relative)));
```
<!-- endref -->

The constructors accept an `ILoggerProvider` or an `ILogger`. For more control, pass an `Action<ILoggingBuilder>`, for
instance to set the minimum level:

<!-- ref:../../tests/Meziantou.Framework.HttpClientMock.Tests/MockTests.cs;region=HttpClientMock_ConfigureLoggingLevel;format=md-fenced-code-block;dedent=true -->
```csharp
using var loggerProvider = new XUnitLoggerProvider(TestContext.Current.TestOutputHelper);
await using var mock = new HttpClientMock(logging =>
{
    logging.AddProvider(loggerProvider);
    logging.SetMinimumLevel(LogLevel.Trace);
});
```
<!-- endref -->

Without a logger, the mock logs nothing. To assert
on logs, see [Logging](../Logging/01-Introduction.md).

## Add services and configuration

`configureServices` adds services to the application of the mock. Handlers get them as parameters, as in a minimal API.
The mock does not read the configuration of the host: `appsettings.json`, the `ASPNETCORE_*` and `DOTNET_*`
environment variables and the command line are ignored, so a mock behaves the same on every machine. Add the
configuration it needs:

<!-- ref:../../tests/Meziantou.Framework.HttpClientMock.Tests/MockTests.cs;region=HttpClientMock_Configuration;format=md-fenced-code-block;dedent=true -->
```csharp
await using var mock = new HttpClientMock(configureLogging: null, configureServices: services =>
{
    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Greeting"] = "Hello" })
        .Build();
    services.AddSingleton<IConfiguration>(configuration);
});

mock.MapGet("/greeting", (IConfiguration configuration) => configuration["Greeting"]);
```
<!-- endref -->

The application of the mock runs in the `Production` environment, with the directory of the test assembly as content
root.

## Choose a constructor

| Constructor | Use it to |
| --- | --- |
| `HttpClientMock()` | Create a mock without logs |
| `HttpClientMock(ILoggerProvider?)` | Send the logs to a logger provider |
| `HttpClientMock(ILogger?)` | Send the logs to a logger |
| `HttpClientMock(Action<ILoggingBuilder>?)` | Configure the logging |
| `HttpClientMock(ILoggerProvider?, Action<IServiceCollection>?)` | Send the logs to a logger provider and add services |
| `HttpClientMock(ILogger?, Action<IServiceCollection>?)` | Send the logs to a logger and add services |
| `HttpClientMock(Action<ILoggingBuilder>?, Action<IServiceCollection>?)` | Configure the logging and add services |

A bare `null` matches several of these constructors, and does not compile. Name the argument to choose one:
`new HttpClientMock(configureLogging: null, configureServices: services => ...)`.

## Startup errors

The mock starts when the first client or handler is created. When the application fails to start, for instance because
a hosted service added with `configureServices` throws while starting, `CreateHttpClient` and
`CreateHttpMessageHandler` throw that error.
