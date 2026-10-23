namespace ChromiumDiff;

/// <summary>A context element of the HTML fragment parsing algorithm (the element whose innerHTML is set).</summary>
/// <param name="Namespace">The namespace of the element.</param>
/// <param name="Name">The local name of the element.</param>
/// <param name="Attributes">The attributes of the element (e.g. the encoding of annotation-xml, which makes it an HTML integration point).</param>
/// <param name="Quirks">Whether the node document of the element is in quirks mode (a document created by DOMParser, without scripting).</param>
internal sealed record FragmentContext(string Namespace, string Name, (string Name, string Value)[] Attributes, bool Quirks)
{
    private const string Html = "http://www.w3.org/1999/xhtml";
    private const string Svg = "http://www.w3.org/2000/svg";
    private const string MathML = "http://www.w3.org/1998/Math/MathML";

    /// <summary>
    /// The context elements: every element that changes the state of the tokenizer or the insertion mode reset by the
    /// fragment parsing algorithm, the elements of the special and formatting categories, the void elements, and the
    /// foreign elements (the integration points and the others).
    /// </summary>
    public static IReadOnlyList<FragmentContext> All { get; } =
    [
        .. new[]
        {
            "html", "head", "body", "frameset", "frame", "template", "div", "p", "span", "a", "b", "font", "nobr", "button",
            "li", "ul", "ol", "dl", "dd", "dt", "form", "label", "fieldset", "h1", "pre", "listing", "address", "center",
            "search", "details", "summary", "dialog", "menu", "table", "caption", "colgroup", "col", "tbody", "thead", "tfoot",
            "tr", "td", "th", "select", "optgroup", "option", "selectedcontent", "datalist", "hr", "input", "keygen", "title",
            "textarea", "style", "xmp", "iframe", "noembed", "noframes", "script", "noscript", "plaintext", "ruby", "rb", "rt",
            "rtc", "rp", "object", "applet", "marquee", "img", "image", "br", "embed", "math", "svg", "custom-element",
        }.Select(name => new FragmentContext(Html, name, [], Quirks: false)),
        .. new[] { "svg", "foreignObject", "foreignobject", "desc", "title", "path", "g", "text", "script", "style", "font", "a" }
            .Select(name => new FragmentContext(Svg, name, [], Quirks: false)),
        .. new[] { "math", "mi", "mo", "mn", "ms", "mtext", "annotation-xml", "mglyph", "malignmark", "mrow", "semantics" }
            .Select(name => new FragmentContext(MathML, name, [], Quirks: false)),
        new(MathML, "annotation-xml", [("encoding", "text/html")], Quirks: false),
        new(MathML, "annotation-xml", [("encoding", "application/xhtml+xml")], Quirks: false),
        new(Html, "body", [], Quirks: true),
        new(Html, "p", [], Quirks: true),
        new(Html, "table", [], Quirks: true),
    ];

    /// <summary>Gets the name of the context in the identifiers of the comparisons (e.g. "td", "svg path", "math annotation-xml encoding=text/html", "body quirks").</summary>
    public string DisplayName
    {
        get
        {
            var name = Namespace switch
            {
                Svg => "svg " + Name,
                MathML => "math " + Name,
                _ => Name,
            };

            foreach (var (attributeName, value) in Attributes)
            {
                name += " " + attributeName + "=" + value;
            }

            return Quirks ? name + " quirks" : name;
        }
    }
}
