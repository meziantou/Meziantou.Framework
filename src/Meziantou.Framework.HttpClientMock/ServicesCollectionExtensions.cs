using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Meziantou.Framework;

/// <summary>
/// Extension methods for registering <see cref="HttpClientMock"/> instances with the service collection.
/// </summary>
/// <summary>
/// Extension methods for registering <see cref="HttpClientMock"/> instances with the service collection.
/// </summary>
public static class ServicesCollectionExtensions
{
    /// <summary>Registers HTTP client mocks in the service collection.</summary>
    /// <param name="services">The service collection to add the mocks to.</param>
    /// <param name="builder">An action to configure the HTTP client mocks.</param>
    /// <returns>The service collection for chaining additional calls.</returns>
    public static IServiceCollection AddHttpClientMock(this IServiceCollection services, Action<HttpMockServiceBuilder> builder)
    {
        return AddHttpClientMock(services, (_, b) => builder(b));
    }

    /// <summary>Registers HTTP client mocks in the service collection with access to the service provider.</summary>
    /// <param name="services">The service collection to add the mocks to.</param>
    /// <param name="builder">An action to configure the HTTP client mocks with access to the service provider.</param>
    /// <returns>The service collection for chaining additional calls.</returns>
    public static IServiceCollection AddHttpClientMock(this IServiceCollection services, Action<IServiceProvider, HttpMockServiceBuilder> builder)
    {
        services.AddTransient<HttpMessageHandlerBuilder>(serviceProvider =>
        {
            var instance = new HttpMockServiceBuilder();
            builder?.Invoke(serviceProvider, instance);
            return instance.Builder;
        });

        return services;
    }
}
