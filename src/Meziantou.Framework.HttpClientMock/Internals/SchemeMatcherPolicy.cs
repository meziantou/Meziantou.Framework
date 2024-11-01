using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Matching;

namespace Meziantou.Framework.Internals;

[SuppressMessage("Performance", "CA1812", Justification = "Use by dependency injection")]
internal sealed class SchemeMatcherPolicy : MatcherPolicy, IEndpointSelectorPolicy
{
    public override int Order => 0;

    bool IEndpointSelectorPolicy.AppliesToEndpoints(IReadOnlyList<Endpoint> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return AppliesToEndpointsCore(endpoints);
    }

    private static bool AppliesToEndpointsCore(IReadOnlyList<Endpoint> endpoints)
    {
        foreach (var endpoint in endpoints)
        {
            if (endpoint.Metadata.GetMetadata<SchemeMetadata>()?.Scheme is not null)
                return true;
        }

        return false;
    }

    public Task ApplyAsync(HttpContext httpContext, CandidateSet candidates)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(candidates);

        for (var i = 0; i < candidates.Count; i++)
        {
            var metadata = candidates[i].Endpoint?.Metadata.GetMetadata<SchemeMetadata>();
            if (metadata == null || metadata.Scheme is null)
                continue;

            if (!candidates.IsValidCandidate(i))
                continue;

            var httpMethod = httpContext.Request.Scheme;
            if (!string.Equals(httpMethod, metadata.Scheme, StringComparison.OrdinalIgnoreCase))
            {
                candidates.SetValidity(i, false);
                continue;
            }
        }

        return Task.CompletedTask;
    }
}
