using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

// The visited links (https://drafts.csswg.org/selectors-4/#link): the history is the store of the options of the browsing
// context (BrowsingContextOptions.VisitedLinks), which the navigations of the browsing contexts feed. Its entries are the
// triples of the partitioned history of https://drafts.csswg.org/selectors-4/#visited-partitioning: the URL of the link, the
// origin of the document that contains the link and the site of its top-level document. A document without browsing
// context, or whose origin is opaque, has no visited link.
public partial class Document
{
    /// <summary>The history of the visited links of the document, or null: no link is visited.</summary>
    internal VisitedLinkStore? VisitedLinks => BrowsingContext?.Options.VisitedLinks is { } store && !Origin.IsOpaque ? store : null;

    /// <summary>
    /// Returns whether a link of the document (an element that matches <c>:any-link</c>) was visited: the URL of its
    /// <c>href</c> attribute, parsed relative to the document, is in the history for the origin of the document and the site of
    /// its top-level document.
    /// </summary>
    internal bool IsVisitedLink(Element link)
    {
        if (VisitedLinks is not { } store)
            return false;

        var href = link is SvgAElement ? Svg.SvgLinking.GetHref(link) : link.GetAttributeValue("href");
        if (href is null || Html.Reflection.EncodingParseUrl(this, href) is not { } url)
            return false;

        return store.IsVisited(new VisitedLink(url.Serialize(), Origin.Serialize(), TopLevelOrigin.SerializeSite()));
    }

    /// <summary>
    /// Records a navigation to a URL of the document in the history of the visited links, for the document itself ("add an
    /// entry to visited history recording the destination's URL, the destination's origin, and the destination's site. Do this
    /// only for navigations from top-level frames or iframes which are same-origin with their top-level frame"), and for the
    /// document that started the navigation ("the navigation's destination URL, the origin of the page containing the link or
    /// script, and the (schemeful) site of the top-level site containing that page").
    /// </summary>
    /// <param name="url">The URL of the navigation.</param>
    /// <param name="initiatorOrigin">The origin of the document that started the navigation, or null.</param>
    /// <param name="initiatorTopLevelOrigin">The origin of the top-level document of the document that started the navigation, or null.</param>
    internal void RecordVisitedLink(UrlRecord url, OriginRecord? initiatorOrigin = null, OriginRecord? initiatorTopLevelOrigin = null)
    {
        if (BrowsingContext?.Options.VisitedLinks is not { } store || url.Scheme is "about" or "blob" or "data" or "javascript")
            return;

        var serialized = url.Serialize();
        if (!Origin.IsOpaque && OriginRecord.IsSameOrigin(Origin, TopLevelOrigin))
        {
            store.Add(new VisitedLink(serialized, Origin.Serialize(), TopLevelOrigin.SerializeSite()));
        }

        if (initiatorOrigin is { IsOpaque: false } && initiatorTopLevelOrigin is { IsOpaque: false })
        {
            store.Add(new VisitedLink(serialized, initiatorOrigin.Serialize(), initiatorTopLevelOrigin.SerializeSite()));
        }
    }
}
