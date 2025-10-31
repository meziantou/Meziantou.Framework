using Microsoft.Extensions.DependencyInjection;

namespace Meziantou.AspNetCore.Components;

/// <summary>Extension methods for adding query string services to the dependency injection container.</summary>
public static class QueryStringServiceExtensions
{
    /// <summary>Adds the <see cref="QueryStringService"/> to the service collection.</summary>
    /// <param name="services">The service collection to add the service to.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddQueryStringParameters(this IServiceCollection services)
    {
        services.AddScoped<QueryStringService>();
        return services;
    }
}
