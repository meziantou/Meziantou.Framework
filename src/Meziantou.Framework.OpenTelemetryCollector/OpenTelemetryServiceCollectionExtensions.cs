using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Meziantou.Framework.OpenTelemetryCollector;

public static class OpenTelemetryServiceCollectionExtensions
{
    public static IServiceCollection AddOpenTelemetryReceiver<TReceiver>(this IServiceCollection services, Action<OpenTelemetryReceiverOptions>? configure = null)
        where TReceiver : OpenTelemetryHandler
    {
        ArgumentNullException.ThrowIfNull(services);

        AddOpenTelemetryInfrastructure(services, configure);
        services.AddSingleton<OpenTelemetryHandlerRegistration>(static serviceProvider => new OpenTelemetryHandlerRegistration(serviceProvider.GetRequiredService<TReceiver>()));
        return services;
    }

    public static IServiceCollection AddOpenTelemetryReceiver(this IServiceCollection services, Func<IServiceProvider, OpenTelemetryHandler> implementationFactory, Action<OpenTelemetryReceiverOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(implementationFactory);

        AddOpenTelemetryInfrastructure(services, configure);
        services.AddSingleton<OpenTelemetryHandlerRegistration>(serviceProvider => new OpenTelemetryHandlerRegistration(implementationFactory(serviceProvider)));
        return services;
    }

    private static void AddOpenTelemetryInfrastructure(IServiceCollection services, Action<OpenTelemetryReceiverOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddGrpc();
        _ = services.AddOptions<OpenTelemetryReceiverOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<OpenTelemetryTraceTailSamplerHandler>();
        services.TryAddSingleton<OpenTelemetryRequestPipeline>();
    }
}
