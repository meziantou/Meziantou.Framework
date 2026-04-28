using Meziantou.Framework.PostgreSql.Handler;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meziantou.Framework.PostgreSql.Hosting;

/// <summary>Extension methods for configuring PostgreSQL server hosting.</summary>
public static class PostgreSqlServerBuilderExtensions
{
    /// <summary>Adds PostgreSQL server services and listeners to an application builder.</summary>
    public static IHostApplicationBuilder AddPostgreSqlServer(this IHostApplicationBuilder builder, Action<PostgreSqlServerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new PostgreSqlServerOptions();
        configure(options);

        if (options.TcpListeners.Count == 0)
        {
            options.AddTcpListener();
        }

        _ = options.GetTlsCertificate();

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<PostgreSqlAuthenticationDelegateHolder>();
        builder.Services.AddSingleton<PostgreSqlQueryDelegateHolder>();
        builder.Services.AddSingleton<PostgreSqlTcpConnectionHandler>();

        builder.Services.Configure<KestrelServerOptions>(kestrelOptions =>
        {
            foreach (var listener in options.TcpListeners)
            {
                kestrelOptions.Listen(listener.BindAddress, listener.Port, listenOptions =>
                {
                    listenOptions.UseConnectionHandler<PostgreSqlTcpConnectionHandler>();
                });
            }
        });

        return builder;
    }
}
