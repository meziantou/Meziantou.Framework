using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Meziantou.Framework.OpenTelemetryCollector;

public static class OpenTelemetryServiceCollectionExtensions
{
    public static IServiceCollection AddOpenTelemetryReceiver<TReceiver>(this IServiceCollection services, Action<OpenTelemetryReceiverOptions>? configure = null)
        where TReceiver : OpenTelemetryHandler
    {
        ArgumentNullException.ThrowIfNull(services);

        AddOpenTelemetryInfrastructure(services, configure);
        services.TryAddSingleton<TReceiver>();

        // Keyed on TReceiver, so calling this method twice for the same handler type does not dispatch every record twice.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<OpenTelemetryHandlerRegistration, OpenTelemetryHandlerRegistration<TReceiver>>());
        return services;
    }

    /// <remarks>
    /// Unlike the overload taking a receiver type, this method always adds a registration. When the factory returns an
    /// instance that is already registered, the duplicate is ignored when the handlers are resolved.
    /// </remarks>
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

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<OpenTelemetryTraceTailSamplerHandler>();
        services.TryAddSingleton<OpenTelemetryRequestPipeline>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OpenTelemetryTailSamplerBackgroundService>());
    }
}
