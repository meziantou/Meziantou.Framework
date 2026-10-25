using System.Net;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.JavaScript;

namespace Meziantou.Framework.WebPlatform.Playwright;

/// <summary>The options of a <see cref="PlaywrightServer"/>.</summary>
public sealed class PlaywrightServerOptions
{
    /// <summary>Gets or sets the address the server listens on. The default is <see cref="IPAddress.Loopback"/>: only the local clients can connect.</summary>
    public IPAddress BindAddress { get; set; } = IPAddress.Loopback;

    /// <summary>Gets or sets the TCP port the server listens on. The default is 0: a free port is chosen (see <see cref="PlaywrightServer.Port"/>).</summary>
    public int Port { get; set; }

    /// <summary>
    /// Gets or sets the path of the WebSocket endpoint (the clients connect to <c>ws://host:port/path</c>). The default is
    /// <c>/</c>. Use an unguessable path to prevent the other users of the machine from connecting.
    /// </summary>
    public string Path { get; set; } = "/";

    /// <summary>
    /// Gets or sets the loader of the documents and of the resources of the pages (see <see cref="BrowsingContextOptions.DocumentLoader"/>).
    /// The default is null: nothing is fetched from the network, the pages can only use the content set by
    /// <c>page.setContent()</c> and the responses of the routes (<c>page.route()</c>). Use <see cref="DocumentLoader.Create"/> to
    /// let the pages load any URL (the clients can then make the server send requests to any host: do not expose such a server
    /// to untrusted clients).
    /// </summary>
    public DocumentLoader? DocumentLoader { get; set; }

    /// <summary>
    /// Gets or sets the options of the script engines of the browser contexts (one engine per browser context), e.g. the
    /// timeout of a script. The default is null: the default options.
    /// </summary>
    public JavaScriptScriptEngineOptions? ScriptEngineOptions { get; set; }

    /// <summary>
    /// Gets or sets whether the pages can use the fonts installed on the machine (see <see cref="BrowsingContextOptions.UseSystemFonts"/>).
    /// The default is false: the pages use the fonts bundled with the library and their web fonts.
    /// </summary>
    public bool UseSystemFonts { get; set; }

    /// <summary>
    /// Gets or sets the name of the browser reported to the clients (<c>browser.browserType().name()</c>). The clients
    /// enable some features depending on it. The default is <c>chromium</c>.
    /// </summary>
    public string BrowserName { get; set; } = "chromium";

    /// <summary>
    /// Gets or sets whether the clients of another version of Playwright can connect. The protocol changes with each minor
    /// version of Playwright: by default, the server rejects the clients whose major and minor versions differ from
    /// <see cref="PlaywrightServer.PlaywrightVersion"/> (as the Playwright server does), with an HTTP 428 response.
    /// </summary>
    public bool AllowAnyClientVersion { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of simultaneous connections. The default is null: no limit. A client that connects
    /// while the limit is reached waits for a connection to close.
    /// </summary>
    public int? MaxConnections { get; set; }
}
