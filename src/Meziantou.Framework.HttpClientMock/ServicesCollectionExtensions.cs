using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Meziantou.Framework;

public static class ServicesCollectionExtensions
{
    public static IServiceCollection AddHttpClientMock(this IServiceCollection services, Action<HttpMockServiceBuilder> builder)
    {
        return AddHttpClientMock(services, (_, b) => builder(b));
    }

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
