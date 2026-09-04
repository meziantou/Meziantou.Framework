using System.Net.Security;
using Meziantou.Framework.DnsServer.Handler;
using Meziantou.Framework.DnsServer.Listeners;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.DnsServer.Hosting;

/// <summary>Extension methods for configuring a DNS server on an <see cref="IHostApplicationBuilder"/>.</summary>
public static class DnsServerBuilderExtensions
{
    /// <summary>Adds a DNS server to the application.</summary>
    public static IHostApplicationBuilder AddDnsServer(this IHostApplicationBuilder builder, Action<DnsServerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new DnsServerOptions();
        configure(options);

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<DnsRequestDelegateHolder>();
        builder.Services.AddSingleton<DnsRequestProcessor>();

        // Configure Kestrel for TCP and TLS listeners
        if (options.TcpListeners.Count > 0 || options.TlsListeners.Count > 0)
        {
            // These listeners are configured through Kestrel, so they would silently never start on a
            // host that has no web server.
            if (!builder.Services.Any(descriptor => descriptor.ServiceType == typeof(IServer)))
                throw new InvalidOperationException("TCP and DNS over TLS listeners require Kestrel. Build the host with WebApplication.CreateBuilder (or another Kestrel-based web host) before calling AddDnsServer, or configure only UDP and QUIC listeners.");

            builder.Services.AddSingleton<DnsTcpConnectionHandler>();

            builder.Services.Configure<KestrelServerOptions>(kestrelOptions =>
            {
                foreach (var tcpListener in options.TcpListeners)
                {
                    kestrelOptions.Listen(tcpListener.BindAddress, tcpListener.Port, listenOptions =>
                    {
                        listenOptions.UseConnectionHandler<DnsTcpConnectionHandler>();
                    });
                }

                foreach (var tlsListener in options.TlsListeners)
                {
                    kestrelOptions.Listen(tlsListener.BindAddress, tlsListener.Port, listenOptions =>
                    {
                        listenOptions.UseHttps(httpsOptions =>
                        {
                            httpsOptions.ServerCertificate = tlsListener.Certificate;

                            // This is a DNS endpoint, not an HTTP one: advertise the ALPN protocol from
                            // RFC 7858 3.1 instead of the HTTP protocols Kestrel would offer by default.
                            httpsOptions.OnAuthenticate = (_, sslOptions) => sslOptions.ApplicationProtocols = [new SslApplicationProtocol("dot")];
                        });
                        listenOptions.UseConnectionHandler<DnsTcpConnectionHandler>();
                    });
                }
            });
        }

        // Register UDP listener as hosted service
        if (options.UdpListeners.Count > 0)
        {
            builder.Services.AddHostedService<DnsUdpListener>();
        }

        // Register QUIC listener as hosted service
        if (options.QuicListeners.Count > 0)
        {
            builder.Services.AddHostedService<DnsQuicListener>();
        }

        return builder;
    }
}
