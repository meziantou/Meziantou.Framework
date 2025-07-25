using System.Net;
using System.Net.Http.Json;
using Meziantou.Extensions.Logging.InMemory;
using Meziantou.Extensions.Logging.Xunit.v3;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

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
        using var mock1Client = mock1.CreateHttpClient();

        await using var mock2 = new HttpClientMock(XUnitLogger.CreateLogger(testOutputHelper), services =>
        {
            services.AddSingleton<IHttpClientFactory>(new CustomHttpFactory(mock1));
        });

        mock2.MapGet("https://example.com/", () => Results.Extensions.ForwardToUpstream(mock1Client));

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

        await ExpectString(mock, "/current", "0");
        await ExpectString(mock, "/current", "1");
        await ExpectString(mock, "/total", "2");
        await ExpectString(mock, "/current", "2");
    }

    [Fact]
    public async Task Extensions_RawJson()
    {
        await using var mock = new HttpClientMock();
        mock.MapGet("/", () => Results.Extensions.RawJson("""{"id":1}"""));

        using var client = mock.CreateHttpClient();
        var data = await client.GetFromJsonAsync<Dictionary<string, object?>>("/", XunitCancellationToken);
        Assert.True(data.ContainsKey("id"));
    }

    [Fact]
    public async Task Extensions_RawJson_StatusCode()
    {
        await using var mock = new HttpClientMock();
        mock.MapGet("/", () => Results.Extensions.RawJson("""{"id":1}""", statusCode: 400));

        using var client = mock.CreateHttpClient();
        using var response = await client.GetAsync("/", XunitCancellationToken);
        Assert.Equal(400, (int)response.StatusCode);
        var data = await response.Content.ReadFromJsonAsync<Dictionary<string, object?>>(XunitCancellationToken);
        Assert.True(data.ContainsKey("id"));
    }

    [Fact]
    public async Task MapGet_RelativeUrl_WithQueryString()
    {
        await using var mock = new HttpClientMock(XUnitLogger.CreateLogger(testOutputHelper));
        mock.MapGet("/", (string? a = "a") => a);
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

    private sealed class CustomHttpFactory(HttpClientMock mock) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => mock.CreateHttpClient();
    }

}
