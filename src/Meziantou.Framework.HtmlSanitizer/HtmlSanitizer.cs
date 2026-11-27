using System.Globalization;
using Meziantou.Framework.WebPlatform;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.Sanitizers;

/// <summary>Sanitizes HTML fragments to prevent XSS attacks by removing dangerous elements, attributes, and URLs while preserving safe HTML structure.</summary>
/// <example>
/// Basic HTML sanitization:
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.HtmlSanitizer.Tests/HtmlSanitizerTests.cs" ref:region="HtmlSanitizer_SanitizeHtmlFragment" ref:dedent="true">
/// var sanitizer = new HtmlSanitizer();
/// var safeHtml = sanitizer.SanitizeHtmlFragment("&lt;p&gt;Hello &lt;script&gt;alert('xss')&lt;/script&gt;World&lt;/p&gt;");
/// Assert.Equal("&lt;p&gt;Hello World&lt;/p&gt;", safeHtml);
/// </code>
/// </example>
public sealed partial class HtmlSanitizer
{
    // Inspiration: https://github.com/angular/angular/blob/4d36b2f6e9a1a7673b3f233752895c96ca7dba1e/packages/core/src/sanitization/html_sanitizer.ts
    // https://wicg.github.io/sanitizer-api/#default-configuration-dictionary

    // Safe Void Elements - HTML5
    // http://dev.w3.org/html5/spec/Overview.html#void-elements
    private static readonly string[] VoidElements = ["area", "br", "col", "hr", "img", "wbr"];

    // Elements that you can, intentionally, leave open (and which close themselves)
    // http://dev.w3.org/html5/spec/Overview.html#optional-tags
    private static readonly string[] OptionalEndTagBlockElements = ["colgroup", "dd", "dt", "li", "p", "tbody", "td", "tfoot", "th", "thead", "tr"];
    private static readonly string[] OptionalEndTagInlineElements = ["rp", "rt"];

