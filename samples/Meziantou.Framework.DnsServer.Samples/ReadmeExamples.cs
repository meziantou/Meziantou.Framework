using System.Security.Cryptography.X509Certificates;
using Meziantou.Framework.DnsServer.Hosting;
using Meziantou.Framework.DnsServer.Protocol;
using Meziantou.Framework.DnsServer.Protocol.Records;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace DnsServerSamples;

internal static class ReadmeExamples
{
    public static void Run(string[] args)
    {
        #region DnsServer_Usage
        var builder = WebApplication.CreateBuilder(args);
        builder.AddDnsServer(options =>
        {
            options.AddUdpListener(port: 5053);
            options.AddTcpListener(port: 5053);
        });

        var app = builder.Build();
        app.MapDnsHandler((context, cancellationToken) =>
        {
            var response = context.CreateResponse();
            response.ResponseCode = DnsResponseCode.NoError;

            foreach (var question in context.Query.Questions)
            {
                if (question.Type == DnsQueryType.A)
                {
                    response.Answers.Add(new DnsResourceRecord
                    {
                        Name = question.Name,
                        Type = DnsQueryType.A,
                        Class = DnsQueryClass.IN,
                        TimeToLive = 300,
                        Data = new DnsARecordData { Address = System.Net.IPAddress.Parse("127.0.0.1") },
                    });
                }
            }

            return ValueTask.FromResult(response);
        });

        app.Run();
        #endregion
    }

    public static void DnsOverHttps(WebApplicationBuilder builder)
    {
        #region DnsServer_DnsOverHttps
        builder.AddDnsServer(options =>
        {
            options.AddTcpListener(port: 5053);
        });

        var app = builder.Build();
        app.MapDnsHandler((context, ct) => ValueTask.FromResult(context.CreateResponse()));
        app.MapDnsOverHttps("/dns-query");

        app.Run();
        #endregion
    }

    // The certificate must outlive the server, so the caller owns it
    public static X509Certificate2 DnsOverTls(WebApplicationBuilder builder)
    {
        #region DnsServer_DnsOverTls
        var certificate = X509Certificate2.CreateFromPemFile("cert.pem", "key.pem");
        builder.AddDnsServer(options =>
        {
            options.AddTlsListener(port: 8853, certificate);
        });
        #endregion

        return certificate;
    }

    // The certificate must outlive the server, so the caller owns it
    public static X509Certificate2 DnsOverQuic(WebApplicationBuilder builder)
    {
        #region DnsServer_DnsOverQuic
        var certificate = X509Certificate2.CreateFromPemFile("cert.pem", "key.pem");
        builder.AddDnsServer(options =>
        {
            options.AddQuicListener(port: 8853, certificate);
        });
        #endregion

        return certificate;
    }

    public static void Limits(WebApplicationBuilder builder)
    {
        #region DnsServer_Limits
        builder.AddDnsServer(options =>
        {
            options.AddUdpListener(port: 5053);

            // Largest UDP response, in bytes. A client's advertised EDNS payload size is clamped to this
            // value and larger answers are truncated so the client retries over TCP. The 1232-byte default
            // comes from DNS Flag Day 2020 and also bounds how much a spoofed query can amplify.
            options.MaxUdpResponseSize = 1232;

            // Closes TCP and DNS over TLS connections that go this long without a complete query
            // (RFC 7766 6.2.3). Use Timeout.InfiniteTimeSpan to disable.
            options.TcpIdleTimeout = TimeSpan.FromSeconds(30);

            // Idle timeout for DNS over QUIC connections.
            options.QuicIdleTimeout = TimeSpan.FromSeconds(30);

            // How many queries one connection may have in flight at once.
            options.MaxConcurrentQueriesPerConnection = 16;
        });
        #endregion
    }

    public static void KestrelLimits(WebApplicationBuilder builder)
    {
        #region DnsServer_KestrelLimits
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxConcurrentConnections = 1000);
        #endregion
    }
}
