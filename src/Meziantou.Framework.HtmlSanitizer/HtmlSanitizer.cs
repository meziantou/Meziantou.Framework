using System.Diagnostics;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

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
    private static readonly string[] OptionalEndTagElements = [.. OptionalEndTagInlineElements, .. OptionalEndTagBlockElements];

    // Safe Block Elements - HTML5
    private static readonly string[] BlockElements = [.. OptionalEndTagBlockElements, "address", "article", "aside", "blockquote", "caption", "center", "del", "dir", "div", "dl", "figure", "figcaption", "footer", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hgroup", "hr", "ins", "map", "menu", "nav", "ol", "pre", "section", "table", "ul"];

    // Inline Elements - HTML5
    private static readonly string[] InlineElements = [.. OptionalEndTagInlineElements, "a", "abbr", "acronym", "b", "bdi", "bdo", "big", "br", "cite", "code", "del", "dfn", "em", "font", "i", "img", "ins", "kbd", "label", "map", "mark", "q", "ruby", "rp", "rt", "s", "samp", "small", "span", "strike", "strong", "sub", "sup", "time", "tt", "u", "var"];

    // Blocked Elements (will be stripped)
    private static readonly string[] DefaulBlockedElements = ["script", "style"];

    private static readonly string[] DefaulValidElements = [.. VoidElements, .. BlockElements, .. InlineElements, .. OptionalEndTagElements];

    //Attributes that have href and hence need to be sanitized
    private static readonly string[] DefaulUriAttrs = ["background", "cite", "href", "longdesc", "src", "xlink:href"];
    private static readonly string[] DefaulSrcsetAttrs = ["srcset"];
    private static readonly string[] DefaultHtmlAttrs = ["abbr", "align", "alt", "axis", "bgcolor", "border", "cellpadding", "cellspacing", "class", "clear", "color", "cols", "colspan", "compact", "coords", "dir", "face", "headers", "height", "hreflang", "hspace", "ismap", "lang", "language", "nohref", "nowrap", "rel", "rev", "rows", "rowspan", "rules", "scope", "scrolling", "shape", "size", "span", "start", "summary", "tabindex", "target", "title", "type", "valign", "value", "vspace", "width"];

    private static readonly string[] DefaulValidAttrs = [.. DefaulUriAttrs, .. DefaulSrcsetAttrs, .. DefaultHtmlAttrs];

    /// <summary>Gets the set of HTML elements that are allowed in sanitized output. Elements not in this set will be removed unless they are in the BlockedElements set.</summary>
    public ISet<string> ValidElements { get; } = ToHashSet(DefaulValidElements);

    /// <summary>Gets the set of HTML attributes that are allowed in sanitized output. Attributes not in this set will be removed from elements.</summary>
    public ISet<string> ValidAttributes { get; } = ToHashSet(DefaulValidAttrs);

    /// <summary>Gets the set of HTML elements that will be completely removed from the output, including their content. By default includes script and style elements.</summary>
    public ISet<string> BlockedElements { get; } = ToHashSet(DefaulBlockedElements);

    /// <summary>Gets the set of attribute names that contain URLs and should be validated for safety. Unsafe URLs will be replaced with empty strings.</summary>
    public ISet<string> UriAttributes { get; } = ToHashSet(DefaulUriAttrs);

    /// <summary>Gets the set of attribute names that contain srcset values (responsive image sources) and should be validated for safety.</summary>
    public ISet<string> SrcsetAttributes { get; } = ToHashSet(DefaulSrcsetAttrs);

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
    public string SanitizeHtmlFragment(string html)
    {
        var element = ParseHtmlFragment(html);
        for (var i = element.ChildNodes.Length - 1; i >= 0; i--)
        {
            Sanitize(element.ChildNodes[i]);
        }

        return element.InnerHtml;
    }

    private void Sanitize(INode node)
    {
        if (node is IElement htmlElement)
        {
            if (!IsValidNode(htmlElement.TagName))
            {
                htmlElement.Remove();
                return;
            }

            for (var i = htmlElement.Attributes.Length - 1; i >= 0; i--)
            {
                var attribute = htmlElement.Attributes[i];
                if (attribute is null)
                    continue;

                if (!IsValidAttribute(attribute.Name))
                {
                    htmlElement.RemoveAttribute(attribute.NamespaceUri, attribute.Name);
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

        for (var i = node.ChildNodes.Length - 1; i >= 0; i--)
        {
            Sanitize(node.ChildNodes[i]);
        }
    }

    private static IElement ParseHtmlFragment(string content)
    {
        var uniqueId = Guid.NewGuid().ToString("N");

        var parser = new HtmlParser();
        var document = parser.ParseDocument($"<div id='{uniqueId}'>{content}</div>");
        var element = document.GetElementById(uniqueId);
        Debug.Assert(element is not null);
        return element;
    }
}