    // Safe Block Elements - HTML5
    private static readonly string[] BlockElements = [.. OptionalEndTagBlockElements, "address", "article", "aside", "blockquote", "caption", "center", "del", "dir", "div", "dl", "figure", "figcaption", "footer", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hgroup", "hr", "ins", "map", "menu", "nav", "ol", "pre", "section", "table", "ul"];

    // Inline Elements - HTML5
    // del, ins and map are in BlockElements as well: their content model is transparent, so they are either
    // depending on what they wrap. The lists are merged into a set, so the overlap costs nothing.
    private static readonly string[] InlineElements = [.. OptionalEndTagInlineElements, "a", "abbr", "acronym", "b", "bdi", "bdo", "big", "br", "cite", "code", "del", "dfn", "em", "font", "i", "img", "ins", "kbd", "label", "map", "mark", "q", "ruby", "rp", "rt", "s", "samp", "small", "span", "strike", "strong", "sub", "sup", "time", "tt", "u", "var"];

    private const string HtmlNamespace = "http://www.w3.org/1999/xhtml";

    // Elements whose content is text rather than markup
    private static readonly HashSet<string> RawTextElements = new(["iframe", "noembed", "noframes", "noscript", "plaintext", "script", "style", "textarea", "title", "xmp"], StringComparer.Ordinal);

    // Blocked Elements (will be stripped)
    private static readonly string[] DefaultBlockedElements = ["script", "style"];

    private static readonly string[] DefaultValidElements = [.. VoidElements, .. BlockElements, .. InlineElements];

    //Attributes that have href and hence need to be sanitized
    private static readonly string[] DefaultUriAttrs = ["background", "cite", "href", "longdesc", "src", "xlink:href"];
    private static readonly string[] DefaultSrcsetAttrs = ["srcset"];
    private static readonly string[] DefaultHtmlAttrs = ["class", .. DescriptiveHtmlAttributes.Names];

    private static readonly string[] DefaultValidAttrs = [.. DefaultUriAttrs, .. DefaultSrcsetAttrs, .. DefaultHtmlAttrs];

    // The sets are used through their concrete type: no interface dispatch, and no boxed enumerator for the prefixes
    private readonly HashSet<string> _validElements = ToHashSet(DefaultValidElements);
    private readonly HashSet<string> _validAttributes = ToHashSet(DefaultValidAttrs);
    private readonly HashSet<string> _validAttributePrefixes = ToHashSet(DescriptiveHtmlAttributes.Prefixes);
    private readonly HashSet<string> _blockedElements = ToHashSet(DefaultBlockedElements);
    private readonly HashSet<string> _uriAttributes = ToHashSet(DefaultUriAttrs);
    private readonly HashSet<string> _srcsetAttributes = ToHashSet(DefaultSrcsetAttrs);

    /// <summary>Gets the set of HTML elements that are allowed in sanitized output. Elements not in this set are unwrapped: the tag is dropped but its content is kept, unless the element is in the BlockedElements set.</summary>
    public ISet<string> ValidElements => _validElements;

    /// <summary>Gets the set of HTML attributes that are allowed in sanitized output. Attributes not in this set will be removed from elements.</summary>
    public ISet<string> ValidAttributes => _validAttributes;

    /// <summary>Gets the set of prefixes of the HTML attributes that are allowed in sanitized output, in addition to the attributes of <see cref="ValidAttributes"/>. By default, it contains <c>aria-</c>.</summary>
    public ISet<string> ValidAttributePrefixes => _validAttributePrefixes;

    /// <summary>Gets the set of HTML elements that will be completely removed from the output, including their content. By default includes script and style elements.</summary>
    public ISet<string> BlockedElements => _blockedElements;

    /// <summary>Gets the set of attribute names that contain URLs and should be validated for safety. Unsafe URLs will be replaced with empty strings.</summary>
    public ISet<string> UriAttributes => _uriAttributes;

    /// <summary>Gets the set of attribute names that contain srcset values (responsive image sources) and should be validated for safety.</summary>
    public ISet<string> SrcsetAttributes => _srcsetAttributes;

    /// <summary>Gets or sets a value indicating whether HTML comments are kept in the sanitized output. Comments are removed by default because downlevel-hidden conditional comments (<c>&lt;!--[if IE]&gt;&lt;script&gt;…&lt;/script&gt;&lt;![endif]--&gt;</c>) are executed by legacy browsers.</summary>
    public bool AllowComments { get; set; }

    /// <summary>
    /// Gets or sets the maximum length of the fragment, in UTF-16 code units. <see cref="SanitizeHtmlFragment"/> throws an
    /// <see cref="ArgumentException"/> for a longer fragment. The default is 1,000,000. Use <see cref="int.MaxValue"/> to
    /// remove the limit.
    /// </summary>
    public int MaxInputLength
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = 1_000_000;

    /// <summary>
    /// Gets or sets the maximum number of elements the parser creates for the fragment. <see cref="SanitizeHtmlFragment"/>
    /// throws an <see cref="ArgumentException"/> for a fragment that creates more elements. The number of elements is not
    /// bounded by the length of the fragment: the parser recreates the open formatting elements (<c>b</c>, <c>i</c>,
    /// <c>a</c>, …) in each new paragraph, so a few kilobytes can create millions of elements. The default is 100,000.
    /// Use <see cref="int.MaxValue"/> to remove the limit.
    /// </summary>
    public int MaxElementCount
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = 100_000;

    private static HashSet<string> ToHashSet(string[] values) => new(values, StringComparer.OrdinalIgnoreCase);

    // Only the elements of the HTML namespace are kept: the SVG and MathML elements are parsed with other rules (foreign
    // content), so they would not be read back as the same elements once moved or serialized.
    private bool IsValidElement(Element element) => element.NamespaceUri == HtmlNamespace && _validElements.Contains(element.LocalName);

    private bool IsValidAttribute(string attributeName)
    {
        if (_validAttributes.Contains(attributeName))
            return true;

        foreach (var prefix in _validAttributePrefixes)
        {
            if (attributeName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Sanitizes an HTML fragment by removing dangerous elements, attributes, and URLs while preserving safe HTML structure.</summary>
    /// <param name="html">The HTML fragment to sanitize.</param>
    /// <returns>A sanitized HTML fragment safe for rendering.</returns>
    /// <exception cref="ArgumentException">The fragment is longer than <see cref="MaxInputLength"/>, or creates more elements than <see cref="MaxElementCount"/>.</exception>
    [return: NotNullIfNotNull(nameof(html))]
    public string? SanitizeHtmlFragment(string? html)
    {
        if (html is null)
            return null;

        if (html.Length > MaxInputLength)
            throw new ArgumentException($"The fragment is longer than {MaxInputLength.ToString(CultureInfo.InvariantCulture)} characters (see {nameof(MaxInputLength)})", nameof(html));

        // The fragment is parsed as the content of a body element, the way a browser parses the markup inserted in a page
        // that is not in quirks mode (where a table start tag does not close a p element). The mode of a document created
        // by createHTMLDocument is no-quirks. The body element is not inserted in the document: it is only the context of
        // the parser and the parent of the sanitized nodes, which are serialized the same way, and inserting the nodes in a
        // document runs the steps of the connected elements for nothing.
        var document = new Document().Implementation.CreateHtmlDocument();
        var body = document.CreateElement("body");
        DocumentFragment fragment;
        try
        {
            fragment = new HtmlDocumentParser(new HtmlParserOptions { MaxElementCount = MaxElementCount }).ParseFragment(html, body);
        }
        catch (HtmlParserLimitExceededException ex)
        {
            throw new ArgumentException($"The fragment creates more than {MaxElementCount.ToString(CultureInfo.InvariantCulture)} elements (see {nameof(MaxElementCount)})", nameof(html), ex);
        }

        // The kept nodes are copied into another fragment rather than unwrapping the other elements in place: moving a node
        // walks its whole subtree, so nested elements that are not allowed would take a time quadratic in the depth, while
        // the copies are inserted before their children, which inserts only leaf nodes.
        var output = document.CreateDocumentFragment();
        Sanitize(fragment, output);
        body.Append(output);
        return body.GetHtml();
    }

    // The traversal is iterative on purpose: a hostile fragment can nest elements thousands of levels deep
    // and a recursive walk would overflow the stack.
    // The source nodes are visited in tree order, and each kept node is appended to the copy of its nearest kept ancestor
    // (or to the output fragment), so the copies are in the same order as the source nodes. The pending items are the next
    // source node to visit, the node its copy is appended to, and the open elements of the copies of its ancestors.
    private void Sanitize(Node source, Node output)
    {
        var document = output.OwnerDocument!;
        var pendingNodes = new Stack<(Node Node, Node Parent, OpenElements State)>();
        if (source.FirstChild is { } firstNode)
        {
            pendingNodes.Push((firstNode, output, OpenElements.None));
        }

        // The parser gives an element the "is" value of its start tag (customized built-in elements), and the serializer
        // writes that value as an "is" attribute when the element has none, so removing the attribute is not enough: the
        // copy of the element is created without an "is" value when the attribute is not allowed. The parser only sets
        // the value of an element that has the attribute, so a copy keeps the value only when it keeps the attribute.
        var keepIsValue = IsValidAttribute("is");
        List<Element>? leadingNewLineElements = null;
        while (pendingNodes.TryPop(out var item))
        {
            var (node, parent, state) = item;

            // The next sibling is visited after the content of the node, which is pushed after it
            if (node.NextSibling is { } nextSibling)
            {
                pendingNodes.Push((nextSibling, parent, state));
            }

            switch (node)
            {
                // Blocked elements, and elements whose content is raw text rather than markup, are removed with
                // their content. The content of a raw text element (script, style, iframe, noscript, …) is
                // serialized verbatim, so neither keeping it nor promoting it to the parent can be made safe.
                // This is checked before ValidElements on purpose: adding such an element to the allow list
                // would otherwise write its content straight through without ever sanitizing it.
                case Element element when _blockedElements.Contains(element.LocalName) || HasRawTextContent(element):
                    break;

                // The element is not allowed but its content is, so only the tag is dropped: its content is
                // copied in the place of the element, so it is sanitized as the content of the parent.
                // Unwrapping an element can move its content where the parser never puts it, which a browser
                // would read back differently (a link in a link, a button in a button, a div in a p, ...), so such
                // an element is unwrapped too (see IsMisnested).
                case Element element when !IsValidElement(element) || IsMisnested(element, parent, state):
                    if (GetContent(element).FirstChild is { } firstChild)
                    {
                        pendingNodes.Push((firstChild, parent, state));
                    }

                    break;

                case Element element:
                    var copy = CopyElement(element, keepIsValue);
                    parent.AppendChild(copy);
                    if (IgnoresLeadingNewLine(copy))
                    {
                        (leadingNewLineElements ??= []).Add(copy);
                    }

                    if (GetContent(element).FirstChild is { } firstElementChild)
                    {
                        pendingNodes.Push((firstElementChild, GetContent(copy), GetChildrenState(element, state)));
                    }

                    break;

                case Text text when text.Data.Contains('\r', StringComparison.Ordinal):
                    parent.AppendChild(document.CreateTextNode(NormalizeNewLines(text.Data)));
                    break;

                // The serializer escapes the text, so it can never be read back as markup
                case Text text:
                    parent.AppendChild(document.CreateTextNode(text.Data));
                    break;

                case Comment comment when AllowComments:
                    parent.AppendChild(document.CreateComment(comment.Data));
                    break;
            }
        }

        // The parser ignores a line feed that directly follows the start tag of a pre or listing element, and the
        // serializer does not write one, so a text that starts with a line feed (written "<pre>&#10;&#10;", or the
        // content of an unwrapped element) would lose it when it is read back. Doubling it keeps the text as it is.
        if (leadingNewLineElements is not null)
        {
            foreach (var element in leadingNewLineElements)
            {
                if (element.FirstChild is Text text && text.Data.StartsWith('\n', StringComparison.Ordinal))
                {
                    text.Data = "\n" + text.Data;
                }
            }
        }
    }

    // The content of a template element is not made of its children but of a separate fragment
    private static Node GetContent(Element element) => element is HtmlTemplateElement template ? template.Content : element;

    private static bool IgnoresLeadingNewLine(Element element) => element.NamespaceUri == HtmlNamespace && element.LocalName is "pre" or "listing";

    // The elements that insert a marker in the list of active formatting elements, so a link they contain does not
    // close a link that contains them (https://html.spec.whatwg.org/multipage/parsing.html#concept-parser-marker)
    private static bool IsFormattingScope(Element element) => element.NamespaceUri == HtmlNamespace && element.LocalName is "applet" or "caption" or "marquee" or "object" or "td" or "template" or "th";

    // The copy gets the attributes that are allowed, in one pass: removing the other ones one by one, or reading the
    // attributes by name, would be quadratic in the number of attributes. The attributes are read by index: the attribute
    // nodes (Element.Attributes) are not created, and the parser accepts attribute names that SetAttribute rejects.
    private Element CopyElement(Element element, bool keepIsValue)
    {
        var copy = element.CloneWithoutAttributes(keepIsValue);
        for (var i = 0; i < element.AttributeCount; i++)
        {
            var name = element.GetAttributeQualifiedName(i);
            if (!IsValidAttribute(name))
                continue;

            // The values are decoded by the parser and encoded again by the serializer, so they are validated as the
            // browser reads them: an entity cannot mask the scheme of a URL
            var value = element.GetAttributeValue(i);
            if (value.Contains('\r', StringComparison.Ordinal))
            {
                value = NormalizeNewLines(value);
            }

            // An attribute can be in both sets: a value that is a safe URL can still be an unsafe srcset value
            // ("https://a 1x, javascript:alert(1) 2x"), so it must pass both validations
            if ((_uriAttributes.Contains(name) && !UrlSanitizer.IsSafeUrl(value)) ||
                (_srcsetAttributes.Contains(name) && !UrlSanitizer.IsSafeSrcset(value)))
            {
                value = "";
            }

            copy.AppendAttribute(element.GetAttributeLocalName(i), element.GetAttributeNamespace(i), element.GetAttributePrefix(i), value);
        }

        return copy;
    }

    // A character reference such as "&#13;" decodes to a carriage return, which the serializer writes as is. A browser
    // reads it back as a line feed (https://html.spec.whatwg.org/multipage/parsing.html#preprocessing-the-input-stream),
    // so it is written as a line feed, and the output has no carriage return left for the browser to change.
    private static string NormalizeNewLines(string value) => value.Replace('\r', '\n');

    // https://html.spec.whatwg.org/multipage/parsing.html#serialising-html-fragments lists the elements whose text is
    // serialized verbatim. noscript is one of them in a browser, where scripting is enabled. title and textarea are
    // escaped by the serializer, but their content is text rather than markup, so it cannot be promoted either.
    private static bool HasRawTextContent(Element element)
    {
        return element.NamespaceUri == HtmlNamespace && RawTextElements.Contains(element.LocalName);
    }
}
