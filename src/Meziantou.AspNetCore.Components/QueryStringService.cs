using System.Reflection;
using System.Text.Json;
using Meziantou.AspNetCore.Components.Internals;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Primitives;
using Microsoft.JSInterop;

namespace Meziantou.AspNetCore.Components;

/// <summary>
/// Provides services for synchronizing component properties with URL query string parameters.
/// </summary>
/// <remarks>
/// <para>
/// This service allows you to bind component properties to query string parameters, making it easy to create shareable URLs
/// that preserve component state. Properties must be marked with <see cref="SupplyParameterFromQueryAttribute"/> to be synchronized.
/// To use this service, register it in your dependency injection container using <see cref="QueryStringServiceExtensions.AddQueryStringParameters"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // In Program.cs or Startup.cs
/// builder.Services.AddQueryStringParameters();
///
/// // In a component
/// @inject QueryStringService QueryStringService
///
/// @code {
///     [Parameter]
///     [SupplyParameterFromQuery]
///     public string? SearchTerm { get; set; }
///
///     [Parameter]
///     [SupplyParameterFromQuery]
///     public int PageNumber { get; set; } = 1;
///
///     protected override void OnInitialized()
///     {
///         // Read query string parameters and populate properties
///         QueryStringService.SetParametersFromQueryString(this);
///     }
///
///     private async Task UpdateSearch(string newSearchTerm)
///     {
///         SearchTerm = newSearchTerm;
///         // Update the URL with new query string parameters
///         await QueryStringService.UpdateQueryString(this);
///     }
/// }
/// </code>
/// </example>
public sealed class QueryStringService
{
    private readonly NavigationManager _navigationManager;
    private readonly IJSRuntime _jsRuntime;

    /// <summary>Initializes a new instance of the <see cref="QueryStringService"/> class.</summary>
    /// <param name="navigationManager">The navigation manager to use for URL operations.</param>
    /// <param name="jsRuntime">The JavaScript runtime to use for history manipulation.</param>
    public QueryStringService(NavigationManager navigationManager, IJSRuntime jsRuntime)
    {
        _navigationManager = navigationManager;
        _jsRuntime = jsRuntime;
    }

    /// <summary>Reads query string parameters from the current URL and sets them on the component properties marked with <see cref="SupplyParameterFromQueryAttribute"/>.</summary>
    /// <typeparam name="T">The type of the component.</typeparam>
    /// <param name="component">The component whose properties should be populated from the query string.</param>
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
            if (parameterName is null)
                continue; // The property is not decorated by [QueryStringParameterAttribute]

            if (queryString.TryGetValue(parameterName, out var value))
            {
                // Convert the value from string to the actual property type
                var convertedValue = ConvertValue(value, property.PropertyType);
                property.SetValue(component, convertedValue);
            }
        }
    }

    /// <summary>Updates the URL query string with values from component properties marked with <see cref="SupplyParameterFromQueryAttribute"/>.</summary>
    /// <typeparam name="T">The type of the component.</typeparam>
    /// <param name="component">The component whose properties should be written to the query string.</param>
    /// <param name="reloadPage">Whether to reload the page after updating the query string. If <c>false</c>, the URL is updated using the browser's history API without reloading.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
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
            if (parameterName is null)
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
            await _jsRuntime.InvokeVoidAsync("window.history.replaceState", cancellationToken, args: [null, "", newUri]);
        }
    }

    private static object? ConvertValue(StringValues value, Type type)
    {
        var firstValue = value[0];
        if (type == typeof(string))
            return firstValue;

        if (firstValue is null)
            return Activator.CreateInstance(type);

        return JsonSerializer.Deserialize(firstValue, type);
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
#pragma warning disable CS0618 // Type or member is obsolete
        var attribute = property.GetCustomAttribute<QueryStringParameterAttribute>();
#pragma warning restore CS0618

        if (attribute is not null)
            return attribute.Name ?? property.Name;

        if (property.GetCustomAttribute<ParameterAttribute>() is not null && property.GetCustomAttribute<SupplyParameterFromQueryAttribute>() is { } supplyAttribute)
            return supplyAttribute.Name ?? property.Name;

        return null;
    }
}
