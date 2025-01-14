using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Matching;

namespace Meziantou.Framework.Internals;

[SuppressMessage("Performance", "CA1812", Justification = "Use by dependency injection")]
internal sealed class QueryStringMatcherPolicy : MatcherPolicy, IEndpointSelectorPolicy
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
            if (endpoint.Metadata.GetMetadata<QueryStringMetadata>()?.QueryString is not null)
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
            var metadata = candidates[i].Endpoint?.Metadata.GetMetadata<QueryStringMetadata>();
            if (metadata == null || metadata.QueryString is null)
                continue;

            if (!candidates.IsValidCandidate(i))
                continue;

            if (httpContext.Request.QueryString.HasValue && MatchQueryString(httpContext, metadata))
                continue;

            candidates.SetValidity(i, false);
        }

        return Task.CompletedTask;
    }

    private static bool MatchQueryString(HttpContext context, QueryStringMetadata metadata)
    {
        if (context.Request.QueryString.Value == metadata.QueryString)
            return true;

        if (context.Request.Query.Count != metadata.Query.Count)
            return false;

        foreach (var kvp in metadata.Query)
        {
            if (!context.Request.Query.TryGetValue(kvp.Key, out var values))
                return false;

            if (values.Count != kvp.Value.Count)
                return false;

            foreach (var value in kvp.Value)
            {
                if (!values.Contains(value, StringComparer.Ordinal))
                    return false;
            }
        }

        return true;
    }
}
