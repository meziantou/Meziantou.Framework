using System.Reflection;
using System.Text.Json;
using Meziantou.AspNetCore.Components.Internals;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Primitives;
using Microsoft.JSInterop;

namespace Meziantou.AspNetCore.Components;

public sealed class QueryStringService
{
    private readonly NavigationManager _navigationManager;
    private readonly IJSRuntime _jsRuntime;

    public QueryStringService(NavigationManager navigationManager, IJSRuntime jsRuntime)
    {
        _navigationManager = navigationManager;
        _jsRuntime = jsRuntime;
    }

    /// <summary>
    /// Apply the values from the query string to the current component
    /// </summary>
    public void SetParametersFromQueryString<T>(T component)
        where T : IComponent
    {
        if (!Uri.TryCreate(_navigationManager.Uri, UriKind.RelativeOrAbsolute, out var uri))
            throw new InvalidOperationException("The current url is not a valid URI. Url: " + _navigationManager.Uri);

        // Parse the query string
        var queryString = QueryHelpers.ParseQuery(uri.Query);

        // Enumerate all properties of the component
        foreach (var property in GetProperties<T>())
        {
            // Get the name of the parameter to read from the query string
            var parameterName = GetQueryStringParameterName(property);
            if (parameterName == null)
                continue; // The property is not decorated by [QueryStringParameterAttribute]

            if (queryString.TryGetValue(parameterName, out var value))
            {
                // Convert the value from string to the actual property type
                var convertedValue = ConvertValue(value, property.PropertyType);
                property.SetValue(component, convertedValue);
            }
        }
    }

    /// <summary>
    /// Apply the values from the component to the query string
    /// </summary>
    public async ValueTask UpdateQueryString<T>(T component, bool reloadPage = true, CancellationToken cancellationToken = default)
        where T : IComponent
    {
        if (!Uri.TryCreate(_navigationManager.Uri, UriKind.RelativeOrAbsolute, out var uri))
            throw new InvalidOperationException("The current url is not a valid URI. Url: " + _navigationManager.Uri);

        // Fill the dictionary with the parameters of the component
        var parameters = QueryHelpers.ParseQuery(uri.Query);
        foreach (var property in GetProperties<T>())
        {
            var parameterName = GetQueryStringParameterName(property);
            if (parameterName == null)
                continue;

            var value = property.GetValue(component);
            if (value is null)
            {
                parameters.Remove(parameterName);
            }
            else
            {
                var convertedValue = ConvertToString(value);
                parameters[parameterName] = convertedValue;
            }
        }

        // Compute the new URL
        var newUri = uri.GetComponents(UriComponents.Scheme | UriComponents.Host | UriComponents.Port | UriComponents.Path, UriFormat.UriEscaped);
        foreach (var parameter in parameters)
        {
            foreach (var value in parameter.Value)
            {
                newUri = QueryHelpers.AddQueryString(newUri, parameter.Key, value);
            }
        }

        if (reloadPage)
        {
            _navigationManager.NavigateTo(newUri);
        }
        else
        {
            await _jsRuntime.InvokeVoidAsync("window.history.replaceState", cancellationToken, args: new object?[] { null, "", newUri });
        }
    }

    private static object? ConvertValue(StringValues value, Type type)
    {
        if (type == typeof(string))
            return value[0];

        return JsonSerializer.Deserialize(value[0], type);
    }

    private static string ConvertToString(object value)
    {
        if (value is string s)
            return s;

        return JsonSerializer.Serialize(value);
    }

    private static PropertyInfo[] GetProperties<T>()
    {
        return typeof(T).GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
    }

    private static string? GetQueryStringParameterName(PropertyInfo property)
    {
        var attribute = property.GetCustomAttribute<QueryStringParameterAttribute>();
        if (attribute != null)
            return attribute.Name ?? property.Name;

        if (property.GetCustomAttribute<ParameterAttribute>() != null && property.GetCustomAttribute<SupplyParameterFromQueryAttribute>() is { } supplyAttribute)
            return supplyAttribute.Name ?? property.Name;

        return null;
    }
}
