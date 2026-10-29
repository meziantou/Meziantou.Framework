using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Testing.Platform.Services;

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
    public async Task Documentation_Usage()
    {
        string[] args = [];

        #region ServiceDefaultsAutoRegister_Usage
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build(); // Also calls UseMeziantouConventions and MapMeziantouDefaultEndpoints

        app.MapGet("/", () => "Hello World!");
        #endregion

        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync(XunitCancellationToken);
        try
        {
            using var httpClient = new HttpClient() { BaseAddress = new Uri(app.Urls.First()) };
            Assert.Equal("Healthy", await httpClient.GetStringAsync("health", XunitCancellationToken));
            Assert.Equal("Hello World!", await httpClient.GetStringAsync("/", XunitCancellationToken));
        }
        finally
        {
            await app.StopAsync(XunitCancellationToken);
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Documentation_Configure()
    {
        string[] args = [];

        #region ServiceDefaultsAutoRegister_Configure
        var builder = WebApplication.CreateBuilder(args);
        builder.UseMeziantouConventions(options => options.OpenApi.Enabled = false);

        var app = builder.Build(); // Keeps these options: the generated code only adds the conventions when they are missing
        #endregion

        await using (app)
        {
            var options = app.Services.GetRequiredService<MeziantouServiceDefaultsOptions>();
            Assert.False(options.OpenApi.Enabled);
        }
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