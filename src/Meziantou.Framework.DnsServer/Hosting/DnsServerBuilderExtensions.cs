using System.Net;
using Meziantou.Framework.DnsServer.Handler;
using Meziantou.Framework.DnsServer.Listeners;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
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

        // Configure Kestrel for TCP and TLS listeners
        if (options.TcpListeners.Count > 0 || options.TlsListeners.Count > 0)
        {
            builder.Services.AddSingleton<DnsTcpConnectionHandler>(sp =>
            {
                var handlerHolder = sp.GetRequiredService<DnsRequestDelegateHolder>();
                var logger = sp.GetRequiredService<ILogger<DnsTcpConnectionHandler>>();
                return new DnsTcpConnectionHandler(handlerHolder, DnsServerProtocol.Tcp, logger);
            });

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
                        listenOptions.UseHttps(tlsListener.Certificate);
                        listenOptions.UseConnectionHandler<DnsTcpConnectionHandler>();
                    });
                }
            });
        }

        // Register UDP listener as hosted service
        if (options.UdpListeners.Count > 0)
        {
            builder.Services.AddHostedService<DnsUdpListener>(sp =>
            {
                var serverOptions = sp.GetRequiredService<DnsServerOptions>();
                var handlerHolder = sp.GetRequiredService<DnsRequestDelegateHolder>();
                var logger = sp.GetRequiredService<ILogger<DnsUdpListener>>();
                return new DnsUdpListener(serverOptions, handlerHolder, logger);
            });
        }

#if NET9_0_OR_GREATER
        // Register QUIC listener as hosted service
        if (options.QuicListeners.Count > 0)
        {
            builder.Services.AddHostedService<DnsQuicListener>(sp =>
            {
                var serverOptions = sp.GetRequiredService<DnsServerOptions>();
                var handlerHolder = sp.GetRequiredService<DnsRequestDelegateHolder>();
                var logger = sp.GetRequiredService<ILogger<DnsQuicListener>>();
                return new DnsQuicListener(serverOptions, handlerHolder, logger);
            });
        }
#endif

        return builder;
    }
}
