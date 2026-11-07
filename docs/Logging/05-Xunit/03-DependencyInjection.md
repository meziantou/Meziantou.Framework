---
title: Dependency injection
description: Register the xUnit.net logger in a host or in an ASP.NET Core application under test, and filter its entries.
---

## Register the logger in a host

[AddXunit](cref:Meziantou.Extensions.Logging.Xunit.v3.XUnitLoggerBuilderExtensions.AddXunit) adds the provider to a logging builder:

<!-- ref:../../../tests/Meziantou.Extensions.Logging.Xunit.v3.Tests/XunitLoggerTests.cs;region=XUnitLogger_AddXunit;format=md-fenced-code-block;dedent=true -->
```csharp
using var host = new HostBuilder()
    .ConfigureLogging(builder =>
    {
        builder.AddXunit();
    })
    .Build();
```
<!-- endref -->

| Overload | Writes to |
| --- | --- |
| [AddXunit()](cref:Meziantou.Extensions.Logging.Xunit.v3.XUnitLoggerBuilderExtensions.AddXunit) | The test that is running |
| `AddXunit(options)` | The test that is running, with [output options](02-Options.md) |
| `AddXunit(testOutputHelper)` | That test |
| `AddXunit(testOutputHelper, options)` | That test, with output options |

Use the parameterless overload to write to whichever test is running, and pass an `ITestOutputHelper` only when you need
to target a specific test. Calling [AddXunit()](cref:Meziantou.Extensions.Logging.Xunit.v3.XUnitLoggerBuilderExtensions.AddXunit) several times registers a single provider, while each call that takes
an argument registers one more.

`ITestOutputHelper` and [XUnitLoggerOptions](cref:Meziantou.Extensions.Logging.Xunit.v3.XUnitLoggerOptions) are both optional reference types, so `AddXunit(null)` is ambiguous and
does not compile. Call [AddXunit()](cref:Meziantou.Extensions.Logging.Xunit.v3.XUnitLoggerBuilderExtensions.AddXunit), or name the argument (`AddXunit(testOutputHelper: null)`) when you pass a variable
that may be `null`. The same applies to the constructors of [XUnitLoggerProvider](cref:Meziantou.Extensions.Logging.Xunit.v3.XUnitLoggerProvider), which you can also register yourself
as a singleton `ILoggerProvider`.

## Filter the entries

The provider alias is `XUnit`. The filters of the logging configuration apply as for any provider, so
`SetMinimumLevel` sets the levels written to the test output, and the configuration can filter this provider alone:

```json
{
  "Logging": {
    "XUnit": {
      "LogLevel": {
        "Default": "Warning"
      }
    }
  }
}
```

## Test an ASP.NET Core application

With a `WebApplicationFactory` (from the `Microsoft.AspNetCore.Mvc.Testing` package), add the logger in the logging
builder of the application:

<!-- ref:../../../samples/Meziantou.Extensions.Logging.Xunit.v3.Samples/UnitTest1.cs;region=XUnitLogger_WebApplicationFactory;format=md-fenced-code-block;dedent=true -->
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

If the factory outlives the test that created it, for instance when it is shared through a fixture, call
`builder.AddXunit()` without the helper, so each entry goes to the test that is running when it is logged. To also
assert on the entries, add the [in-memory logger](../04-InMemory/03-ApplicationTests.md) to the same logging builder.
