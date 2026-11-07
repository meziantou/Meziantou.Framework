using System.Reflection;
using Meziantou.AspNetCore.Components.Internals;
using Microsoft.AspNetCore.Components;

namespace Meziantou.AspNetCore.Components;

/// <summary>Extension methods for <see cref="NavigationManager"/> to work with query string parameters.</summary>
public static class NavigationManagerExtensions
{
    /// <summary>Gets the first value of a query string parameter from the current URL.</summary>
    /// <param name="navigationManager">The navigation manager.</param>
    /// <param name="parameterName">The name of the query string parameter.</param>
    /// <returns>The first value of the parameter, or <see langword="null"/> if the parameter is not found.</returns>
    public static string? GetQueryStringParameterValue(this NavigationManager navigationManager, string parameterName)
    {
        var values = GetQueryStringParameterValues(navigationManager, parameterName);
        if (values is null || values.Count == 0)
            return null;

        return values[0];
    }

    /// <summary>Gets all values of a query string parameter from the current URL.</summary>
    /// <param name="navigationManager">The navigation manager.</param>
    /// <param name="parameterName">The name of the query string parameter.</param>
    /// <returns>A read-only list of all values for the parameter, or an empty list if the parameter is not found.</returns>
    public static IReadOnlyList<string> GetQueryStringParameterValues(this NavigationManager navigationManager, string parameterName)
    {
        if (Uri.TryCreate(navigationManager.Uri, UriKind.RelativeOrAbsolute, out var uri))
        {
            var parameters = QueryHelpers.ParseNullableQuery(uri.Query);
            if (parameters is not null && parameters.TryGetValue(parameterName, out var values))
            {
                return values.ToArray()!;
            }
        }

        return [];
    }

    /// <summary>Updates the current URL with query string parameters from component properties marked with <see cref="SupplyParameterFromQueryAttribute"/>.</summary>
    /// <param name="navigationManager">The navigation manager.</param>
    /// <param name="component">The component whose properties should be used to update the query string.</param>
    /// <param name="replaceHistory">Whether to replace the current history entry instead of adding a new one.</param>
    public static void UpdateUrlUsingParameters(this NavigationManager navigationManager, IComponent component, bool replaceHistory = false)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in GetProperties(component.GetType()))
        {
            var parameterName = GetQueryStringParameterName(property);
            if (parameterName is null)
                continue;

            var value = property.GetValue(component);
            parameters[parameterName] = NormalizeQueryValue(value);
        }

        // Compute the new URL
        var newUri = navigationManager.GetUriWithQueryParameters(parameters);
        if (newUri != navigationManager.Uri)
        {
            navigationManager.NavigateTo(newUri, new NavigationOptions() { ReplaceHistoryEntry = replaceHistory });
        }
    }

    // GetUriWithQueryParameters only accepts a fixed set of types and throws for anything else. Values it cannot
    // handle (enums, TimeSpan, custom types, ...) are converted to their string representation instead.
    private static object? NormalizeQueryValue(object? value)
    {
        if (value is null)
            return null;

        var type = value.GetType();
        if (type == typeof(string) || type == typeof(bool) || type == typeof(int) || type == typeof(long)
            || type == typeof(float) || type == typeof(double) || type == typeof(decimal)
            || type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateOnly) || type == typeof(TimeOnly))
        {
            return value;
        }

        // Collections of supported types are handled natively by GetUriWithQueryParameters
        if (value is System.Collections.IEnumerable)
            return value;

        return ValueConverter.ConvertToString(value);
    }

    private static PropertyInfo[] GetProperties(Type type)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
    }

    private static string? GetQueryStringParameterName(PropertyInfo property)
    {
        if (property.GetCustomAttribute<ParameterAttribute>() is null)
            return null;

        var attribute = property.GetCustomAttribute<SupplyParameterFromQueryAttribute>();
        if (attribute is null)
            return null;

        return attribute.Name ?? property.Name;
    }
}
