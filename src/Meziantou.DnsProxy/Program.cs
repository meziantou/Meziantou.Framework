using Meziantou.DnsProxy;
using Meziantou.DnsProxy.Diagnostics;
using Meziantou.DnsProxy.Filtering;
using Meziantou.DnsProxy.Forwarding;
using Meziantou.DnsProxy.History;
using Meziantou.DnsProxy.Proxy;
using Meziantou.Framework.DnsServer.Hosting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Security.Cryptography.X509Certificates;

var builder = WebApplication.CreateBuilder(args);

var bootstrapOptions = new DnsProxyOptions();
builder.Configuration.GetSection(DnsProxyOptions.SectionName).Bind(bootstrapOptions);
bootstrapOptions.ApplyDefaults();
var dnsOverHttpsPath = string.IsNullOrWhiteSpace(bootstrapOptions.DnsOverHttpsPath) ? "/dns-query" : bootstrapOptions.DnsOverHttpsPath;

builder.Services.AddHttpClient();
builder.Services.AddAntiforgery();
builder.Services.Configure<DnsProxyOptions>(builder.Configuration.GetSection(DnsProxyOptions.SectionName));
builder.Services.PostConfigure<DnsProxyOptions>(options => options.ApplyDefaults());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RequestHistoryStore>();
builder.Services.AddSingleton<FilteringPauseState>();
builder.Services.AddSingleton<FilterEngineProvider>();
builder.Services.AddHostedService<FilterEngineRefreshService>();
builder.Services.AddSingleton<CustomDnsRecordProvider>();
builder.Services.AddSingleton<UpstreamDnsClientFactory>();
builder.Services.AddSingleton<IUpstreamDnsClientProvider>(serviceProvider => serviceProvider.GetRequiredService<UpstreamDnsClientFactory>());
builder.Services.AddSingleton<DnsResponseCache>();
builder.Services.AddSingleton<ClientRateLimiter>();
builder.Services.AddSingleton<ClientAccessPolicy>();
builder.Services.AddSingleton<DnsProxyHandler>();

var certificate = bootstrapOptions.HasSecureServerListenerConfigured ? GetRequiredCertificate(bootstrapOptions) : null;
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(bootstrapOptions.HttpPort);
    if (bootstrapOptions.DnsOverHttpsPort > 0)
    {
        options.ListenLocalhost(bootstrapOptions.DnsOverHttpsPort, listenOptions =>
        {
            listenOptions.UseHttps(certificate!);
        });
    }
});

var bindAddresses = GetBindAddresses(bootstrapOptions);
builder.AddDnsServer(options =>
{
    foreach (var bindAddress in bindAddresses)
    {
        options.AddUdpListener(bootstrapOptions.DnsPort, bindAddress);
        options.AddTcpListener(bootstrapOptions.DnsPort, bindAddress);

        if (bootstrapOptions.DnsOverTlsPort > 0)
        {
            options.AddTlsListener(bootstrapOptions.DnsOverTlsPort, certificate!, bindAddress);
        }

        if (bootstrapOptions.DnsOverQuicPort > 0)
        {
            options.AddQuicListener(bootstrapOptions.DnsOverQuicPort, certificate!, bindAddress);
        }
    }
});

try
{
    var app = builder.Build();

    app.UseAntiforgery();

    var dnsProxyHandler = app.Services.GetRequiredService<DnsProxyHandler>();
    app.MapDnsHandler(dnsProxyHandler.HandleAsync);
    app.MapDnsOverHttps(dnsOverHttpsPath);

    app.MapGet("/", (HttpContext httpContext, IAntiforgery antiforgery, RequestHistoryStore historyStore, IOptions<DnsProxyOptions> optionsAccessor, FilterEngineProvider filters, FilteringPauseState filteringPauseState, IUpstreamDnsClientProvider upstreams, int? limit) =>
    {
        var tokens = antiforgery.GetAndStoreTokens(httpContext);
        var maxRenderedHistoryEntries = limit is > 0 ? limit.Value : DiagnosticsPageRenderer.DefaultRenderedHistoryEntries;
        var html = DiagnosticsPageRenderer.Render(optionsAccessor.Value, filters, filteringPauseState, upstreams.GetUpstreams(), historyStore.GetSnapshot(), tokens.FormFieldName, tokens.RequestToken, maxRenderedHistoryEntries);
        return Results.Content(html, "text/html; charset=utf-8");
    });

    app.MapPost("/filtering/disable", async (HttpContext httpContext, IAntiforgery antiforgery, FilteringPauseState filteringPauseState) =>
    {
        // Without this, any page the operator visits could silently turn filtering off with a cross-site form post.
        try
        {
            await antiforgery.ValidateRequestAsync(httpContext).ConfigureAwait(false);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest("Invalid antiforgery token.");
        }

        filteringPauseState.DisableFor(TimeSpan.FromMinutes(15));

        return Results.Redirect("/");
    });

    await app.RunAsync().ConfigureAwait(false);
}
finally
{
    certificate?.Dispose();
}

static X509Certificate2 GetRequiredCertificate(DnsProxyOptions options)
{
    if (string.IsNullOrWhiteSpace(options.CertificatePath))
        throw new InvalidOperationException("DnsProxy.CertificatePath must be configured to enable DoH/DoT/DoQ listeners.");

#pragma warning disable SYSLIB0057 // Loading certificate from file for server listeners
    return new X509Certificate2(options.CertificatePath, options.CertificatePassword);
#pragma warning restore SYSLIB0057
}

static List<IPAddress> GetBindAddresses(DnsProxyOptions options)
{
    var addresses = new List<IPAddress>();
    foreach (var value in options.BindAddresses)
    {
        if (string.IsNullOrWhiteSpace(value))
            continue;

        if (!IPAddress.TryParse(value.Trim(), out var address))
            throw new InvalidOperationException($"DnsProxy.BindAddresses contains an invalid address: '{value}'.");

        if (!addresses.Contains(address))
        {
            addresses.Add(address);
        }
    }

    if (addresses.Count == 0)
    {
        addresses.Add(IPAddress.Loopback);
    }

    return addresses;
}

public partial class Program;
