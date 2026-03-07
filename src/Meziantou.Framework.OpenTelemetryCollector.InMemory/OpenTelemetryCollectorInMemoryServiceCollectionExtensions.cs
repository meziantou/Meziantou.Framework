using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meziantou.Framework.OpenTelemetryCollector.InMemory;

public static class OpenTelemetryCollectorInMemoryServiceCollectionExtensions
{
    public static IServiceCollection AddInMemoryOpenTelemetryReceiver(this IServiceCollection services)
    {
        return AddInMemoryOpenTelemetryReceiver(services, options: null);
    }

    public static IServiceCollection AddInMemoryOpenTelemetryReceiver(this IServiceCollection services, InMemoryOpenTelemetryHandlerOptions? options)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<InMemoryOpenTelemetryHandler>(_ => new InMemoryOpenTelemetryHandler(options ?? new()));
        services.AddOpenTelemetryReceiver(static serviceProvider => serviceProvider.GetRequiredService<InMemoryOpenTelemetryHandler>());
        return services;
    }
}
