using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Svg;

/// <summary>
/// The URL references of the SVG elements (https://svgwg.org/svg2-draft/linking.html#processingURL): the <c>href</c> attribute
/// (and the deprecated <c>xlink:href</c> attribute) of the elements that reference another element (<c>use</c>, the paint server
/// templates, <c>textPath</c>, <c>mpath</c>, <c>filter</c>).
/// </summary>
internal static class SvgLinking
{
    /// <summary>
    /// Returns the URL reference of an element: the animated value of its <c>href</c> attribute, else its <c>xlink:href</c>
    /// attribute (https://svgwg.org/svg2-draft/linking.html#XLinkRefAttrs), without its leading and trailing ASCII white space
    /// (which the URL parser strips), or null.
    /// </summary>
    public static string? GetHref(Element element)
        => (element.GetAnimatedAttributeValue("href") ?? element.GetAttributeValue(Namespaces.XLink, "href"))?.Trim(' ', '\t', '\n', '\f', '\r');

    /// <summary>
    /// Returns the fragment of a URL reference to an element of the document of <paramref name="element"/> (a same-document
    /// reference: a fragment-only URL, or a URL that only differs from the URL of the document by its fragment), percent-decoded
    /// (https://html.spec.whatwg.org/multipage/browsing-the-web.html#find-a-potential-indicated-element); false for a reference
    /// without fragment or to another document.
    /// </summary>
    public static bool TryGetSameDocumentFragment(Element element, string? href, out string id)
    {
        id = "";
        if (href is null)
            return false;

        var hash = href.IndexOf('#', StringComparison.Ordinal);
        if (hash < 0 || hash == href.Length - 1)
            return false;

        if (hash > 0)
        {
            var document = element.NodeDocument;
            if (Urls.UrlParser.Parse(href, document.DocumentBaseUrl) is not { } url || url.Serialize(excludeFragment: true) != document.UrlRecord.Serialize(excludeFragment: true))
                return false;
        }

        id = href[(hash + 1)..];
        if (id.Contains('%', StringComparison.Ordinal))
        {
            id = System.Text.Encoding.UTF8.GetString(Urls.PercentEncoding.PercentDecode(id));
        }

        return true;
    }

    /// <summary>
    /// Whether a URL reference of an element is a same-document reference: a URL that only differs from the URL of the
    /// document of the element by its fragment (e.g. a fragment-only URL, or an empty URL).
    /// </summary>
    public static bool IsSameDocumentReference(Element element, string? href)
    {
        if (href is null || href.Length == 0 || href[0] == '#')
            return true;

        var document = element.NodeDocument;
        return Urls.UrlParser.Parse(href, document.DocumentBaseUrl) is not { } url || url.Serialize(excludeFragment: true) == document.UrlRecord.Serialize(excludeFragment: true);
    }

    /// <summary>
    /// Returns the element of the tree of an element (its document, or its shadow tree or document fragment) referenced by its
    /// <c>href</c> attribute, or null (no reference, a reference to another document, no element with this id).
    /// </summary>
    public static Element? GetReferencedElement(Element element)
    {
        if (!TryGetSameDocumentFragment(element, GetHref(element), out var id))
            return null;

        return element.GetRoot() switch
        {
            Document document => document.GetElementById(id),
            DocumentFragment fragment => fragment.GetElementById(id),
            _ => null,
        };
    }
}
