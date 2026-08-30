using Meziantou.Framework.Html;

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

    // Blocked Elements (will be stripped)
    private static readonly string[] DefaultBlockedElements = ["script", "style"];

    private static readonly string[] DefaultValidElements = [.. VoidElements, .. BlockElements, .. InlineElements];

    //Attributes that have href and hence need to be sanitized
    private static readonly string[] DefaultUriAttrs = ["background", "cite", "href", "longdesc", "src", "xlink:href"];
    private static readonly string[] DefaultSrcsetAttrs = ["srcset"];
    private static readonly string[] DefaultHtmlAttrs = ["abbr", "align", "alt", "axis", "bgcolor", "border", "cellpadding", "cellspacing", "class", "clear", "color", "cols", "colspan", "compact", "coords", "dir", "face", "headers", "height", "hreflang", "hspace", "ismap", "lang", "language", "nohref", "nowrap", "rel", "rev", "rows", "rowspan", "rules", "scope", "scrolling", "shape", "size", "span", "start", "summary", "tabindex", "target", "title", "type", "valign", "value", "vspace", "width"];

    private static readonly string[] DefaultValidAttrs = [.. DefaultUriAttrs, .. DefaultSrcsetAttrs, .. DefaultHtmlAttrs];

    /// <summary>Gets the set of HTML elements that are allowed in sanitized output. Elements not in this set are unwrapped: the tag is dropped but its content is kept, unless the element is in the BlockedElements set.</summary>
    public ISet<string> ValidElements { get; } = ToHashSet(DefaultValidElements);

    /// <summary>Gets the set of HTML attributes that are allowed in sanitized output. Attributes not in this set will be removed from elements.</summary>
    public ISet<string> ValidAttributes { get; } = ToHashSet(DefaultValidAttrs);

    /// <summary>Gets the set of HTML elements that will be completely removed from the output, including their content. By default includes script and style elements.</summary>
    public ISet<string> BlockedElements { get; } = ToHashSet(DefaultBlockedElements);

    /// <summary>Gets the set of attribute names that contain URLs and should be validated for safety. Unsafe URLs will be replaced with empty strings.</summary>
    public ISet<string> UriAttributes { get; } = ToHashSet(DefaultUriAttrs);

    /// <summary>Gets the set of attribute names that contain srcset values (responsive image sources) and should be validated for safety.</summary>
    public ISet<string> SrcsetAttributes { get; } = ToHashSet(DefaultSrcsetAttrs);

    /// <summary>Gets or sets a value indicating whether HTML comments are kept in the sanitized output. Comments are removed by default because downlevel-hidden conditional comments (<c>&lt;!--[if IE]&gt;&lt;script&gt;…&lt;/script&gt;&lt;![endif]--&gt;</c>) are executed by legacy browsers.</summary>
    public bool AllowComments { get; set; }

    private static HashSet<string> ToHashSet(string[] values) => new(values, StringComparer.OrdinalIgnoreCase);

    private bool IsValidNode(string tagName)
    {
        if (BlockedElements.Contains(tagName))
            return false;

        if (!ValidElements.Contains(tagName))
            return false;

        return true;
    }

    private bool IsValidAttribute(string attributeName)
    {
        if (!ValidAttributes.Contains(attributeName))
            return false;

        return true;
    }

    /// <summary>Sanitizes an HTML fragment by removing dangerous elements, attributes, and URLs while preserving safe HTML structure.</summary>
    /// <param name="html">The HTML fragment to sanitize.</param>
    /// <returns>A sanitized HTML fragment safe for rendering.</returns>
    [return: NotNullIfNotNull(nameof(html))]
    public string? SanitizeHtmlFragment(string? html)
    {
        if (html is null)
            return null;

        var document = ParseHtmlFragment(html);
        Sanitize(document);
        return document.InnerHtml;
    }

    // The traversal is iterative on purpose: a hostile fragment can nest elements thousands of levels deep
    // and a recursive walk would overflow the stack.
    private void Sanitize(HtmlNode root)
    {
        var pendingNodes = new Stack<HtmlNode>();
        pendingNodes.Push(root);

        while (pendingNodes.Count > 0)
        {
            var parent = pendingNodes.Pop();
            for (var i = parent.ChildNodes.Count - 1; i >= 0; i--)
            {
                switch (parent.ChildNodes[i])
                {
                    case HtmlElement element when !IsValidNode(element.Name):
                    {
                        // Blocked elements, and elements whose content is raw text rather than markup, are removed
                        // with their content. The content of a raw text element (script, style, title, textarea, …)
                        // is written back verbatim, so promoting it to the parent would re-inject markup.
                        if (BlockedElements.Contains(element.Name) || HasRawTextContent(element))
                        {
                            element.Remove();
                            break;
                        }

                        // The element is not allowed but its content is, so only the tag is dropped.
                        var promotedCount = element.ChildNodes.Count;
                        element.Remove(keepChildren: true);

                        // The promoted children take the place of the element, so they must be sanitized too.
                        i += promotedCount;
                        break;
                    }

                    case HtmlElement element:
                        SanitizeAttributes(element);
                        pendingNodes.Push(element);
                        break;

                    case HtmlComment comment when !AllowComments:
                        comment.Remove();
                        break;

                    case HtmlText { IsCData: true } text:
                        // "<![CDATA[…]]>" is not a CDATA section in an HTML document: it is a bogus comment that ends
                        // at the first ">". Writing the section back as-is would let everything after that ">" escape
                        // the comment and be parsed as markup, so the content is turned into escaped text instead.
                        text.IsCData = false;
                        text.Value = EscapeText(text.Value);
                        break;
                }
            }
        }
    }

    private void SanitizeAttributes(HtmlElement element)
    {
        for (var i = element.Attributes.Count - 1; i >= 0; i--)
        {
            var attribute = element.Attributes[i];
            if (!IsValidAttribute(attribute.Name))
            {
                // Removing by index instead of by name: HtmlNode.RemoveAttribute expects a local name, so a
                // namespace-qualified attribute such as "xxx:onclick" would never be found and would survive.
                element.Attributes.RemoveAt(i);
            }
            else if (UriAttributes.Contains(attribute.Name))
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
    }

    private static bool HasRawTextContent(HtmlElement element)
    {
        var options = element.OwnerDocument?.Options.GetElementReadOptions(element.Name) ?? HtmlElementReadOptions.None;
        return (options & HtmlElementReadOptions.InnerRaw) == HtmlElementReadOptions.InnerRaw;
    }

    private static string EscapeText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        return text
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    private static HtmlDocument ParseHtmlFragment(string content)
    {
        var document = new HtmlDocument();
        document.LoadHtml(content);
        return document;
    }
}
