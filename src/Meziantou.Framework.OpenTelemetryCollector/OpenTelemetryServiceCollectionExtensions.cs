using Meziantou.AspNetCore.OpenTelemetryCollector;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meziantou.Framework.OpenTelemetryCollector;

public static class OpenTelemetryServiceCollectionExtensions
{
    public static IServiceCollection AddOpenTelemetryReceiver<TReceiver>(this IServiceCollection services)
        where TReceiver : OpenTelemetryHandler
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddGrpc();
        services.TryAddSingleton<OpenTelemetryOptions>();
        services.AddSingleton<OpenTelemetryHandlerRegistration>(static serviceProvider => new OpenTelemetryHandlerRegistration(serviceProvider.GetRequiredService<TReceiver>()));
        return services;
    }

    public static IServiceCollection AddOpenTelemetryReceiver(this IServiceCollection services, Func<IServiceProvider, OpenTelemetryHandler> implementationFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(implementationFactory);

        services.AddGrpc();
        services.TryAddSingleton<OpenTelemetryOptions>();
        services.AddSingleton<OpenTelemetryHandlerRegistration>(serviceProvider => new OpenTelemetryHandlerRegistration(implementationFactory(serviceProvider)));
        return services;
    }
}
