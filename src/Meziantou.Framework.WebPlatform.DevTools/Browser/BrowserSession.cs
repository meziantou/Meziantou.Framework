using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.DevTools.Protocol;

namespace Meziantou.Framework.WebPlatform.DevTools.Browser;

/// <summary>The browser session of a connection: the commands without <c>sessionId</c> (the <c>Browser</c>, <c>Target</c> and <c>Storage</c> domains).</summary>
internal sealed class BrowserSession(CdpConnection connection, DevToolsBrowser browser) : CdpSession(connection, id: null)
{
    public override Task<JsonObject?> InvokeAsync(string method, JsonElement parameters) => browser.InvokeAsync(method, parameters);
}
