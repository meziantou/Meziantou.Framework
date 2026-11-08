using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework.Tds.Hosting;

/// <summary>Extension methods for configuring TDS server hosting.</summary>
public static class TdsServerBuilderExtensions
{
    /// <summary>Adds TDS server services and listeners to an application builder.</summary>
    /// <remarks>When <see cref="TdsServerOptions.SqlServerBrowser"/> is set, a hosted service also runs the SQL Server Browser responder.</remarks>
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

        var tdsListenOptions = new List<ListenOptions>();
        builder.Services.Configure<KestrelServerOptions>(kestrelOptions =>
        {
            foreach (var listener in options.TcpListeners)
            {
                kestrelOptions.Listen(listener.BindAddress, listener.Port, listenOptions =>
                {
                    listenOptions.UseConnectionHandler<TdsTcpConnectionHandler>();
                    tdsListenOptions.Add(listenOptions);
                });
            }
        });

        if (options.SqlServerBrowser is { } browserOptions)
        {
            TdsSqlServerBrowserResponder.Validate(browserOptions);
            builder.Services.AddHostedService(services => new TdsSqlServerBrowserHostedService(
                browserOptions,
                tdsListenOptions,
                services.GetRequiredService<ILogger<TdsSqlServerBrowserHostedService>>()));
        }

        return builder;
    }
}
