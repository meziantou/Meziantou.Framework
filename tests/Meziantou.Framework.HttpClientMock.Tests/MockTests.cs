using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meziantou.Framework.Tests;
public sealed class MockTests
{
    [Fact]
    [SuppressMessage("Usage", "ASP0022:Route conflict detected between route handlers", Justification = "false-positive")]
    public async Task Test()
    {
        await using var mock1 = new HttpClientMock();
        mock1.Application.MapGet("/", () => Results.Ok("test1"));

        await using var mock2 = new HttpClientMock();
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
    }

    private sealed class SampleClient(HttpClient httpClient)
    {
        public Task<string> GetStringAsync(string url) => httpClient.GetStringAsync(url);
    }
}
