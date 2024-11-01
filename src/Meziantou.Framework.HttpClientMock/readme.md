# Meziantou.Framework.HttpClientMock

Define the http mock

````c#
await using var mock = new HttpClientMock();
mock.MapGet("/", () => Results.Ok("test"));
mock.MapGet("/todos/{id}", (int id) => Results.Ok("test"));
mock.MapGet("/todos/?filter=abc", () => Results.Ok("test"));
mock.MapGet("https://example.com/path?search=text", () => Results.Ok("test"));

mock.MapGet("/json", () => Results.Extensions.RawJson("""{"id": 1}"""));
mock.MapGet("/xml", () => Results.Extensions.RawXml("""<root></root>"""));

// Forward the request to the upstream server
mock.MapGet("/upstream", () => Results.Extensions.ForwardToUpstream());

mock.ForwardUnknownRequestsToUpstream();
````

Count number of requests per endpoint

````c#
mock.MapGet("/counter", (RequestCounter counter) => counter.Get());
````

Register the mock in the service collection (Dependency Injection)

````c#
// Register the mock in the service collection
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
