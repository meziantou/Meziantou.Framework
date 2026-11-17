using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Meziantou.AspNetCore.Testing.Components.JSInterop;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Meziantou.AspNetCore.Testing.Components.Internals;

/// <summary>
/// The <see cref="IJSRuntime"/> of the components: the calls set up in <see cref="ComponentJSInterop"/>, then the functions
/// of Blazor implemented by the test host, then the functions of the page when it runs JavaScript.
/// </summary>
internal sealed class TestJSRuntime(ComponentJSInterop interop, Func<Document> document, Func<PageJSRuntime?> pageRuntime) : IJSRuntime
{
    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(string identifier, object?[]? args)
        => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The results are converted to the types the components expect")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The results are converted to the types the components expect")]
    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        var invocation = new JSInvocation(identifier, args ?? [], typeof(TValue));
        interop.Record(invocation);

        if (interop.TryGetHandler(identifier, out var handler))
            return ValueTask.FromResult(ConvertResult<TValue>(handler(invocation)));

        if (TryInvokeBuiltIn(invocation, out var builtInResult))
            return ValueTask.FromResult(ConvertResult<TValue>(builtInResult));

        if (pageRuntime() is { } page)
            return page.InvokeAsync<TValue>(identifier, cancellationToken, args);

        if (interop.Mode is JSInteropMode.Strict)
            return ValueTask.FromException<TValue>(new UnplannedJSInvocationException(invocation));

        return ValueTask.FromResult(default(TValue)!);
    }

    private bool TryInvokeBuiltIn(JSInvocation invocation, out object? result)
    {
        result = null;
        switch (invocation.Identifier)
        {
            // ElementReference.FocusAsync()
            case "Blazor._internal.domWrapper.focus":
                if (invocation.Arguments is [ElementReference reference, ..] && GetElement(reference) is HtmlElement element)
                {
                    element.Focus(new FocusOptions { PreventScroll = invocation.Arguments is [_, true] });
                }

                return true;

            case "Blazor._internal.domWrapper.focusBySelector":
                if (invocation.Arguments is [string selector, ..] && document().QuerySelector(selector) is HtmlElement selectedElement)
                {
                    selectedElement.Focus();
                }

                return true;

            default:
                return false;
        }
    }

    public Element? GetElement(ElementReference reference) => document().QuerySelector("[" + ElementReferences.GetAttributeName(reference.Id) + "]");

    [RequiresUnreferencedCode("The result is converted with System.Text.Json")]
    [RequiresDynamicCode("The result is converted with System.Text.Json")]
    private static TValue ConvertResult<TValue>(object? result)
    {
        if (result is null)
            return default!;

        if (result is TValue value)
            return value;

        // Convert the result as the JSON interop does
        return JsonSerializer.Deserialize<TValue>(JsonSerializer.Serialize(result, result.GetType(), JsonSerializerOptions.Web), JsonSerializerOptions.Web)!;
    }
}
