using Meziantou.DnsProxy;
using Meziantou.DnsProxy.Diagnostics;
using Meziantou.DnsProxy.Filtering;
using Meziantou.DnsProxy.Forwarding;
using Meziantou.DnsProxy.History;
using Meziantou.DnsProxy.Proxy;
using Meziantou.Framework.DnsServer.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Security.Cryptography.X509Certificates;

var builder = WebApplication.CreateBuilder(args);

var bootstrapOptions = new DnsProxyOptions();
builder.Configuration.GetSection(DnsProxyOptions.SectionName).Bind(bootstrapOptions);
var dnsOverHttpsPath = string.IsNullOrWhiteSpace(bootstrapOptions.DnsOverHttpsPath) ? "/dns-query" : bootstrapOptions.DnsOverHttpsPath;

builder.Services.AddHttpClient();
builder.Services.Configure<DnsProxyOptions>(builder.Configuration.GetSection(DnsProxyOptions.SectionName));
builder.Services.AddSingleton<RequestHistoryStore>();
builder.Services.AddSingleton<FilterEngineProvider>();
builder.Services.AddHostedService<FilterEngineRefreshService>();
builder.Services.AddSingleton<UpstreamDnsClientFactory>();
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

builder.AddDnsServer(options =>
{
    options.AddUdpListener(bootstrapOptions.DnsPort);
    options.AddTcpListener(bootstrapOptions.DnsPort);

    if (bootstrapOptions.DnsOverTlsPort > 0)
    {
        options.AddTlsListener(bootstrapOptions.DnsOverTlsPort, certificate!);
    }

    if (bootstrapOptions.DnsOverQuicPort > 0)
    {
        options.AddQuicListener(bootstrapOptions.DnsOverQuicPort, certificate!);
    }
});

try
{
    var app = builder.Build();

    var dnsProxyHandler = app.Services.GetRequiredService<DnsProxyHandler>();
    app.MapDnsHandler(dnsProxyHandler.HandleAsync);
    app.MapDnsOverHttps(dnsOverHttpsPath);

    app.MapGet("/", (RequestHistoryStore historyStore, IOptions<DnsProxyOptions> optionsAccessor, FilterEngineProvider filters, UpstreamDnsClientFactory upstreams) =>
    {
        var html = DiagnosticsPageRenderer.Render(optionsAccessor.Value, filters, upstreams.GetUpstreams(), historyStore.GetSnapshot());
        return Results.Content(html, "text/html; charset=utf-8");
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

public partial class Program;
