---
title: Dependency injection
description: Replace the default, named and typed clients of IHttpClientFactory with mocks, and fail on the clients you did not mock.
---

When the code under test gets its clients from `IHttpClientFactory`, `AddHttpClientMock` replaces the handlers of the
clients you choose with mocks. The clients are still created by the factory, so their configuration, such as their base
address, is kept.

## Named clients and the default client

<!-- ref:../../tests/Meziantou.Framework.HttpClientMock.Tests/MockTests.cs;region=HttpClientMock_NamedClients;format=md-fenced-code-block;dedent=true -->
```csharp
await using var mock = new HttpClientMock();
mock.MapGet("https://api.example.com/user", () => "John");

var services = new ServiceCollection();
services.AddHttpClient("api");
services.AddHttpClient("other");
services.AddHttpClientMock(builder => builder
    .AddHttpClientMock("api", mock)
    .ThrowOnUnknownHttpClient());

await using var serviceProvider = services.BuildServiceProvider();
var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

using var apiClient = httpClientFactory.CreateClient("api");
Assert.Equal("John", await apiClient.GetStringAsync(new Uri("https://api.example.com/user")));

// No mock is registered for "other": creating it throws instead of sending real requests
Assert.Throws<InvalidOperationException>(() => httpClientFactory.CreateClient("other"));
```
<!-- endref -->

- `AddHttpClientMock(name, mock)` mocks the named client `name`.
- `AddHttpClientMock(mock)` mocks the default client: the `HttpClient` that `services.AddHttpClient()` registers, and
  `IHttpClientFactory.CreateClient()` without a name.
- Several clients can use the same mock, or each its own.

## Typed clients

`AddHttpClientMock<T>(mock)` mocks the typed client `T`, registered with `services.AddHttpClient<T>()`:

<!-- ref:../../tests/Meziantou.Framework.HttpClientMock.Tests/MockTests.cs;region=HttpClientMock_TypedClient;format=md-fenced-code-block;dedent=true -->
```csharp
await using var mock = new HttpClientMock();
mock.MapGet("https://weather.example.com/forecast", () => "sunny");

var services = new ServiceCollection();
services.AddHttpClient<WeatherClient>(httpClient => httpClient.BaseAddress = new Uri("https://weather.example.com"));
services.AddHttpClientMock(builder => builder.AddHttpClientMock<WeatherClient>(mock));

await using var serviceProvider = services.BuildServiceProvider();
var weatherClient = serviceProvider.GetRequiredService<WeatherClient>();
Assert.Equal("sunny", await weatherClient.GetForecastAsync());
```
<!-- endref -->

with this typed client:

<!-- ref:../../tests/Meziantou.Framework.HttpClientMock.Tests/MockTests.cs;region=HttpClientMock_TypedClientClass;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class WeatherClient(HttpClient httpClient)
{
    public Task<string> GetForecastAsync() => httpClient.GetStringAsync(new Uri("/forecast", UriKind.Relative));
}
```
<!-- endref -->

## Fail on clients without a mock

By default, a client for which no mock is registered keeps its real handler and sends real HTTP requests.
`ThrowOnUnknownHttpClient` makes the factory throw an `InvalidOperationException` when such a client is created, as in
the named clients example, so a client you forgot to mock cannot reach the network. The message gives the name of the
client and the names of the clients that have a mock.

## What the mock replaces

The mock replaces the primary handler of the client, the one that sends the requests: a handler set with
`ConfigurePrimaryHttpMessageHandler` is replaced too. The `DelegatingHandler`s added to the client, for instance with
`AddHttpMessageHandler` or a resilience handler, still run in front of the mock, so a test also exercises them.

To mock only one client without `AddHttpClientMock`, set its primary handler yourself:

<!-- ref:../../tests/Meziantou.Framework.HttpClientMock.Tests/MockTests.cs;region=HttpClientMock_ConfigurePrimaryHandler;format=md-fenced-code-block;dedent=true -->
```csharp
await using var mock = new HttpClientMock();
mock.MapGet("https://weather.example.com/forecast", () => "rainy");

var services = new ServiceCollection();
services.AddHttpClient<WeatherClient>(httpClient => httpClient.BaseAddress = new Uri("https://weather.example.com"))
    .ConfigurePrimaryHttpMessageHandler(() => mock.CreateHttpMessageHandler());

await using var serviceProvider = services.BuildServiceProvider();
Assert.Equal("rainy", await serviceProvider.GetRequiredService<WeatherClient>().GetForecastAsync());
```
<!-- endref -->

## Choose mocks from the service provider

An overload of `AddHttpClientMock` takes an `Action<IServiceProvider, HttpMockServiceBuilder>`, to choose the mocks
from services registered in the container. The action runs each time the factory creates a handler.

## Test an ASP.NET Core application

In an integration test of an ASP.NET Core application, call `AddHttpClientMock` where the test replaces the services of
the application, for instance in `ConfigureTestServices` of `WebApplicationFactory<TEntryPoint>`. The application then
calls the mocks instead of the real services.
