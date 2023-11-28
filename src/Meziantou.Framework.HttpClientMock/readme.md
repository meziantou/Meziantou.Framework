# Meziantou.Framework.HttpClientMock

````c#
// Define the http mock
await using var mock = new HttpClientMock();
mock.Application.MapGet("/", () => Results.Ok("test"));

// Register the mock in the service collection
var services = new ServiceCollection();
services.AddHttpClient();
services.AddHttpClient<SampleClient>(); // Add a typed client

// Mock both HttpClient
services.AddHttpClientMock(builder => builder
    .AddHttpClientMock(mock)
    .AddHttpClientMock<SampleClient>(mock));
````
