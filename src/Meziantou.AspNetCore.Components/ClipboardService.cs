using System.Threading;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace Meziantou.AspNetCore.Components
{
    /// <seealso cref="https://www.meziantou.net/copying-text-to-clipboard-in-a-blazor-application.htm"/>
    public sealed class ClipboardService
    {
        private readonly IJSRuntime _jsRuntime;

        public ClipboardService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public ValueTask<string> ReadTextAsync(CancellationToken cancellationToken = default)
        {
            return _jsRuntime.InvokeAsync<string>("navigator.clipboard.readText", cancellationToken);
        }

        public ValueTask WriteTextAsync(string text, CancellationToken cancellationToken = default)
        {
            return _jsRuntime.InvokeVoidAsync("navigator.clipboard.writeText", cancellationToken, text);
        }
    }
}
