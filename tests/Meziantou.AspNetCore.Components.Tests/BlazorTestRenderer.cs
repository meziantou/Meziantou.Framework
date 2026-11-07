using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meziantou.AspNetCore.Components.Tests;

/// <summary>Renders a component the way static server-side rendering does, and returns the produced markup.</summary>
internal static class BlazorTestRenderer
{
    public static Task<string> RenderAsync<T>(params (string Name, object? Value)[] parameters)
        where T : IComponent
    {
        var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, value) in parameters)
        {
            dictionary[name] = value;
        }

        return RenderAsync<T>(dictionary);
    }

    public static async Task<string> RenderAsync<T>(Dictionary<string, object?> parameters)
        where T : IComponent
    {
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(serviceProvider, NullLoggerFactory.Instance);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters));
            return component.ToHtmlString();
        });
    }
}
