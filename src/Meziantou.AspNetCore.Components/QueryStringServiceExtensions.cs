using System;
using System.Collections.Generic;
using Meziantou.AspNetCore.Components.Internals;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Meziantou.AspNetCore.Components;

public static class QueryStringServiceExtensions
{
    public static IServiceCollection AddQueryStringParameters(this IServiceCollection services)
    {
        services.AddScoped<QueryStringService>();
        return services;
    }

    public static string? GetQueryStringParameterValue(this NavigationManager navigationManager, string parameterName)
    {
        var values = GetQueryStringParameterValues(navigationManager, parameterName);
        if (values == null || values.Count == 0)
            return null;

        return values[0];
    }

    public static IReadOnlyList<string> GetQueryStringParameterValues(this NavigationManager navigationManager, string parameterName)
    {
        if (Uri.TryCreate(navigationManager.Uri, UriKind.RelativeOrAbsolute, out var uri))
        {
            var parameters = QueryHelpers.ParseNullableQuery(uri.Query);
            if (parameters != null && parameters.TryGetValue(parameterName, out var values))
            {
                return values.ToArray();
            }
        }

        return Array.Empty<string>();
    }
}
