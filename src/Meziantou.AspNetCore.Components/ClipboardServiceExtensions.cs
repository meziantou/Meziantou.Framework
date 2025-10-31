using Microsoft.Extensions.DependencyInjection;

namespace Meziantou.AspNetCore.Components;

/// <summary>Extension methods for adding clipboard services to the dependency injection container.</summary>
public static class ClipboardServiceExtensions
{
    /// <summary>Adds the <see cref="ClipboardService"/> to the service collection.</summary>
    /// <param name="serviceCollection">The service collection to add the service to.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddClipboard(this IServiceCollection serviceCollection)
    {
        return serviceCollection.AddScoped<ClipboardService>();
    }
}
