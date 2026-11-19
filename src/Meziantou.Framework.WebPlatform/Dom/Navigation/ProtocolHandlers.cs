using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The <c>registerProtocolHandler()</c> automation mode of a document (https://html.spec.whatwg.org/multipage/system-state.html#registerprotocolhandler()-automation-mode),
/// set by the <c>Set RPH Registration Mode</c> WebDriver extension command: the registrations of the document are accepted or
/// rejected without asking the user.
/// </summary>
public enum ProtocolHandlerAutomationMode
{
    /// <summary>The user is asked (<see cref="ProtocolHandlerOptions.RequestRegistration"/>).</summary>
    None,

    /// <summary>The registrations are accepted, as if the user accepted them.</summary>
    AutoAccept,

    /// <summary>The registrations are rejected, as if the user rejected them.</summary>
    AutoReject,
}

/// <summary>A request of a page to register a protocol handler (<c>navigator.registerProtocolHandler()</c>).</summary>
/// <param name="Document">The document that requests the registration.</param>
/// <param name="Scheme">The normalized scheme (lowercase, e.g. <c>mailto</c> or <c>web+soup</c>).</param>
/// <param name="Url">The normalized URL of the handler: an absolute <c>http:</c> or <c>https:</c> URL that contains <c>%s</c>.</param>
public sealed record ProtocolHandlerRegistration(Document Document, string Scheme, string Url);

/// <summary>
/// The host hooks of the protocol handlers (https://html.spec.whatwg.org/multipage/system-state.html#custom-handlers): the
/// handlers that the pages register with <c>navigator.registerProtocolHandler()</c> are stored in <see cref="Registry"/>,
/// and a navigation to a URL of their scheme navigates to the handler instead.
/// </summary>
public sealed class ProtocolHandlerOptions
{
    /// <summary>Gets the handlers of the user agent: the browsing contexts that share these options share their handlers.</summary>
    public ProtocolHandlerRegistry Registry { get; init; } = new();

    /// <summary>
    /// Gets or sets the callback that asks the user whether to register a handler, when the <c>registerProtocolHandler()</c>
    /// automation mode of the document is <see cref="ProtocolHandlerAutomationMode.None"/>. It returns true when the user
    /// accepts. It is invoked on the thread of the event loop, and must not block it. The default is null: there is no user
    /// to accept the registrations, so they are not registered.
    /// </summary>
    public Func<ProtocolHandlerRegistration, bool>? RequestRegistration { get; init; }
}

/// <summary>The protocol handlers registered by the pages (https://html.spec.whatwg.org/multipage/system-state.html#custom-handlers). It is thread-safe.</summary>
public sealed class ProtocolHandlerRegistry
{
    private readonly Lock _lock = new();
    private readonly List<(string Scheme, string Url)> _handlers = [];

    /// <summary>Gets the registered handlers (the scheme and the URL of each handler), in the order of their registration.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Handlers
    {
        get
        {
            lock (_lock)
            {
                return [.. _handlers.Select(handler => KeyValuePair.Create(handler.Scheme, handler.Url))];
            }
        }
    }

    /// <summary>Registers a handler for a scheme. A scheme can have several handlers: the last registered one is used.</summary>
    /// <param name="scheme">The scheme (e.g. <c>mailto</c> or <c>web+soup</c>).</param>
    /// <param name="url">The URL of the handler, in which the first <c>%s</c> is replaced with the URL to handle.</param>
    public void Register(string scheme, string url)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        ArgumentNullException.ThrowIfNull(url);
        lock (_lock)
        {
            _handlers.Remove((scheme, url));
            _handlers.Add((scheme, url));
        }
    }

    /// <summary>Removes the handler of a scheme registered with a URL. Returns false when there is none.</summary>
    /// <param name="scheme">The scheme.</param>
    /// <param name="url">The URL of the handler.</param>
    public bool Unregister(string scheme, string url)
    {
        lock (_lock)
        {
            return _handlers.Remove((scheme, url));
        }
    }

    /// <summary>Returns the URL of the handler used for a scheme (the last registered one), or null.</summary>
    /// <param name="scheme">The scheme.</param>
    public string? GetHandler(string scheme)
    {
        lock (_lock)
        {
            for (var i = _handlers.Count - 1; i >= 0; i--)
            {
                if (_handlers[i].Scheme == scheme)
                    return _handlers[i].Url;
            }

            return null;
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/system-state.html#protocol-handler-invocation: the URL of the handler of a URL,
    /// whose first <c>%s</c> is replaced with the URL (without its credentials, UTF-8 percent-encoded with the component
    /// percent-encode set), or null when its scheme has no handler or the result is not a valid URL.
    /// </summary>
    internal UrlRecord? Translate(UrlRecord url)
    {
        if (GetHandler(url.Scheme) is not { } handlerUrl)
            return null;

        var input = url.Clone();
        input.Username = "";
        input.Password = "";
        var encodedUrl = PercentEncoding.Utf8PercentEncode(input.Serialize(), PercentEncodeSet.Component);
        var index = handlerUrl.IndexOf("%s", StringComparison.Ordinal);
        if (index < 0)
            return null;

        return UrlParser.Parse(string.Concat(handlerUrl.AsSpan(0, index), encodedUrl, handlerUrl.AsSpan(index + 2)), baseUrl: null);
    }
}
