using Microsoft.Extensions.DependencyInjection;

namespace Meziantou.AspNetCore.Components;

public static class TimeZoneServiceExtensions
{
    public static IServiceCollection AddTimeZoneServices(this IServiceCollection services)
    {
        services.AddScoped<TimeZoneService>();
        return services;
    }
}

