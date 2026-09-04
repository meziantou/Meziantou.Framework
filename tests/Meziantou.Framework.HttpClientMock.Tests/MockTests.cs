using System.Globalization;
using System.Net;
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
