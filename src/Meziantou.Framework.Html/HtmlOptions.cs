
namespace Meziantou.Framework.Html;

#if HTML_PUBLIC
public
#else
internal
#endif
sealed class HtmlOptions
{
    private readonly Dictionary<string, HtmlElementReadOptions> _readOptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HtmlElementWriteOptions> _writeOptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _emptyNamespacesForXPath = new(StringComparer.Ordinal);
    private readonly HashSet<string> _emptyNamespaces = new(StringComparer.Ordinal);
    private readonly HashSet<string> _parsedScriptTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _impliedEndTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _impliedEndTagScopes = new(StringComparer.OrdinalIgnoreCase);

    public HtmlOptions()
    {
        ReaderThrowsOnEncodingMismatch = true;
        ReaderRestartsOnEncodingDetected = true;

        // check http://dev.w3.org/html5/html-author/#conforming-elements
        _readOptions["area"] = HtmlElementReadOptions.AutoClosed | HtmlElementReadOptions.NoChild;
        _readOptions["base"] = HtmlElementReadOptions.AutoClosed | HtmlElementReadOptions.NoChild;
        _readOptions["basefont"] = HtmlElementReadOptions.AutoClosed;
        _readOptions["bgsound"] = HtmlElementReadOptions.AutoClosed;
        _readOptions["br"] = HtmlElementReadOptions.AutoClosed | HtmlElementReadOptions.NoChild;
        _readOptions["col"] = HtmlElementReadOptions.AutoClosed | HtmlElementReadOptions.NoChild;
        _readOptions["command"] = HtmlElementReadOptions.AutoClosed | HtmlElementReadOptions.NoChild;
        _readOptions["embed"] = HtmlElementReadOptions.AutoClosed | HtmlElementReadOptions.NoChild;
        _readOptions["frame"] = HtmlElementReadOptions.AutoClosed;
        _readOptions["hr"] = HtmlElementReadOptions.AutoClosed | HtmlElementReadOptions.NoChild;
        _readOptions["img"] = HtmlElementReadOptions.AutoClosed | HtmlElementReadOptions.NoChild;
        _readOptions["input"] = HtmlElementReadOptions.AutoClosed | HtmlElementReadOptions.NoChild;
        _readOptions["isindex"] = HtmlElementReadOptions.AutoClosed;
        _readOptions["keygen"] = HtmlElementReadOptions.AutoClosed;
        _readOptions["link"] = HtmlElementReadOptions.AutoClosed | HtmlElementReadOptions.NoChild;
        _readOptions["meta"] = HtmlElementReadOptions.AutoClosed | HtmlElementReadOptions.NoChild;
        _readOptions["p"] = HtmlElementReadOptions.AutoClosed;
        _readOptions["param"] = HtmlElementReadOptions.AutoClosed | HtmlElementReadOptions.NoChild;
        _readOptions["script"] = HtmlElementReadOptions.InnerRaw;
        _readOptions["spacer"] = HtmlElementReadOptions.AutoClosed;
        _readOptions["source"] = HtmlElementReadOptions.AutoClosed | HtmlElementReadOptions.NoChild;
        _readOptions["style"] = HtmlElementReadOptions.InnerRaw;
        _readOptions["textarea"] = HtmlElementReadOptions.InnerRaw;
        _readOptions["title"] = HtmlElementReadOptions.InnerRaw;
        _readOptions["track"] = HtmlElementReadOptions.AutoClosed | HtmlElementReadOptions.NoChild;
        _readOptions["wbr"] = HtmlElementReadOptions.AutoClosed | HtmlElementReadOptions.NoChild;

        // NOTE: This "NOXHTML" element is not defined in specs and is specific to us
        // It may just be used by the caller if he wants to make sure what is inside will never be changed.
        _readOptions["noxhtml"] = HtmlElementReadOptions.InnerRaw;

        _writeOptions["a"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["abbr"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["address"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["area"] = HtmlElementWriteOptions.NoChild;
        _writeOptions["article"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["aside"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["audio"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["b"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["base"] = HtmlElementWriteOptions.NoChild;
        _writeOptions["bb"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["bdo"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["br"] = HtmlElementWriteOptions.NoChild;
        _writeOptions["blockquote"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["button"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["canvas"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["caption"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["cite"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["code"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["col"] = HtmlElementWriteOptions.NoChild;
        _writeOptions["command"] = HtmlElementWriteOptions.NoChild;
        _writeOptions["datagrid"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["datalist"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["del"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["details"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["dfn"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["dialog"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["div"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["dl"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["em"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["embed"] = HtmlElementWriteOptions.NoChild;
        _writeOptions["fieldset"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["figure"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["footer"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["form"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["h1"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["h2"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["h3"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["h4"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["h5"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["h6"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["header"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["hgroup"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["hr"] = HtmlElementWriteOptions.NoChild;
        _writeOptions["i"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["iframe"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["img"] = HtmlElementWriteOptions.NoChild;
        _writeOptions["input"] = HtmlElementWriteOptions.NoChild;
        _writeOptions["ins"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["kbd"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["label"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["legend"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["li"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["link"] = HtmlElementWriteOptions.NoChild;
        _writeOptions["map"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["mark"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["menu"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["meta"] = HtmlElementWriteOptions.DontCloseIfEmpty | HtmlElementWriteOptions.NoChild;
        _writeOptions["meter"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["nav"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["noscript"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["object"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["ol"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["output"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["param"] = HtmlElementWriteOptions.NoChild;
        _writeOptions["pre"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["progress"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["q"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["rp"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["rt"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["ruby"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["samp"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["script"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["section"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["select"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["small"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["source"] = HtmlElementWriteOptions.NoChild;
        _writeOptions["span"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["strong"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["style"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["sub"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["sup"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["table"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["textarea"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["time"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["title"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["ul"] = HtmlElementWriteOptions.AlwaysClose;
        _writeOptions["video"] = HtmlElementWriteOptions.AlwaysClose;

        // Elements that are implicitly closed when another element is opened.
        // check https://html.spec.whatwg.org/multipage/parsing.html#parsing-main-inbody
        string[] closesParagraph =
        [
            "address", "article", "aside", "blockquote", "center", "details", "dialog", "dir", "div", "dl",
            "fieldset", "figcaption", "figure", "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6", "header",
            "hgroup", "hr", "listing", "main", "menu", "nav", "ol", "p", "plaintext", "pre", "section", "summary",
            "table", "ul", "xmp",
        ];

        foreach (var name in closesParagraph)
        {
            SetImpliedEndTags(name, "p");
        }

        SetImpliedEndTags("li", "li", "p");
        SetImpliedEndTags("dd", "dd", "dt", "p");
        SetImpliedEndTags("dt", "dd", "dt", "p");
        SetImpliedEndTags("option", "option");
        SetImpliedEndTags("optgroup", "optgroup", "option");
        SetImpliedEndTags("rp", "rp", "rt");
        SetImpliedEndTags("rt", "rp", "rt");
        SetImpliedEndTags("td", "td", "th", "p");
        SetImpliedEndTags("th", "td", "th", "p");
        SetImpliedEndTags("tr", "tr", "td", "th", "p");

        string[] tableSections = ["caption", "colgroup", "tbody", "tfoot", "thead"];
        foreach (var name in tableSections)
        {
            SetImpliedEndTags(name, "caption", "colgroup", "tbody", "tfoot", "thead", "tr", "td", "th", "p");
        }

        // Elements that stop the search for an element to implicitly close.
        foreach (var name in new[]
        {
            "html", "head", "body", "template", "table", "caption", "colgroup", "tbody", "tfoot", "thead", "tr",
            "td", "th", "button", "object", "applet", "marquee", "select", "optgroup", "ol", "ul", "dl", "div",
            "form", "fieldset", "blockquote", "section", "article", "aside", "nav", "main", "header", "footer",
            "figure", "details", "dialog",
        })
        {
            _impliedEndTagScopes.Add(name);
        }

        // avoids using xhtml for all HTML xpath queries
        _emptyNamespacesForXPath.Add(HtmlNode.XhtmlNamespaceURI);
    }

    /// <summary>Gets the names of the open elements that are implicitly closed when <paramref name="name"/> is opened.</summary>
    /// <param name="name">The name of the element being opened.</param>
    /// <returns>The names of the elements to close, or an empty collection when <paramref name="name"/> closes nothing.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public IReadOnlyCollection<string> GetImpliedEndTags(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return _impliedEndTags.TryGetValue(name, out var names) ? names : [];
    }

    /// <summary>Sets the names of the open elements that are implicitly closed when <paramref name="name"/> is opened.</summary>
    /// <param name="name">The name of the element being opened.</param>
    /// <param name="closedElementNames">The names of the elements to close. Pass an empty array to close nothing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="closedElementNames"/> is <see langword="null"/>.</exception>
    public void SetImpliedEndTags(string name, params string[] closedElementNames)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(closedElementNames);

        _impliedEndTags[name] = new HashSet<string>(closedElementNames, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Gets the names of the elements that stop the search for an element to implicitly close.</summary>
    /// <remarks>An open element is never implicitly closed through one of these elements.</remarks>
    public ISet<string> ImpliedEndTagScopes => _impliedEndTagScopes;

    internal bool IsImpliedEndTag(string name, string openElementName)
    {
        return _impliedEndTags.TryGetValue(name, out var names) && names.Contains(openElementName);
    }

    public HtmlElementWriteOptions GetElementWriteOptions(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        _writeOptions.TryGetValue(name, out var options);
        return options;
    }

    public void SetElementWriteOptions(string name, HtmlElementWriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(name);

        _writeOptions[name] = options;
    }

    internal bool ParseScriptType(string? type)
    {
        if (type is null)
            return false;

        return ParsedScriptTypes.Contains(type);
    }

    public HtmlElementReadOptions GetElementReadOptions(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        _readOptions.TryGetValue(name, out var options);
        return options;
    }

    public void SetElementReadOptions(string name, HtmlElementReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(name);

        _readOptions[name] = options;
    }

    public ISet<string> ParsedScriptTypes => _parsedScriptTypes;

    public ISet<string> EmptyNamespaces => _emptyNamespaces;

    public ISet<string> EmptyNamespacesForXPath => _emptyNamespacesForXPath;

    public bool ReaderThrowsOnEncodingMismatch { get; set; }
    public bool ReaderRestartsOnEncodingDetected { get; set; }
    public bool ReaderThrowsOnUnknownDetectedEncoding { get; set; }
}
