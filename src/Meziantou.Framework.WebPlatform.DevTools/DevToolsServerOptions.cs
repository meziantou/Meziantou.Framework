using System.Net;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.JavaScript;

namespace Meziantou.Framework.WebPlatform.DevTools;

/// <summary>The options of a <see cref="DevToolsServer"/>.</summary>
public sealed class DevToolsServerOptions
{
    /// <summary>Gets or sets the address the server listens on. The default is <see cref="IPAddress.Loopback"/>: only the local clients can connect.</summary>
    public IPAddress BindAddress { get; set; } = IPAddress.Loopback;

    /// <summary>Gets or sets the TCP port the server listens on. The default is 0: a free port is chosen (see <see cref="DevToolsServer.Port"/>).</summary>
    public int Port { get; set; }

    /// <summary>
    /// Gets or sets the loader of the documents and of the resources of the pages (see <see cref="BrowsingContextOptions.DocumentLoader"/>).
    /// The default is null: nothing is fetched from the network, the pages can only use the content they are given (e.g.
    /// <c>page.setContent()</c>) and the responses of the interception of the requests (the <c>Fetch</c> domain, e.g.
    /// <c>page.route()</c>). Use <see cref="DocumentLoader.Create"/> to let the pages load any URL (the clients can then make the
    /// server send requests to any host: do not expose such a server to untrusted clients).
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
    /// Gets or sets the maximum number of simultaneous connections. The default is null: no limit. A client that connects
    /// while the limit is reached waits for a connection to close.
    /// </summary>
    public int? MaxConnections { get; set; }
}
