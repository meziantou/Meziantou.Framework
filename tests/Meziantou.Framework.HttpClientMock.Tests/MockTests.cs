using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Meziantou.Extensions.Logging.InMemory;
using Meziantou.Extensions.Logging.Xunit.v3;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.Tests;
public sealed class MockTests(ITestOutputHelper testOutputHelper)
{
    [Fact]
    public async Task ReadmeExample_Configure()
    {
        #region HttpClientMock_Configure
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
        #endregion

        #region HttpClientMock_RequestCounter
        mock.MapGet("/counter", (RequestCounter counter) => counter.Get());
        #endregion

        #region HttpClientMock_CreateHttpClient
        using var httpClient = mock.CreateHttpClient();
        #endregion

        Assert.Equal("""{"id": 1}""", await httpClient.GetStringAsync("https://example.com/json", XunitCancellationToken));
        Assert.Equal("\"test\"", await httpClient.GetStringAsync("https://example.com/todos/1", XunitCancellationToken));
        Assert.Equal("\"test\"", await httpClient.GetStringAsync("https://example.com/path?search=text", XunitCancellationToken));
        Assert.Equal("1", await httpClient.GetStringAsync("https://example.com/counter", XunitCancellationToken));
        Assert.Equal("2", await httpClient.GetStringAsync("https://example.com/counter", XunitCancellationToken));

        #region HttpClientMock_CreateHttpMessageHandler
        using var httpMessageHandler = mock.CreateHttpMessageHandler();
        #endregion

        using var httpClientFromHandler = new HttpClient(httpMessageHandler);
        Assert.Equal("<root></root>", await httpClientFromHandler.GetStringAsync("https://example.com/xml", XunitCancellationToken));
    }

    [Fact]
    public async Task ReadmeExample_DependencyInjection()
    {
        await using var mock = new HttpClientMock();
        mock.MapGet("/", () => "mocked");

        #region HttpClientMock_DependencyInjection
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
        #endregion

        await using var serviceProvider = services.BuildServiceProvider();
        Assert.Equal("mocked", await serviceProvider.GetRequiredService<HttpClient>().GetStringAsync("https://example.com/", XunitCancellationToken));
        Assert.Equal("mocked", await serviceProvider.GetRequiredService<SampleClient>().GetStringAsync("https://example.com/"));
    }

    [Fact]
    public async Task ReadmeExample_ThrowOnUnknownHttpClient()
    {
        await using var mock = new HttpClientMock();
        mock.MapGet("/", () => "mocked");
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddHttpClient<SampleClient>();

        #region HttpClientMock_ThrowOnUnknownHttpClient
        services.AddHttpClientMock(builder => builder
            .AddHttpClientMock(mock)
            .ThrowOnUnknownHttpClient());
        #endregion

        await using var serviceProvider = services.BuildServiceProvider();
        Assert.Equal("mocked", await serviceProvider.GetRequiredService<HttpClient>().GetStringAsync("https://example.com/", XunitCancellationToken));
        Assert.NotNull(Record.Exception(() => serviceProvider.GetRequiredService<SampleClient>()));
    }

    [Fact]
    public async Task ReadmeExample_ResiliencePolicies()
    {
        #region HttpClientMock_ResiliencePolicies
        await using var mock = new HttpClientMock(configureLogging: null, configureServices: services =>
        {
            services.ConfigureHttpClientDefaults(services => services.AddStandardResilienceHandler());
        });
        #endregion

        mock.MapGet("/", () => "mocked");
        using var httpClient = mock.CreateHttpClient();
        Assert.Equal("mocked", await httpClient.GetStringAsync("https://example.com/", XunitCancellationToken));
    }

    [Fact]
    public async Task ReadmeExample_XUnitLoggerProvider()
    {
        #region HttpClientMock_XUnitLoggerProvider
        using var loggerProvider = new XUnitLoggerProvider(testOutputHelper);
        await using var mock = new HttpClientMock(loggerProvider);
        #endregion

        mock.MapGet("/", () => "mocked");
        using var httpClient = mock.CreateHttpClient();
        Assert.Equal("mocked", await httpClient.GetStringAsync("https://example.com/", XunitCancellationToken));
    }

    [Fact]
    public async Task ReadmeExample_ConfigureLogging()
    {
        #region HttpClientMock_ConfigureLogging
        using var loggerProvider = new XUnitLoggerProvider(testOutputHelper);
        await using var mock = new HttpClientMock(logs =>
        {
            logs.AddProvider(loggerProvider);
            logs.SetMinimumLevel(LogLevel.Trace);
        });
        #endregion

        mock.MapGet("/", () => "mocked");
        using var httpClient = mock.CreateHttpClient();
        Assert.Equal("mocked", await httpClient.GetStringAsync("https://example.com/", XunitCancellationToken));
    }

