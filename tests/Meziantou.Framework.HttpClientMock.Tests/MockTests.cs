using Meziantou.Extensions.Logging.InMemory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Meziantou.Framework.Tests;
public sealed class MockTests
{
    [Fact]
    [SuppressMessage("Usage", "ASP0022:Route conflict detected between route handlers", Justification = "false-positive")]
    public async Task Test()
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
        Assert.Equal("\"test1\"", await httpClient.GetStringAsync("https://example.com/"));

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
        Assert.Equal("\"test1\"", await httpClient.GetStringAsync("https://example.com/"));

        Assert.NotEmpty(loggerProvider.Logs);
    }

    private sealed class SampleClient(HttpClient httpClient)
    {
        public Task<string> GetStringAsync(string url) => httpClient.GetStringAsync(url);
    }
}
