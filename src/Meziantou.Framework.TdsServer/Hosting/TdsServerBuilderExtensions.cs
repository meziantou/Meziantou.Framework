using Meziantou.Framework.Tds.Handler;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meziantou.Framework.Tds.Hosting;

/// <summary>Extension methods for configuring TDS server hosting.</summary>
public static class TdsServerBuilderExtensions
{
    /// <summary>Adds TDS server services and listeners to an application builder.</summary>
    public static IHostApplicationBuilder AddTdsServer(this IHostApplicationBuilder builder, Action<TdsServerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new TdsServerOptions();
        configure(options);

        if (options.TcpListeners.Count == 0)
        {
            options.AddTcpListener();
        }

        _ = options.GetTlsCertificate();

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<TdsAuthenticationDelegateHolder>();
        builder.Services.AddSingleton<TdsQueryDelegateHolder>();
        builder.Services.AddSingleton<TdsTcpConnectionHandler>();

        builder.Services.Configure<KestrelServerOptions>(kestrelOptions =>
        {
            foreach (var listener in options.TcpListeners)
            {
                kestrelOptions.Listen(listener.BindAddress, listener.Port, listenOptions =>
                {
                    listenOptions.UseConnectionHandler<TdsTcpConnectionHandler>();
                });
            }
        });

        return builder;
    }
}
