using Meziantou.Framework.PostgreSql.Handler;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meziantou.Framework.PostgreSql.Hosting;

/// <summary>Extension methods for registering PostgreSQL callbacks on endpoint builders.</summary>
public static class PostgreSqlEndpointRouteBuilderExtensions
{
    /// <summary>Registers the PostgreSQL authentication callback.</summary>
    public static IEndpointRouteBuilder MapPostgreSqlAuthenticationHandler(this IEndpointRouteBuilder endpoints, PostgreSqlAuthenticationDelegate handler)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(handler);

        endpoints.ServiceProvider.GetRequiredService<PostgreSqlAuthenticationDelegateHolder>().Handler = handler;
        return endpoints;
    }

    /// <summary>Registers the PostgreSQL query callback.</summary>
    public static IEndpointRouteBuilder MapPostgreSqlQueryHandler(this IEndpointRouteBuilder endpoints, PostgreSqlQueryDelegate handler)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(handler);

        endpoints.ServiceProvider.GetRequiredService<PostgreSqlQueryDelegateHolder>().Handler = handler;
        return endpoints;
    }

    /// <summary>Registers both authentication and query callbacks.</summary>
    public static IEndpointRouteBuilder MapPostgreSqlHandlers(this IEndpointRouteBuilder endpoints, PostgreSqlAuthenticationDelegate authenticate, PostgreSqlQueryDelegate query)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(authenticate);
        ArgumentNullException.ThrowIfNull(query);

        endpoints.ServiceProvider.GetRequiredService<PostgreSqlAuthenticationDelegateHolder>().Handler = authenticate;
        endpoints.ServiceProvider.GetRequiredService<PostgreSqlQueryDelegateHolder>().Handler = query;
        return endpoints;
    }
}
