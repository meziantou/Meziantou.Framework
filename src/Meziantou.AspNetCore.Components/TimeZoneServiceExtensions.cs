using Microsoft.Extensions.DependencyInjection;

namespace Meziantou.AspNetCore.Components;

/// <summary>Extension methods for adding timezone services to the dependency injection container.</summary>
public static class TimeZoneServiceExtensions
{
    /// <summary>Adds the <see cref="TimeZoneService"/> to the service collection.</summary>
    /// <param name="services">The service collection to add the service to.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddTimeZoneServices(this IServiceCollection services)
    {
        services.AddScoped<TimeZoneService>();
        return services;
    }
}

