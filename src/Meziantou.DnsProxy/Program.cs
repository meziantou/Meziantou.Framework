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

var builder = WebApplication.CreateBuilder(args);

var bootstrapOptions = new DnsProxyOptions();
builder.Configuration.GetSection(DnsProxyOptions.SectionName).Bind(bootstrapOptions);

builder.Services.AddHttpClient();
builder.Services.Configure<DnsProxyOptions>(builder.Configuration.GetSection(DnsProxyOptions.SectionName));
builder.Services.AddSingleton<RequestHistoryStore>();
builder.Services.AddSingleton<FilterEngineProvider>();
builder.Services.AddHostedService<FilterEngineRefreshService>();
builder.Services.AddSingleton<UpstreamDnsClientFactory>();
builder.Services.AddSingleton<DnsProxyHandler>();

builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, $"http://127.0.0.1:{bootstrapOptions.HttpPort}");

builder.AddDnsServer(options =>
{
    options.AddUdpListener(bootstrapOptions.DnsPort);
    options.AddTcpListener(bootstrapOptions.DnsPort);
});

var app = builder.Build();

var dnsProxyHandler = app.Services.GetRequiredService<DnsProxyHandler>();
app.MapDnsHandler(dnsProxyHandler.HandleAsync);
app.MapDnsOverHttps("/dns-query");

app.MapGet("/", (RequestHistoryStore historyStore, IOptions<DnsProxyOptions> optionsAccessor, FilterEngineProvider filters, UpstreamDnsClientFactory upstreams) =>
{
    var html = DiagnosticsPageRenderer.Render(optionsAccessor.Value, filters, upstreams.GetUpstreams(), historyStore.GetSnapshot());
    return Results.Content(html, "text/html; charset=utf-8");
});

await app.RunAsync().ConfigureAwait(false);

public partial class Program;
