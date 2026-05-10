using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meziantou.Framework.OpenTelemetryCollector.InMemory;

public static class OpenTelemetryCollectorInMemoryServiceCollectionExtensions
{
    public static IServiceCollection AddInMemoryOpenTelemetryReceiver(this IServiceCollection services)
    {
        return AddInMemoryOpenTelemetryReceiver(services, options: null, configure: null);
    }

    public static IServiceCollection AddInMemoryOpenTelemetryReceiver(this IServiceCollection services, Action<OpenTelemetryReceiverOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        return AddInMemoryOpenTelemetryReceiver(services, options: null, configure);
    }

    public static IServiceCollection AddInMemoryOpenTelemetryReceiver(this IServiceCollection services, InMemoryOpenTelemetryHandlerOptions? options)
    {
        return AddInMemoryOpenTelemetryReceiver(services, options, configure: null);
    }

    public static IServiceCollection AddInMemoryOpenTelemetryReceiver(this IServiceCollection services, InMemoryOpenTelemetryHandlerOptions? options, Action<OpenTelemetryReceiverOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<InMemoryOpenTelemetryHandler>(_ => new InMemoryOpenTelemetryHandler(options ?? new()));
        services.AddOpenTelemetryReceiver(static serviceProvider => serviceProvider.GetRequiredService<InMemoryOpenTelemetryHandler>(), configure);
        return services;
    }
}
