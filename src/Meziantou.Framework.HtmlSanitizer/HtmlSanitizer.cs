using Meziantou.Framework.WebPlatform;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.Sanitizers;

/// <summary>Sanitizes HTML fragments to prevent XSS attacks by removing dangerous elements, attributes, and URLs while preserving safe HTML structure.</summary>
/// <example>
/// Basic HTML sanitization:
/// <code>
/// var sanitizer = new HtmlSanitizer();
/// var safeHtml = sanitizer.SanitizeHtmlFragment("&lt;p&gt;Hello &lt;script&gt;alert('xss')&lt;/script&gt;World&lt;/p&gt;");
/// // Result: "&lt;p&gt;Hello World&lt;/p&gt;"
/// </code>
/// </example>
public sealed class HtmlSanitizer
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

    /// <summary>Gets the set of HTML elements that are allowed in sanitized output. Elements not in this set are unwrapped: the tag is dropped but its content is kept, unless the element is in the BlockedElements set.</summary>
    public ISet<string> ValidElements { get; } = ToHashSet(DefaultValidElements);

    /// <summary>Gets the set of HTML attributes that are allowed in sanitized output. Attributes not in this set will be removed from elements.</summary>
    public ISet<string> ValidAttributes { get; } = ToHashSet(DefaultValidAttrs);

    /// <summary>Gets the set of prefixes of the HTML attributes that are allowed in sanitized output, in addition to the attributes of <see cref="ValidAttributes"/>. By default, it contains <c>aria-</c>.</summary>
    public ISet<string> ValidAttributePrefixes { get; } = ToHashSet(DescriptiveHtmlAttributes.Prefixes);

    /// <summary>Gets the set of HTML elements that will be completely removed from the output, including their content. By default includes script and style elements.</summary>
    public ISet<string> BlockedElements { get; } = ToHashSet(DefaultBlockedElements);

    /// <summary>Gets the set of attribute names that contain URLs and should be validated for safety. Unsafe URLs will be replaced with empty strings.</summary>
    public ISet<string> UriAttributes { get; } = ToHashSet(DefaultUriAttrs);

    /// <summary>Gets the set of attribute names that contain srcset values (responsive image sources) and should be validated for safety.</summary>
    public ISet<string> SrcsetAttributes { get; } = ToHashSet(DefaultSrcsetAttrs);

    /// <summary>Gets or sets a value indicating whether HTML comments are kept in the sanitized output. Comments are removed by default because downlevel-hidden conditional comments (<c>&lt;!--[if IE]&gt;&lt;script&gt;…&lt;/script&gt;&lt;![endif]--&gt;</c>) are executed by legacy browsers.</summary>
    public bool AllowComments { get; set; }

    private static HashSet<string> ToHashSet(string[] values) => new(values, StringComparer.OrdinalIgnoreCase);

    // Only the elements of the HTML namespace are kept: the SVG and MathML elements are parsed with other rules (foreign
    // content), so they would not be read back as the same elements once moved or serialized.
    private bool IsValidElement(Element element) => element.NamespaceUri == HtmlNamespace && ValidElements.Contains(element.LocalName);

    private bool IsValidAttribute(string attributeName)
    {
        if (ValidAttributes.Contains(attributeName))
            return true;

        foreach (var prefix in ValidAttributePrefixes)
        {
            if (attributeName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Sanitizes an HTML fragment by removing dangerous elements, attributes, and URLs while preserving safe HTML structure.</summary>
    /// <param name="html">The HTML fragment to sanitize.</param>
    /// <returns>A sanitized HTML fragment safe for rendering.</returns>
    [return: NotNullIfNotNull(nameof(html))]
    public string? SanitizeHtmlFragment(string? html)
    {
        if (html is null)
            return null;

        // The fragment is parsed as the content of a body element, the way a browser parses the markup inserted in a page
        var document = HtmlDocumentParser.ParseDocument("");
        var body = document.Body!;
        var fragment = new HtmlDocumentParser().ParseFragment(html, body);
        Sanitize(fragment);
        body.Append(fragment);
        return body.GetHtml();
    }

    // The traversal is iterative on purpose: a hostile fragment can nest elements thousands of levels deep
    // and a recursive walk would overflow the stack.
    private void Sanitize(Node root)
    {
        var pendingNodes = new Stack<(Node Parent, bool IsInLink)>();
        pendingNodes.Push((root, false));
        List<Element>? leadingNewLineElements = null;

        while (pendingNodes.TryPop(out var item))
        {
            var (parent, isInLink) = item;
            var node = parent.FirstChild;
            while (node is not null)
            {
                var next = node.NextSibling;
                switch (node)
                {
                    // Blocked elements, and elements whose content is raw text rather than markup, are removed with
                    // their content. The content of a raw text element (script, style, iframe, noscript, …) is
                    // serialized verbatim, so neither keeping it nor promoting it to the parent can be made safe.
                    // This is checked before ValidElements on purpose: adding such an element to the allow list
                    // would otherwise write its content straight through without ever sanitizing it.
                    case Element element when BlockedElements.Contains(element.LocalName) || HasRawTextContent(element):
                        element.Remove();
                        break;

                    // The element is not allowed but its content is, so only the tag is dropped. The promoted children
                    // take the place of the element, so they are sanitized next.
                    // Unwrapping an element can move its content where the parser never puts it, which a browser
                    // would read back differently: the parser allows a link in a link, or a heading in a heading, only
                    // when an element such as template or object separates them. Such an element is unwrapped too.
                    case Element element when !IsValidElement(element) || (isInLink && IsLink(element)) || (IsHeading(element) && parent is Element parentElement && IsHeading(parentElement)):
                        next = Unwrap(element) ?? next;
                        break;

                    case Element element:
                        if (SanitizeAttributes(element))
                        {
                            element = RecreateWithoutIsValue(element);
                        }

                        if (IgnoresLeadingNewLine(element))
                        {
                            (leadingNewLineElements ??= []).Add(element);
                        }

                        // The content of a template element is not made of its children but of a separate fragment
                        Node content = element is HtmlTemplateElement template ? template.Content : element;
                        pendingNodes.Push((content, IsLink(element) || (isInLink && !IsFormattingScope(element))));
                        break;

                    case Text text when text.Data.Contains('\r', StringComparison.Ordinal):
                        text.Data = NormalizeNewLines(text.Data);
                        break;

                    // The serializer escapes the text, so it can never be read back as markup
                    case Text:
                        break;

                    case Comment when AllowComments:
                        break;

                    default:
                        parent.RemoveChild(node);
                        break;
                }

                node = next;
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

    private static bool IgnoresLeadingNewLine(Element element) => element.NamespaceUri == HtmlNamespace && element.LocalName is "pre" or "listing";

    private static bool IsLink(Element element) => element.NamespaceUri == HtmlNamespace && element.LocalName == "a";

    private static bool IsHeading(Element element) => element.NamespaceUri == HtmlNamespace && element.LocalName is "h1" or "h2" or "h3" or "h4" or "h5" or "h6";

    // The elements that insert a marker in the list of active formatting elements, so a link they contain does not
    // close a link that contains them (https://html.spec.whatwg.org/multipage/parsing.html#concept-parser-marker)
    private static bool IsFormattingScope(Element element) => element.NamespaceUri == HtmlNamespace && element.LocalName is "applet" or "caption" or "marquee" or "object" or "td" or "template" or "th";

    private static Node? Unwrap(Element element)
    {
        Node content = element is HtmlTemplateElement template ? template.Content : element;
        var parent = element.ParentNode!;
        var firstChild = content.FirstChild;
        while (content.FirstChild is { } child)
        {
            parent.InsertBefore(child, element);
        }

        element.Remove();
        return firstChild;
    }

    // Returns whether the "is" attribute was removed
    private bool SanitizeAttributes(Element element)
    {
        var removedIsAttribute = false;
        var attributes = element.Attributes;
        for (var i = attributes.Count - 1; i >= 0; i--)
        {
            // The values are decoded by the parser and encoded again by the serializer, so they are validated as the
            // browser reads them: an entity cannot mask the scheme of a URL
            var attribute = attributes[i];
            if (!IsValidAttribute(attribute.Name))
            {
                removedIsAttribute |= attribute.NamespaceUri is null && attribute.LocalName == "is";
                element.RemoveAttributeNode(attribute);
                continue;
            }

            if (attribute.Value.Contains('\r', StringComparison.Ordinal))
            {
                attribute.Value = NormalizeNewLines(attribute.Value);
            }

            if (UriAttributes.Contains(attribute.Name))
            {
                if (!UrlSanitizer.IsSafeUrl(attribute.Value))
                {
                    attribute.Value = "";
                }
            }
            else if (SrcsetAttributes.Contains(attribute.Name))
            {
                if (!UrlSanitizer.IsSafeSrcset(attribute.Value))
                {
                    attribute.Value = "";
                }
            }
        }

        return removedIsAttribute;
    }

    // The parser gives an element the "is" value of its start tag (customized built-in elements), and the serializer
    // writes that value as an "is" attribute when the element has none, so removing the attribute is not enough. The
    // element is replaced with one created without an "is" value, which gets its attributes and its content.
    private static Element RecreateWithoutIsValue(Element element)
    {
        var replacement = element.OwnerDocument!.CreateElementNS(element.NamespaceUri, element.LocalName);
        var attributes = element.Attributes;
        while (attributes.Count > 0)
        {
            // Moving the nodes rather than copying the values: the parser accepts attribute names that SetAttribute rejects
            var attribute = attributes[0];
            element.RemoveAttributeNode(attribute);
            replacement.SetAttributeNode(attribute);
        }

        Node content = element is HtmlTemplateElement template ? template.Content : element;
        Node replacementContent = replacement is HtmlTemplateElement replacementTemplate ? replacementTemplate.Content : replacement;
        while (content.FirstChild is { } child)
        {
            replacementContent.AppendChild(child);
        }

        element.ParentNode!.ReplaceChild(replacement, element);
        return replacement;
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
