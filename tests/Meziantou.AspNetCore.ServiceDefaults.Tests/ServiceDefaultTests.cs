using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meziantou.AspNetCore.ServiceDefaults.Tests;

public sealed class ServiceDefaultTests
{
    [Fact]
    public async Task Test1()
    {
        var builder = WebApplication.CreateBuilder();
        builder.UseMeziantouConventions(options => options.StaticAssets.Enabled = false);
        builder.WebHost.UseKestrel(conf => conf.Listen(IPAddress.Loopback, port: 0));

        await using var app = builder.Build();
        app.MapMeziantouDefaultEndpoints();
        app.MapGet("/", () => TypedResults.Ok(new { Sample = Sample.Value1 }));
        var t = app.RunAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First();
        using var httpClient = new HttpClient() { BaseAddress = new Uri(address) };
        Assert.Equal("Healthy", await httpClient.GetStringAsync("health", XunitCancellationToken));
        Assert.Equal("Healthy", await httpClient.GetStringAsync("alive", XunitCancellationToken));

        Assert.Equal("""{"sample":"value1"}""", await httpClient.GetStringAsync("/"));
    }

    private enum Sample
    {
        Value1,
        Value2,
    }
}