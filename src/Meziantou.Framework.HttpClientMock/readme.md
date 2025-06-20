# Meziantou.Framework.HttpClientMock

The `HttpClientMock` class allows you to mock an `HttpClient` instance. You can configure the mock to return a specific response for a given request. You can also forward the request to an upstream server.
It uses ASP.NET Core with the TestServer to handle the requests, so you can easily define the responses using the same syntax as you would in an ASP.NET Core application.

## Configuring HttpClientMock

You can use the `MapGet`, `MapPost`, and so on, methods to define the response for a given request. You can also use the `ForwardUnknownRequestsToUpstream` method to forward the request to upstream server.
The url provided to the Map methods can be relative or absolute. It can also contains a query string.

````c#
await using var mock = new HttpClientMock();
mock.MapGet("/", () => Results.Ok("test"));
mock.MapGet("/todos/{id}", (int id) => Results.Ok("test"));
mock.MapGet("/todos?search=abc", () => Results.Ok("test"));
mock.MapGet("/todos", (string search) => Results.Ok("test"));

mock.MapGet("https://example.com/path?search=text", () => Results.Ok("test"));

// Return raw content
mock.MapGet("/json", () => Results.Extensions.RawJson("""{"id": 1}"""));
mock.MapGet("/xml", () => Results.Extensions.RawXml("""<root></root>"""));

// Forward the request to the upstream server
mock.MapGet("/upstream", () => Results.Extensions.ForwardToUpstream());

// Forward any request that doesn't match a configure route to the upstream server
mock.ForwardUnknownRequestsToUpstream();
````

The  `RequestCounter` class allows you to count the number of requests per endpoint. You can use it to test if a specific endpoint has been called.

````c#
mock.MapGet("/counter", (RequestCounter counter) => counter.Get());
````

## Use the mock

There are multiple ways to use the mock depending on your needs:

- Get an `HttpClient` instance from the mock

    ```c#
    using var httpClient = mock.CreateHttpClient();
    ```

- Get an `HttpMessageHandler` instance from the mock

    ```c#
    using var httpMessageHandler = mock.CreateHttpMessageHandler();
    ```

- Register the mock in the service collection (Dependency Injection)

    ````c#
    var services = new ServiceCollection();
    services.AddHttpClient();
    services.AddHttpClient<SampleClient>();

    // Mock both HttpClient
    services.AddHttpClientMock(builder => builder
        .AddHttpClientMock(mock)
        .AddHttpClientMock<SampleClient>(mock));

    // Alternative way to register the mock
    services.AddHttpClient<SampleClient>()
        .ConfigurePrimaryHttpMessageHandler(() => mock.CreateHttpMessageHandler());
    ````

# Use resiliency policies when forwarding requests

When forwarding requests to an upstream server, you can use resiliency policies to handle transient failures. You can use the `Microsoft.Extensions.Http.Resilience` package.

````c#
await using var mock = new HttpClientMock(configureLogging: null, configureServices: services =>
{
    services.ConfigureHttpClientDefaults(services => services.AddStandardResilienceHandler());
});
````

# Forward logs to xUnit ITestOutputHelper

You can forward logs to the xUnit `ITestOutputHelper`. This can be useful to debug issues with the mock.

1. Add the `Meziantou.Extensions.Logging.Xunit` package to your project
1.

````c#
using var loggerProvider = new XUnitLoggerProvider(testOutputHelper);
await using var mock = new HttpClientMock(loggerProvider);
````

If you need more controls about logging, you can use the `configureLogging` parameter of the constructor.

````c#
using var loggerProvider = new XUnitLoggerProvider(testOutputHelper);
await using var mock = new HttpClientMock(logs =>
{
    logs.AddProvider(loggerProvider);
    logs.SetMinimumLevel(LogLevel.Trace);
});
````
