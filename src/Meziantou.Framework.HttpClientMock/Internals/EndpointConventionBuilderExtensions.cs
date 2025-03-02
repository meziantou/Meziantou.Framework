#if NET6_0
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Meziantou.Framework.Internals;

internal static class EndpointConventionBuilderExtensions
{
    public static TBuilder WithOrder<TBuilder>(this TBuilder builder, int order) where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(builder =>
        {
            if (builder is RouteEndpointBuilder routeEndpointBuilder)
            {
                routeEndpointBuilder.Order = order;
            }
            else
            {
                throw new InvalidOperationException("This endpoint does not support Order.");
            }
        });
        return builder;
    }
}
#endif
