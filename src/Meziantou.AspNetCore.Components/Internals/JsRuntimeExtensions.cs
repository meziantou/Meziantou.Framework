using Microsoft.JSInterop;

namespace Meziantou.AspNetCore.Components.Internals;

internal static class JsRuntimeExtensions
{
    // Only the failures caused by the component or the circuit going away are ignored. Errors raised by the
    // JavaScript code itself must reach the caller: swallowing them turns a failed operation into a silent no-op.
    private static bool IsIgnorable(Exception exception)
        => exception is JSDisconnectedException or OperationCanceledException or ObjectDisposedException;

    public static async ValueTask SafeInvokeVoidAsync(this IJSRuntime jsRuntime, string identifier, params object?[] args)
    {
        try
        {
            await jsRuntime.InvokeVoidAsync(identifier, args);
        }
        catch (Exception ex) when (IsIgnorable(ex))
        {
        }
    }

    public static async ValueTask SafeInvokeVoidAsync(this IJSRuntime jsRuntime, string identifier, CancellationToken cancellationToken, params object?[] args)
    {
        try
        {
            await jsRuntime.InvokeVoidAsync(identifier, cancellationToken, args);
        }
        catch (Exception ex) when (IsIgnorable(ex))
        {
        }
    }

    public static async ValueTask SafeInvokeVoidAsync(this IJSObjectReference jsRuntime, string identifier, CancellationToken cancellationToken, params object?[] args)
    {
        try
        {
            await jsRuntime.InvokeVoidAsync(identifier, cancellationToken, args);
        }
        catch (Exception ex) when (IsIgnorable(ex))
        {
        }
    }

    public static async ValueTask SafeInvokeVoidAsync(this IJSObjectReference jsRuntime, string identifier, params object?[] args)
    {
        try
        {
            await jsRuntime.InvokeVoidAsync(identifier, args);
        }
        catch (Exception ex) when (IsIgnorable(ex))
        {
        }
    }
}
