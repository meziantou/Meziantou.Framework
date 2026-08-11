using Microsoft.JSInterop;

namespace Meziantou.AspNetCore.Components.Internals;

public static class JsRuntimeExtensions
{
    public static async ValueTask SafeInvokeVoidAsync(this IJSRuntime jsRuntime, string identifier, params object?[] args)
    {
        try
        {
            await jsRuntime.InvokeVoidAsync(identifier, args);
        }
        catch
        {
        }
    }

    public static async ValueTask SafeInvokeVoidAsync(this IJSRuntime jsRuntime, string identifier, CancellationToken cancellationToken, params object?[] args)
    {
        try
        {
            await jsRuntime.InvokeVoidAsync(identifier, cancellationToken, args);
        }
        catch
        {
        }
    }

    public static async ValueTask SafeInvokeVoidAsync(this IJSObjectReference jsRuntime, string identifier, CancellationToken cancellationToken, params object?[] args)
    {
        try
        {
            await jsRuntime.InvokeVoidAsync(identifier, cancellationToken, args);
        }
        catch
        {
        }
    }

    public static async ValueTask SafeInvokeVoidAsync(this IJSObjectReference jsRuntime, string identifier, params object?[] args)
    {
        try
        {
            await jsRuntime.InvokeVoidAsync(identifier, args);
        }
        catch
        {
        }
    }
}