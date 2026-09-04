using Microsoft.AspNetCore.Builder;

namespace Meziantou.Framework.OpenTelemetryCollector;

/// <summary>Applies conventions to every endpoint mapped by <see cref="OpenTelemetryEndpointRouteBuilderExtensions.MapOpenTelemetryReceiverEndpoints"/>.</summary>
internal sealed class OpenTelemetryEndpointConventionBuilder(List<IEndpointConventionBuilder> builders) : IEndpointConventionBuilder
{
    private readonly List<IEndpointConventionBuilder> _builders = builders;

    public void Add(Action<EndpointBuilder> convention)
    {
        foreach (var builder in _builders)
        {
            builder.Add(convention);
        }
    }

    public void Finally(Action<EndpointBuilder> finallyConvention)
    {
        foreach (var builder in _builders)
        {
            builder.Finally(finallyConvention);
        }
    }
}