    [Fact]
    public async Task ReadmeExample_Configuration()
    {
        #region HttpClientMock_Configuration
        await using var mock = new HttpClientMock(configureLogging: null, configureServices: services =>
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Greeting"] = "Hello" })
                .Build();
            services.AddSingleton<IConfiguration>(configuration);
        });

        mock.MapGet("/greeting", (IConfiguration configuration) => configuration["Greeting"]);
        #endregion

        using var httpClient = mock.CreateHttpClient();
        Assert.Equal("Hello", await httpClient.GetStringAsync("https://example.com/greeting", XunitCancellationToken));
    }

    [Fact]
    public async Task HttpClientMock_Example()
    {
        #region HttpClientMock_Example
        await using var mock = new HttpClientMock();
        mock.MapGet("/api/users", () => Results.Ok(new { name = "John" }));
        using var httpClient = mock.CreateHttpClient();
        using var response = await httpClient.GetAsync(new Uri("/api/users", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"name":"John"}""", await response.Content.ReadAsStringAsync());
        #endregion
    }

    [Fact]
    public async Task Results_ForwardToUpstream_HttpClient()
    {
        await using var mock1 = new HttpClientMock(XUnitLogger.CreateLogger(testOutputHelper));
        mock1.MapGet("https://example.com/", () => "test");
        using var mock1Client = mock1.CreateHttpClient();

        await using var mock2 = new HttpClientMock(XUnitLogger.CreateLogger(testOutputHelper), services =>
        {
            services.ConfigureHttpClientDefaults(services => services.AddStandardResilienceHandler());
        });

        mock2.MapGet("https://example.com/", () => Results.Extensions.ForwardToUpstream(mock1Client));

        using var client = mock2.CreateHttpClient();
        var value = await client.GetStringAsync("https://example.com/", XunitCancellationToken);
        Assert.Equal("test", value);
    }

    [Fact]
    public async Task Results_ForwardToUpstream_HttpClientFactory()
    {
        await using var mock1 = new HttpClientMock(XUnitLogger.CreateLogger(testOutputHelper));
        mock1.MapGet("https://example.com/", () => "test");

        await using var mock2 = new HttpClientMock(XUnitLogger.CreateLogger(testOutputHelper), services =>
        {
            // ForwardToUpstream() without an explicit client resolves IHttpClientFactory. Replacing the primary
            // handler of every client it creates keeps the request inside mock1 instead of reaching the network.
            services.ConfigureHttpClientDefaults(builder => builder.ConfigurePrimaryHttpMessageHandler(mock1.CreateHttpMessageHandler));
        });

        mock2.MapGet("https://example.com/", () => Results.Extensions.ForwardToUpstream());

        using var client = mock2.CreateHttpClient();
        var value = await client.GetStringAsync("https://example.com/", XunitCancellationToken);
        Assert.Equal("test", value);
    }

    [Fact]
    public async Task ForwardUnknownRequestsToUpstream()
    {
        await using var mock1 = new HttpClientMock(XUnitLogger.CreateLogger(testOutputHelper));
        mock1.MapGet("https://example.com/", () => "test");
        using var mock1Client = mock1.CreateHttpClient();

        await using var mock2 = new HttpClientMock(XUnitLogger.CreateLogger(testOutputHelper), services =>
        {
            services.ConfigureHttpClientDefaults(services => services.AddStandardResilienceHandler());
        });
        mock2.MapGet("https://example.com/dummy", () => "dummy");
        mock2.MapGet("https://example.com/not_found", () => Results.NotFound("not_found"));
        mock2.ForwardUnknownRequestsToUpstream(mock1Client);

        await ExpectString(mock2, "https://example.com/dummy", "dummy");

        using var client = mock2.CreateHttpClient();
        {
            using var value = await client.GetAsync("https://example.com/not_found", XunitCancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, value.StatusCode);
            var content = await value.Content.ReadFromJsonAsync<string>(XunitCancellationToken);
            Assert.Equal("not_found", content);
        }

        {
            // There are many issues with connection initialization on GitHub Actions
            var value = await client.GetStringAsync("https://example.com/", XunitCancellationToken);
            Assert.Equal("test", value);
        }
    }

    [Fact]
    public async Task RequestCounter()
    {
        await using var mock = new HttpClientMock(XUnitLogger.CreateLogger(testOutputHelper));
        mock.MapGet("/current", (RequestCounter counter) => counter.Get());
        mock.MapGet("/total", (RequestCounter counter) => counter.TotalCount);

        await ExpectString(mock, "/current", "1");
        await ExpectString(mock, "/current", "2");
        await ExpectString(mock, "/total", "3");
        await ExpectString(mock, "/current", "3");
    }

    [Fact]
    public async Task Extensions_RawJson()
    {
        await using var mock = new HttpClientMock();
        mock.MapGet("/", () => Results.Extensions.RawJson("""{"id":1}"""));

        using var client = mock.CreateHttpClient();
        var data = await client.GetFromJsonAsync<Dictionary<string, object>>("/", XunitCancellationToken);
        Assert.NotNull(data);
        Assert.Contains("id", data);
    }

    [Fact]
    public async Task Extensions_RawJson_StatusCode()
    {
        await using var mock = new HttpClientMock();
        mock.MapGet("/", () => Results.Extensions.RawJson("""{"id":1}""", statusCode: 400));

        using var client = mock.CreateHttpClient();
        using var response = await client.GetAsync("/", XunitCancellationToken);
        Assert.Equal(400, (int)response.StatusCode);
        var data = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(XunitCancellationToken);
        Assert.NotNull(data);
        Assert.Contains("id", data);
    }

    [Fact]
    public async Task MapGet_RelativeUrl_WithQueryString()
    {
        await using var mock = new HttpClientMock(XUnitLogger.CreateLogger(testOutputHelper));
        mock.MapGet("/", (string a = "a") => a);
        mock.MapGet("/?a=b", () => "b");
        mock.MapGet("/?a=c", () => "c");

        await ExpectString(mock, "/", "a");
        await ExpectString(mock, "https://dummy.com/", "a");
        await ExpectString(mock, "HTTPS://dummy.com/", "a");
        await ExpectString(mock, "https://dummy.com/?a=b", "b");
        await ExpectString(mock, "https://dummy.com/?a=c", "c");
        await ExpectString(mock, "https://dummy.com/?a=d", "d");
        await ExpectNotFound(mock, "https://dummy.com/path");
    }

    [Fact]
    public async Task MapGet_AbsoluteUrl_WithQueryString_Unordered()
    {
        await using var mock = new HttpClientMock(XUnitLogger.CreateLogger(testOutputHelper));
        mock.MapGet("http://dummy.com/", () => "a");
        mock.MapGet("HTTP://dummy.com/?a=b&c=d", () => "b");

        await ExpectString(mock, "http://dummy.com/", "a");
        await ExpectString(mock, "http://dummy.com/?a=b", "a");
        await ExpectString(mock, "http://dummy.com/?c=d", "a");
        await ExpectString(mock, "http://dummy.com/?a=b&c=d&e=f", "a");
        await ExpectString(mock, "http://dummy.com/?a=b&c=d", "b");
        await ExpectString(mock, "http://dummy.com/?c=d&a=b", "b");
    }

    [Fact]
    public async Task MapGet_AbsoluteUrl_WithQueryString()
    {
        await using var mock = new HttpClientMock(XUnitLogger.CreateLogger(testOutputHelper));
        mock.MapGet("http://dummy.com/", () => "a");
        mock.MapGet("http://dummy.com/?a=b", () => "b");
        mock.MapGet("http://dummy.com/?a=c", () => "c");

        await ExpectString(mock, "http://dummy.com/", "a");
        await ExpectString(mock, "http://dummy.com/?a=b", "b");
        await ExpectString(mock, "http://dummy.com/?a=c", "c");
        await ExpectNotFound(mock, "http://dummy.com/path");
    }

    [Fact]
    public async Task MapGet_AbsoluteUrl_WithScheme()
    {
        await using var mock = new HttpClientMock(XUnitLogger.CreateLogger(testOutputHelper));
        mock.MapGet("http://dummy.com/", () => "a");
        mock.MapGet("https://dummy.com/", () => "b");

        await ExpectString(mock, "http://dummy.com/", "a");
        await ExpectString(mock, "https://dummy.com/", "b");
    }

    [Fact]
    public async Task MapGet_AbsoluteUrl_WithPort()
    {
        await using var mock = new HttpClientMock(XUnitLogger.CreateLogger(testOutputHelper));
        mock.MapGet("http://dummy.com:2222/", () => "a");
        mock.MapGet("http://dummy.com:3333/", () => "b");

        await ExpectString(mock, "http://dummy.com:2222/", "a");
        await ExpectString(mock, "http://dummy.com:3333/", "b");
    }

    [Fact]
    [SuppressMessage("Usage", "ASP0022:Route conflict detected between route handlers", Justification = "false-positive")]
    public async Task MultipleMocks()
    {
        using var logger1 = new InMemoryLoggerProvider();
        using var logger2 = new InMemoryLoggerProvider();
        await using var mock1 = new HttpClientMock(logger1);
        mock1.Application.MapGet("/", () => Results.Ok("test1"));

        await using var mock2 = new HttpClientMock(builder => builder.AddProvider(logger2));
        mock2.Application.MapGet("/", () => Results.Ok("test2"));

        var services = new ServiceCollection().AddHttpClient();
        services.AddHttpClient<SampleClient>();

        services.AddHttpClientMock(builder => builder
            .AddHttpClientMock(mock1)
            .AddHttpClientMock<SampleClient>(mock2));

        await using var serviceProvider = services.BuildServiceProvider();
        var httpClient = serviceProvider.GetRequiredService<HttpClient>();
        Assert.Equal("\"test1\"", await httpClient.GetStringAsync("https://example.com/", XunitCancellationToken));

        var sampleClient = serviceProvider.GetRequiredService<SampleClient>();
        Assert.Equal("\"test2\"", await sampleClient.GetStringAsync("https://example.com/"));

        Assert.NotEmpty(logger1.Logs);
        Assert.NotEmpty(logger2.Logs);
    }

    [Fact]
    public async Task Test_Logger()
    {
        using var loggerProvider = new InMemoryLoggerProvider();
        var logger = loggerProvider.CreateLogger("dummy");
        await using var mock = new HttpClientMock(logger);
        mock.Application.MapGet("/", () => Results.Ok("test1"));

        var services = new ServiceCollection().AddHttpClient();
        services.AddHttpClient<SampleClient>();

        services.AddHttpClientMock(builder => builder
            .AddHttpClientMock(mock)
            .AddHttpClientMock<SampleClient>(mock));

        await using var serviceProvider = services.BuildServiceProvider();
        var httpClient = serviceProvider.GetRequiredService<HttpClient>();
        Assert.Equal("\"test1\"", await httpClient.GetStringAsync("https://example.com/", XunitCancellationToken));

        Assert.NotEmpty(loggerProvider.Logs);
    }


    [Fact]
    public async Task RequestCounter_CountsRequestWhoseHandlerThrows()
    {
        await using var mock = new HttpClientMock();
        mock.MapGet("/boom", void () => throw new InvalidOperationException("boom"));
        mock.MapGet("/total", (RequestCounter counter) => counter.TotalCount);

        using var client = mock.CreateHttpClient();
        await Assert.ThrowsAnyAsync<Exception>(() => client.GetAsync("/boom", XunitCancellationToken));

        await ExpectString(mock, "/total", "2");
    }

    [Fact]
    public async Task Forward_PreservesRequestContentHeaders()
    {
        await using var upstream = new HttpClientMock(XUnitLogger.CreateLogger(testOutputHelper));
        upstream.MapPost("https://example.com/echo", async (HttpContext context) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync(context.RequestAborted);
            return Results.Text(context.Request.ContentType + "|" + body);
        });
        using var upstreamClient = upstream.CreateHttpClient();

        await using var mock = new HttpClientMock(XUnitLogger.CreateLogger(testOutputHelper));
        mock.ForwardUnknownRequestsToUpstream(upstreamClient);

        using var client = mock.CreateHttpClient();
        using var response = await client.PostAsJsonAsync("https://example.com/echo", new { id = 1 }, XunitCancellationToken);
        var content = await response.Content.ReadAsStringAsync(XunitCancellationToken);
        Assert.Equal("""application/json; charset=utf-8|{"id":1}""", content);
    }

    [Fact]
    public async Task Forward_PreservesResponseContentHeaders()
    {
        await using var upstream = new HttpClientMock(XUnitLogger.CreateLogger(testOutputHelper));
        upstream.MapGet("https://example.com/json", () => Results.Extensions.RawJson("""{"id":1}"""));
        using var upstreamClient = upstream.CreateHttpClient();

        await using var mock = new HttpClientMock(XUnitLogger.CreateLogger(testOutputHelper));
        mock.ForwardUnknownRequestsToUpstream(upstreamClient);

        using var client = mock.CreateHttpClient();
        using var response = await client.GetAsync("https://example.com/json", XunitCancellationToken);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var data = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(XunitCancellationToken);
        Assert.NotNull(data);
        Assert.Contains("id", data);
    }

    [Fact]
    public async Task Forward_GetRequest_HasNoBody()
    {
        await using var upstream = new HttpClientMock(XUnitLogger.CreateLogger(testOutputHelper));
        upstream.MapGet("https://example.com/probe", (HttpContext context) =>
            (context.Request.ContentLength?.ToString(CultureInfo.InvariantCulture) ?? "<null>") + "|" + (context.Request.ContentType ?? "<null>"));
        using var upstreamClient = upstream.CreateHttpClient();

        await using var mock = new HttpClientMock(XUnitLogger.CreateLogger(testOutputHelper));
        mock.ForwardUnknownRequestsToUpstream(upstreamClient);

        await ExpectString(mock, "https://example.com/probe", "<null>|<null>");
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task Map_AllVerbs(string verb)
    {
        await using var mock = new HttpClientMock(XUnitLogger.CreateLogger(testOutputHelper));
        mock.MapGet("/", () => "GET");
        mock.MapPost("/", () => "POST");
        mock.MapPut("/", () => "PUT");
        mock.MapPatch("/", () => "PATCH");
        mock.MapDelete("/", () => "DELETE");
        mock.MapHead("/", () => "HEAD");
        mock.MapOptions("/", () => "OPTIONS");

        using var client = mock.CreateHttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Parse(verb), "/");
        using var response = await client.SendAsync(request, XunitCancellationToken);
        response.EnsureSuccessStatusCode();

        // TestServer does not strip the body of a HEAD response the way a real server does
        var content = await response.Content.ReadAsStringAsync(XunitCancellationToken);
        Assert.Equal(verb, content);
    }

    [Fact]
    public async Task Map_RequestDelegate()
    {
        await using var mock = new HttpClientMock(XUnitLogger.CreateLogger(testOutputHelper));
        mock.MapPost("/", context => context.Response.WriteAsync("delegate", context.RequestAborted));

        using var client = mock.CreateHttpClient();
        using var response = await client.PostAsync("/", content: null, XunitCancellationToken);
        Assert.Equal("delegate", await response.Content.ReadAsStringAsync(XunitCancellationToken));
    }

    [Fact]
    public async Task MapGet_RouteParameter()
    {
        await using var mock = new HttpClientMock(XUnitLogger.CreateLogger(testOutputHelper));
        mock.MapGet("/todos/{id}", (int id) => id.ToString(CultureInfo.InvariantCulture));

        await ExpectString(mock, "/todos/42", "42");
    }

    [Fact]
    public async Task MapGet_QueryString_RepeatedValues()
    {
        await using var mock = new HttpClientMock(XUnitLogger.CreateLogger(testOutputHelper));
        mock.MapGet("/", () => "fallback");
        mock.MapGet("/?a=1&a=1", () => "match");

        await ExpectString(mock, "/?a=1&a=1", "match");
        await ExpectString(mock, "/?a=1&a=2", "fallback");
    }

    [Fact]
    public async Task Extensions_RawXml()
    {
        await using var mock = new HttpClientMock();
        mock.MapGet("/", () => Results.Extensions.RawXml("<root></root>"));

        using var client = mock.CreateHttpClient();
        using var response = await client.GetAsync("/", XunitCancellationToken);
        Assert.Equal("text/xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("<root></root>", await response.Content.ReadAsStringAsync(XunitCancellationToken));
    }

    [Fact]
    public async Task NamedHttpClientMock()
    {
        await using var mock = new HttpClientMock(XUnitLogger.CreateLogger(testOutputHelper));
        mock.Application.MapGet("/", () => "named");

        var services = new ServiceCollection();
        services.AddHttpClient("api");
        services.AddHttpClientMock(builder => builder.AddHttpClientMock("api", mock));

        await using var serviceProvider = services.BuildServiceProvider();
        using var client = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("api");
        Assert.Equal("named", await client.GetStringAsync("https://example.com/", XunitCancellationToken));
    }

    [Fact]
    public async Task GenericTypedHttpClientMock()
    {
        await using var mock = new HttpClientMock(XUnitLogger.CreateLogger(testOutputHelper));
        mock.Application.MapGet("/", () => "generic");

        var services = new ServiceCollection();
        services.AddHttpClient<GenericClient<int>>();
        services.AddHttpClientMock(builder => builder.AddHttpClientMock<GenericClient<int>>(mock));

        await using var serviceProvider = services.BuildServiceProvider();
        var client = serviceProvider.GetRequiredService<GenericClient<int>>();
        Assert.Equal("generic", await client.GetStringAsync("https://example.com/"));
    }

    [Fact]
    public async Task ThrowOnUnknownHttpClient()
    {
        await using var mock = new HttpClientMock(XUnitLogger.CreateLogger(testOutputHelper));
        mock.Application.MapGet("/", () => "mocked");

        var services = new ServiceCollection().AddHttpClient();
        services.AddHttpClient<SampleClient>();
        services.AddHttpClientMock(builder => builder
            .AddHttpClientMock(mock)
            .ThrowOnUnknownHttpClient());

        await using var serviceProvider = services.BuildServiceProvider();
        Assert.Equal("mocked", await serviceProvider.GetRequiredService<HttpClient>().GetStringAsync("https://example.com/", XunitCancellationToken));

        var exception = Record.Exception(() => serviceProvider.GetRequiredService<SampleClient>());
        Assert.NotNull(exception);
        Assert.Contains(nameof(SampleClient), exception.ToString());
    }

    [Fact]
    public async Task StartupFailure_ReportsActualError()
    {
        await using var mock = new HttpClientMock(configureLogging: null, configureServices: services => services.AddHostedService<ThrowingHostedService>());
        mock.MapGet("/", () => "ok");

        var exception = Record.Exception(() => mock.CreateHttpClient().Dispose());
        Assert.NotNull(exception);
        Assert.Equal("startup boom", exception.Message);
    }

    [Fact]
    public async Task HostConfiguration_IsIsolatedFromTheAmbientEnvironment()
    {
        await using var mock = new HttpClientMock();

        Assert.Equal(Environments.Production, mock.Application.Environment.EnvironmentName);
        Assert.Equal(
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),
            mock.Application.Environment.ContentRootPath.TrimEnd(Path.DirectorySeparatorChar));

        var configuration = (IConfigurationRoot)mock.Application.Configuration;
        Assert.Empty(configuration.Providers);
    }

    [Fact]
    public async Task Documentation_RouteParameters()
    {
        #region HttpClientMock_RouteParameters
        await using var mock = new HttpClientMock();
        mock.MapGet("/users/{id}", (string id) => "user " + id);        // a route parameter
        mock.MapGet("/search", (string q) => "results for " + q);       // bound from the query string

        using var httpClient = mock.CreateHttpClient();
        Assert.Equal("user 42", await httpClient.GetStringAsync(new Uri("/users/42", UriKind.Relative)));
        Assert.Equal("results for dotnet", await httpClient.GetStringAsync(new Uri("/search?q=dotnet", UriKind.Relative)));
        #endregion
    }

    [Fact]
    public async Task Documentation_MatchQueryString()
    {
        #region HttpClientMock_MatchQueryString
        await using var mock = new HttpClientMock();
        mock.MapGet("/todos", () => "all");
        mock.MapGet("/todos?status=done", () => "done");
        mock.MapGet("/todos?status=open&sort=date", () => "open, by date");

        using var httpClient = mock.CreateHttpClient();
        Assert.Equal("done", await httpClient.GetStringAsync(new Uri("/todos?status=done", UriKind.Relative)));

        // The order of the parameters does not matter
        Assert.Equal("open, by date", await httpClient.GetStringAsync(new Uri("/todos?sort=date&status=open", UriKind.Relative)));

        // A query string that no route matches exactly falls back to the route without a query string
        Assert.Equal("all", await httpClient.GetStringAsync(new Uri("/todos?status=open", UriKind.Relative)));
        Assert.Equal("all", await httpClient.GetStringAsync(new Uri("/todos", UriKind.Relative)));
        #endregion
    }

    [Fact]
    public async Task Documentation_MatchAbsoluteUrl()
    {
        #region HttpClientMock_MatchAbsoluteUrl
        await using var mock = new HttpClientMock();
        mock.MapGet("https://api.example.com/status", () => "api");
        mock.MapGet("https://www.example.com/status", () => "www");
        mock.MapGet("http://www.example.com/status", () => "www over http");
        mock.MapGet("/status", () => "any other host");

        using var httpClient = mock.CreateHttpClient();
        Assert.Equal("api", await httpClient.GetStringAsync(new Uri("https://api.example.com/status")));
        Assert.Equal("www", await httpClient.GetStringAsync(new Uri("https://www.example.com/status")));
        Assert.Equal("www over http", await httpClient.GetStringAsync(new Uri("http://www.example.com/status")));
        Assert.Equal("any other host", await httpClient.GetStringAsync(new Uri("https://cdn.example.com/status")));
        #endregion
    }

    [Fact]
    public async Task Documentation_RawContent()
    {
        #region HttpClientMock_RawContent
        await using var mock = new HttpClientMock();
        mock.MapGet("/users/1", () => Results.Extensions.RawJson("""{"id":1,"name":"John"}"""));
        mock.MapGet("/users/2", () => Results.Extensions.RawJson("""{"error":"not found"}""", statusCode: 404));
        mock.MapGet("/feed", () => Results.Extensions.RawXml("<feed />"));

        using var httpClient = mock.CreateHttpClient();
        using var response = await httpClient.GetAsync(new Uri("/users/2", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("""{"error":"not found"}""", await response.Content.ReadAsStringAsync());
        #endregion
    }

    [Fact]
    public async Task Documentation_RequestDelegate()
    {
        #region HttpClientMock_RequestDelegate
        await using var mock = new HttpClientMock();
        mock.MapGet("/report", async context =>
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/csv";
            context.Response.Headers.ETag = "\"v1\"";
            await context.Response.WriteAsync("id,name\n1,John\n", context.RequestAborted);
        });

        using var httpClient = mock.CreateHttpClient();
        using var response = await httpClient.GetAsync(new Uri("/report", UriKind.Relative));
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("\"v1\"", response.Headers.ETag?.Tag);
        Assert.Equal("id,name\n1,John\n", await response.Content.ReadAsStringAsync());
        #endregion
    }

    [Fact]
    public async Task Documentation_ReadRequestBody()
    {
        #region HttpClientMock_ReadRequestBody
        await using var mock = new HttpClientMock();

        // The body of a POST request is bound to a parameter of a complex type, as in a minimal API
        mock.MapPost("/todos", (Dictionary<string, string> todo) => Results.Created("/todos/1", new { id = 1, title = todo["title"] }));

        using var httpClient = mock.CreateHttpClient();
        using var response = await httpClient.PostAsJsonAsync(new Uri("/todos", UriKind.Relative), new { title = "Write the documentation" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/todos/1", response.Headers.Location?.OriginalString);
        Assert.Equal("""{"id":1,"title":"Write the documentation"}""", await response.Content.ReadAsStringAsync());
        #endregion
    }

    [Fact]
    public async Task Documentation_ForwardUnknownRequests()
    {
        #region HttpClientMock_ForwardUnknownRequests
        // The upstream server is another mock here, so the example does not depend on the network
        await using var upstream = new HttpClientMock();
        upstream.MapGet("https://example.com/users", () => "users from the upstream server");
        upstream.MapGet("https://example.com/orders", () => "orders from the upstream server");
        using var upstreamHttpClient = upstream.CreateHttpClient();

        await using var mock = new HttpClientMock();
        mock.MapGet("https://example.com/users", () => "mocked users");
        mock.ForwardUnknownRequestsToUpstream(upstreamHttpClient);

        using var httpClient = mock.CreateHttpClient();
        Assert.Equal("mocked users", await httpClient.GetStringAsync(new Uri("https://example.com/users")));
        Assert.Equal("orders from the upstream server", await httpClient.GetStringAsync(new Uri("https://example.com/orders")));
        #endregion
    }

    [Fact]
    public async Task Documentation_SequenceOfResponses()
    {
        #region HttpClientMock_SequenceOfResponses
        await using var mock = new HttpClientMock();

        // The first call fails and the next ones succeed, to test the retry logic of the code under test
        mock.MapGet("/flaky", (RequestCounter counter) => counter.Get() is 1 ? Results.StatusCode(503) : Results.Ok("success"));

        using var httpClient = mock.CreateHttpClient();
        using var first = await httpClient.GetAsync(new Uri("/flaky", UriKind.Relative));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);

        using var second = await httpClient.GetAsync(new Uri("/flaky", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        #endregion
    }

    [Fact]
    public async Task Documentation_TotalCount()
    {
        #region HttpClientMock_TotalCount
        await using var mock = new HttpClientMock();
        mock.MapGet("/status", () => "ok");
        mock.MapPost("/events", () => Results.Accepted());

        using var httpClient = mock.CreateHttpClient();
        _ = await httpClient.GetStringAsync(new Uri("/status", UriKind.Relative));
        using var response = await httpClient.PostAsync(new Uri("/events", UriKind.Relative), content: null);

        // RequestCounter is a singleton of the mock's service provider
        var counter = mock.Application.Services.GetRequiredService<RequestCounter>();
        Assert.Equal(2, counter.TotalCount);
        #endregion
    }

    [Fact]
    public async Task Documentation_CaptureRequests()
    {
        #region HttpClientMock_CaptureRequests
        await using var mock = new HttpClientMock();
        var authorizationHeaders = new ConcurrentQueue<string>();
        mock.MapGet("/me", (HttpRequest request) =>
        {
            authorizationHeaders.Enqueue(request.Headers.Authorization.ToString());
            return "John";
        });

        using var httpClient = mock.CreateHttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token");
        _ = await httpClient.GetStringAsync(new Uri("/me", UriKind.Relative));

        Assert.Equal(["Bearer token"], authorizationHeaders.ToArray());
        #endregion
    }

    [Fact]
    public async Task Documentation_NamedClients()
    {
        #region HttpClientMock_NamedClients
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
        #endregion
    }

    [Fact]
    public async Task Documentation_TypedClient()
    {
        #region HttpClientMock_TypedClient
        await using var mock = new HttpClientMock();
        mock.MapGet("https://weather.example.com/forecast", () => "sunny");

        var services = new ServiceCollection();
        services.AddHttpClient<WeatherClient>(httpClient => httpClient.BaseAddress = new Uri("https://weather.example.com"));
        services.AddHttpClientMock(builder => builder.AddHttpClientMock<WeatherClient>(mock));

        await using var serviceProvider = services.BuildServiceProvider();
        var weatherClient = serviceProvider.GetRequiredService<WeatherClient>();
        Assert.Equal("sunny", await weatherClient.GetForecastAsync());
        #endregion
    }

    [Fact]
    public async Task Documentation_ConfigureLoggingLevel()
    {
        #region HttpClientMock_ConfigureLoggingLevel
        using var loggerProvider = new XUnitLoggerProvider(TestContext.Current.TestOutputHelper);
        await using var mock = new HttpClientMock(logging =>
        {
            logging.AddProvider(loggerProvider);
            logging.SetMinimumLevel(LogLevel.Trace);
        });
        #endregion

        mock.MapGet("/", () => "ok");
        using var httpClient = mock.CreateHttpClient();
        Assert.Equal("ok", await httpClient.GetStringAsync(new Uri("/", UriKind.Relative)));
    }

    [Fact]
    public async Task Documentation_ConfigurePrimaryHandler()
    {
        #region HttpClientMock_ConfigurePrimaryHandler
        await using var mock = new HttpClientMock();
        mock.MapGet("https://weather.example.com/forecast", () => "rainy");

        var services = new ServiceCollection();
        services.AddHttpClient<WeatherClient>(httpClient => httpClient.BaseAddress = new Uri("https://weather.example.com"))
            .ConfigurePrimaryHttpMessageHandler(() => mock.CreateHttpMessageHandler());

        await using var serviceProvider = services.BuildServiceProvider();
        Assert.Equal("rainy", await serviceProvider.GetRequiredService<WeatherClient>().GetForecastAsync());
        #endregion
    }

    [Fact]
    public async Task Documentation_LogToTestOutput()
    {
        #region HttpClientMock_LogToTestOutput
        // The logs of the mock are written to the output of the current xUnit test
        using var loggerProvider = new XUnitLoggerProvider(TestContext.Current.TestOutputHelper);
        await using var mock = new HttpClientMock(loggerProvider);
        mock.MapGet("/", () => "ok");

        using var httpClient = mock.CreateHttpClient();
        Assert.Equal("ok", await httpClient.GetStringAsync(new Uri("/", UriKind.Relative)));
        #endregion
    }

    private static async Task ExpectString(HttpClientMock mock, string url, string expectedValue)
    {
        using var client = mock.CreateHttpClient();
        var value = await client.GetStringAsync(url, TestContext.Current.CancellationToken);
        Assert.Equal(expectedValue, value);
    }

    private static async Task ExpectNotFound(HttpClientMock mock, string url)
    {
        using var client = mock.CreateHttpClient();
        using var response = await client.GetAsync(url, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #region HttpClientMock_TypedClientClass
    public sealed class WeatherClient(HttpClient httpClient)
    {
        public Task<string> GetForecastAsync() => httpClient.GetStringAsync(new Uri("/forecast", UriKind.Relative));
    }
    #endregion

    private sealed class SampleClient(HttpClient httpClient)
    {
        public Task<string> GetStringAsync(string url) => httpClient.GetStringAsync(url, TestContext.Current.CancellationToken);
    }

    private sealed class GenericClient<T>(HttpClient httpClient)
    {
        public Task<string> GetStringAsync(string url) => httpClient.GetStringAsync(url, TestContext.Current.CancellationToken);
    }

    private sealed class ThrowingHostedService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("startup boom");
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

}
