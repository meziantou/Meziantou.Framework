using Microsoft.JSInterop;

namespace Meziantou.AspNetCore.Components;

/// <summary>Provides access to the browser's clipboard API for reading and writing text.</summary>
/// <remarks>
/// <para>
/// This service provides a convenient way to interact with the browser's clipboard in Blazor applications.
/// To use this service, register it in your dependency injection container using <see cref="ClipboardServiceExtensions.AddClipboard"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // In Program.cs or Startup.cs
/// builder.Services.AddClipboard();
///
/// // In a component
/// @inject ClipboardService ClipboardService
///
/// &lt;button @onclick="CopyToClipboard"&gt;Copy&lt;/button&gt;
///
/// @code {
///     private async Task CopyToClipboard()
///     {
///         await ClipboardService.WriteTextAsync("Hello, World!");
///     }
/// }
/// </code>
/// </example>
/// <seealso href="https://www.meziantou.net/copying-text-to-clipboard-in-a-blazor-application.htm"/>
public sealed class ClipboardService
{
    private readonly IJSRuntime _jsRuntime;

    /// <summary>Initializes a new instance of the <see cref="ClipboardService"/> class.</summary>
    /// <param name="jsRuntime">The JavaScript runtime to use for interoperability.</param>
    public ClipboardService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    /// <summary>Reads text from the clipboard.</summary>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the text from the clipboard.</returns>
    public ValueTask<string> ReadTextAsync(CancellationToken cancellationToken = default)
    {
#pragma warning disable BL0016 // Unguarded JS interop call
        return _jsRuntime.InvokeAsync<string>("navigator.clipboard.readText", cancellationToken);
#pragma warning restore BL0016 // Unguarded JS interop call
    }

    /// <summary>Writes text to the clipboard.</summary>
    /// <param name="text">The text to write to the clipboard.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="JSException">
    /// The browser refused the write. This happens in a non-secure (non-HTTPS) context, when the clipboard permission
    /// is denied, or when the call does not originate from a user gesture. The failure is reported rather than
    /// swallowed so the caller does not report success for a copy that never happened.
    /// </exception>
    public ValueTask WriteTextAsync(string text, CancellationToken cancellationToken = default)
    {
#pragma warning disable BL0016 // Unguarded JS interop call
        return _jsRuntime.InvokeVoidAsync("navigator.clipboard.writeText", cancellationToken, text);
#pragma warning restore BL0016 // Unguarded JS interop call
    }
}
