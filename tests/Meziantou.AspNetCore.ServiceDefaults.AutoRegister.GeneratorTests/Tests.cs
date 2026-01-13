using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Testing.Platform.Services;
using Xunit;

namespace Meziantou.AspNetCore.ServiceDefaults.AutoRegister.GeneratorTests;

public sealed class Tests
{
    [Fact]
    public async Task CreateBuilderTest()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel(conf => conf.Listen(IPAddress.Loopback, port: 0));
        var app = builder.Build();

        Assert.NotNull(app.Services.GetService<MeziantouServiceDefaultsOptions>());
        var t = app.RunAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var httpClient = new HttpClient() { BaseAddress = new Uri(address) };

        Assert.Equal("Healthy", await httpClient.GetStringAsync("health", XunitCancellationToken));
    }

    [Fact]
    public async Task CreateBuilderArgsTest()
    {
        var builder = WebApplication.CreateBuilder([]);
        builder.WebHost.UseKestrel(conf => conf.Listen(IPAddress.Loopback, port: 0));
        var app = builder.Build();

        Assert.NotNull(app.Services.GetService<MeziantouServiceDefaultsOptions>());
        var t = app.RunAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var httpClient = new HttpClient() { BaseAddress = new Uri(address) };

        Assert.Equal("Healthy", await httpClient.GetStringAsync("health", XunitCancellationToken));
    }
}