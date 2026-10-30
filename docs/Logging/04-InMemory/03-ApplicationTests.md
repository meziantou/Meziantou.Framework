---
title: Testing an application
description: Collect the logs of a service collection, a host or an ASP.NET Core application with InMemoryLoggerProvider.
---

## Register the provider

Add an [InMemoryLoggerProvider](cref:Meziantou.Extensions.Logging.InMemory.InMemoryLoggerProvider) to the logging builder, then read `provider.Logs`: it collects the entries of every
logger the application creates.

<!-- ref:../../../tests/Meziantou.Extensions.Logging.InMemory.Tests/InMemoryLoggerTests.cs;region=InMemoryLoggerProvider_Example;format=md-fenced-code-block;dedent=true -->
```csharp
// Use with dependency injection
var services = new ServiceCollection();
using var provider = new InMemoryLoggerProvider();
services.AddLogging(builder => builder.AddProvider(provider));

using var serviceProvider = services.BuildServiceProvider();
var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("MyCategory");
logger.LogInformation("Test message");

// Access logs through the provider
var log = Assert.Single(provider.Logs.Informations);
Assert.Equal("Test message", log.Message);
```
<!-- endref -->

The provider receives the scopes from the logger factory, so the entries have the scopes created by the application,
including the ones the factory adds for the current activity when `ActivityTrackingOptions` is set.

## Choose the levels to collect

The filters of the logging configuration apply to the provider. `AddLogging` and the hosts set the minimum level to
`Information`, so the `Debug` and `Trace` entries are not collected unless you lower it with `SetMinimumLevel` or
`AddFilter` in the logging builder.

## Test an ASP.NET Core application

With a `WebApplicationFactory` (from the `Microsoft.AspNetCore.Mvc.Testing` package), register the provider as a
singleton `ILoggerProvider` in the logging builder of the application:

<!-- ref:../../../samples/Meziantou.Extensions.Logging.InMemory.Samples/ReadmeExamples.cs;region=InMemoryLogger_WebApplicationFactory;format=md-fenced-code-block;dedent=true -->
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

The application runs in the test process, so its log calls add entries to `loggerProvider.Logs` while the requests of
the test run. To also see the logs in the output of the test, add the
[xUnit.net logger](../05-Xunit/03-DependencyInjection.md) to the same logging builder.
