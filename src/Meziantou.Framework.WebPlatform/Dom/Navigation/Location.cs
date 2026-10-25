using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// https://html.spec.whatwg.org/multipage/nav-history-apis.html#the-location-interface. The fragment navigations are
/// performed synchronously; the other navigations use <see cref="BrowsingContextOptions.DocumentLoader"/> and are ignored when
/// there is none.
/// </summary>
/// <remarks>
/// The URLs given to href, assign() and replace() are resolved against the entry document (the document of the running
/// script, or the relevant document outside a script). The scripting bindings check that the members other than the
/// setter of <see cref="Href"/> and <see cref="Replace"/> are accessed by a same origin-domain realm.
/// </remarks>
/// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#location"/>
[IdlInterface("Location", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#location")]
public sealed class Location
{
    private static readonly UrlRecord AboutBlank = UrlParser.Parse("about:blank", baseUrl: null)!;

    private readonly Window _window;
    private DomStringList? _emptyAncestorOrigins;

    internal Location(Window window)
    {
        _window = window;
    }

    /// <summary>The relevant global object of the Location object.</summary>
    internal Window Window => _window;

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#relevant-document</summary>
    private Document? RelevantDocument => _window.BrowsingContext?.Document;

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#concept-location-url</summary>
    private UrlRecord Url => RelevantDocument?.UrlRecord ?? AboutBlank;

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-href</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-href"/>
    [IdlMember("href", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-href")]
    public string Href
    {
        get => Url.Serialize();
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (RelevantDocument is not { } document)
                return;

            var url = EncodingParseUrl(value, document) ?? throw new DomException($"'{value}' is not a valid URL", "SyntaxError");
            Navigate(url);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-origin</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-origin"/>
    [IdlMember("origin", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-origin")]
    public string Origin => Url.SerializeOrigin();

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-protocol</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-protocol"/>
    [IdlMember("protocol", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-protocol")]
    public string Protocol
    {
        get => Url.Scheme + ":";
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (RelevantDocument is null)
                return;

            var copy = Url.Clone();
            if (!UrlParser.ParseWithStateOverride(value + ":", copy, UrlParserState.SchemeStart))
                throw new DomException($"'{value}' is not a valid scheme", "SyntaxError");

            if (copy.Scheme is not ("http" or "https"))
                return;

            Navigate(copy);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-host</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-host"/>
    [IdlMember("host", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-host")]
    public string Host
    {
        get => new Url(Url).Host;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (RelevantDocument is null)
                return;

            var copy = Url.Clone();
            if (copy.HasOpaquePath)
                return;

            UrlParser.ParseWithStateOverride(value, copy, UrlParserState.Host);
            Navigate(copy);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-hostname</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-hostname"/>
    [IdlMember("hostname", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-hostname")]
    public string Hostname
    {
        get => Url.Host?.Serialized ?? "";
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (RelevantDocument is null)
                return;

            var copy = Url.Clone();
            if (copy.HasOpaquePath)
                return;

            UrlParser.ParseWithStateOverride(value, copy, UrlParserState.Hostname);
            Navigate(copy);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-port</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-port"/>
    [IdlMember("port", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-port")]
    public string Port
    {
        get => new Url(Url).Port;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (RelevantDocument is null)
                return;

            var copy = Url.Clone();
            if (copy.CannotHaveUsernamePasswordPort)
                return;

            if (value.Length == 0)
            {
                copy.Port = -1;
            }
            else
            {
                UrlParser.ParseWithStateOverride(value, copy, UrlParserState.Port);
            }

            Navigate(copy);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-pathname</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-pathname"/>
    [IdlMember("pathname", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-pathname")]
    public string Pathname
    {
        get => Url.SerializePath();
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (RelevantDocument is null)
                return;

            var copy = Url.Clone();
            if (copy.HasOpaquePath)
                return;

            copy.Path.Clear();
            UrlParser.ParseWithStateOverride(value, copy, UrlParserState.PathStart);
            Navigate(copy);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-search</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-search"/>
    [IdlMember("search", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-search")]
    public string Search
    {
        get => string.IsNullOrEmpty(Url.Query) ? "" : "?" + Url.Query;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (RelevantDocument is null)
                return;

            var copy = Url.Clone();
            if (value.Length == 0)
            {
                copy.Query = null;
            }
            else
            {
                copy.Query = "";
                UrlParser.ParseWithStateOverride(value.StartsWith('?', StringComparison.Ordinal) ? value[1..] : value, copy, UrlParserState.Query);
            }

            Navigate(copy);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-hash</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-hash"/>
    [IdlMember("hash", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-hash")]
    public string Hash
    {
        get => string.IsNullOrEmpty(Url.Fragment) ? "" : "#" + Url.Fragment;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (RelevantDocument is null)
                return;

            var copy = Url.Clone();
            var thisUrlFragment = copy.Fragment ?? "";
            copy.Fragment = "";
            UrlParser.ParseWithStateOverride(value.StartsWith('#', StringComparison.Ordinal) ? value[1..] : value, copy, UrlParserState.Fragment);

            // Setting the same fragment does not navigate (e.g. no hashchange event), and the empty string is the fragment of
            // a URL without fragment
            if (copy.Fragment == thisUrlFragment)
                return;

            Navigate(copy);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-assign</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-assign"/>
    [IdlMember("assign", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-assign")]
    public void Assign(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (RelevantDocument is not { } document)
            return;

        var urlRecord = EncodingParseUrl(url, document) ?? throw new DomException($"'{url}' is not a valid URL", "SyntaxError");
        Navigate(urlRecord);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-replace</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-replace"/>
    [IdlMember("replace", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-replace")]
    public void Replace(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (RelevantDocument is not { } document)
            return;

        var urlRecord = EncodingParseUrl(url, document) ?? throw new DomException($"'{url}' is not a valid URL", "SyntaxError");
        Navigate(urlRecord, NavigationHistoryBehavior.Replace);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-reload</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-reload"/>
    [IdlMember("reload", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-reload")]
    public void Reload()
    {
        if (RelevantDocument is null)
            return;

        _window.BrowsingContext?.ReloadNavigable();
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-ancestororigins: the ancestor origins list of the relevant document.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-ancestororigins"/>
    [IdlMember("ancestorOrigins", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location-ancestororigins")]
    public DomStringList AncestorOrigins
    {
        get
        {
            // The relevant Document is null when the document is not fully active (e.g. its iframe was removed): the empty list
            if (!_window.Document.IsFullyActive || RelevantDocument is not { } document)
                return _emptyAncestorOrigins ??= new DomStringList([]);

            return document.AncestorOriginsList;
        }
    }

    /// <inheritdoc/>
    public override string ToString() => Href;

    /// <summary>
    /// Encoding-parses a URL relative to the entry settings object
    /// (https://html.spec.whatwg.org/multipage/urls-and-fetching.html#encoding-parsing-a-url): the document of the running
    /// script, e.g. the parent document that sets the location of an iframe, or the relevant document. The entry document
    /// is not tracked in the promise jobs: the document of the current realm is used.
    /// </summary>
    private static UrlRecord? EncodingParseUrl(string url, Document relevantDocument)
        => Html.Reflection.EncodingParseUrl(Document.EntryGlobalDocument ?? (relevantDocument.BrowsingContext?.Options.ScriptEngine?.GetCurrentGlobal() as Window)?.Document ?? relevantDocument, url);

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/nav-history-apis.html#location-object-navigate: the source document is the
    /// incumbent document, the document of the running script (e.g. an iframe that navigates a popup, whose about:blank
    /// document gets the base URL of the iframe), or the relevant document outside a script.
    /// </summary>
    private void Navigate(UrlRecord url, NavigationHistoryBehavior historyHandling = NavigationHistoryBehavior.Auto)
    {
        if (_window.BrowsingContext is not { } navigable)
            return;

        // The rate limit of the navigations (https://html.spec.whatwg.org/multipage/browsing-the-web.html#navigate)
        if (!navigable.IsAllowedToPerformNavigationOrHistoryUpdate())
            return;

        // The source document is the incumbent document: the document of the realm of the running script (the current
        // realm, as the setters and the methods of the bindings run in the realm of their caller)
        var sourceDocument = (navigable.Options.ScriptEngine?.GetCurrentGlobal() as Window)?.Document ?? Document.EntryGlobalDocument ?? navigable.Document;

        // A navigation before the document is completely loaded replaces its entry, unless the incumbent global object (the
        // window of the running script) has transient activation
        if (!navigable.Document.CompletelyLoaded && sourceDocument.RelevantWindow is not { HasTransientActivation: true })
        {
            historyHandling = NavigationHistoryBehavior.Replace;
        }
        navigable.Navigate(url, sourceDocument, historyHandling, exceptionsEnabled: true);
    }
}
