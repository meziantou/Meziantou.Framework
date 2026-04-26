using Meziantou.Framework.Tds.Handler;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meziantou.Framework.Tds.Hosting;

/// <summary>Extension methods for registering TDS callbacks on endpoint builders.</summary>
public static class TdsEndpointRouteBuilderExtensions
{
    /// <summary>Registers the TDS authentication callback.</summary>
    public static IEndpointRouteBuilder MapTdsAuthenticationHandler(this IEndpointRouteBuilder endpoints, TdsAuthenticationDelegate handler)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(handler);

        endpoints.ServiceProvider.GetRequiredService<TdsAuthenticationDelegateHolder>().Handler = handler;
        return endpoints;
    }

    /// <summary>Registers the TDS query callback.</summary>
    public static IEndpointRouteBuilder MapTdsQueryHandler(this IEndpointRouteBuilder endpoints, TdsQueryDelegate handler)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(handler);

        endpoints.ServiceProvider.GetRequiredService<TdsQueryDelegateHolder>().Handler = handler;
        return endpoints;
    }

    /// <summary>Registers both authentication and query callbacks.</summary>
    public static IEndpointRouteBuilder MapTdsHandlers(this IEndpointRouteBuilder endpoints, TdsAuthenticationDelegate authenticate, TdsQueryDelegate query)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(authenticate);
        ArgumentNullException.ThrowIfNull(query);

        endpoints.ServiceProvider.GetRequiredService<TdsAuthenticationDelegateHolder>().Handler = authenticate;
        endpoints.ServiceProvider.GetRequiredService<TdsQueryDelegateHolder>().Handler = query;
        return endpoints;
    }
}
