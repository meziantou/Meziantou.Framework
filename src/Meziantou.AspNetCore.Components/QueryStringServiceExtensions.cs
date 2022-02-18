using Microsoft.Extensions.DependencyInjection;

namespace Meziantou.AspNetCore.Components;

public static class QueryStringServiceExtensions
{
    public static IServiceCollection AddQueryStringParameters(this IServiceCollection services)
    {
        services.AddScoped<QueryStringService>();
        return services;
    }
}
