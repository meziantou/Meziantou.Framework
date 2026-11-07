using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Html;

/// <summary>Prerendering (https://wicg.github.io/nav-speculation/prerendering.html): the prerender records of the speculation rules.</summary>
internal static class Prerendering
{
    /// <summary>Whether a document of the navigable may prerender: a prerendering document does not (its speculation rules are prefetched).</summary>
    public static bool CanActivateIn(BrowsingContext navigable) => !navigable.Top.IsPrerendering;

    /// <summary>Whether the prerendering is for a new top-level traversable (<c>"target_hint": "_blank"</c>).</summary>
    public static bool IsForNewWindow(PrefetchRecord record) => record.TargetHint is { } targetHint && targetHint.Equals("_blank", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// https://wicg.github.io/nav-speculation/prerendering.html#start-referrer-initiated-prerendering: creates a prerendering
    /// traversable and navigates it to the URL of the record, with the document as the initiator. Only the same-site URLs
    /// are prerendered (a cross-origin response must opt in, see <c>Supports-Loading-Mode</c>).
    /// </summary>
    public static void Start(Document document, PrefetchRecord record)
    {
        if (document.BrowsingContext is not { Parent: null } navigable)
            return;

        var url = record.Url;
        if (!OriginRecord.IsSameSite(document.Origin, OriginRecord.FromUrl(url)))
            return;

        if (url.Fragment is not null && BrowsingContext.UrlEquals(url, document.UrlRecord, excludeFragments: true))
            return;

        document.AddPrefetchRecord(record);
        record.StartTime = navigable.EventLoop.TimeProvider.GetTimestamp();
        var prerendering = navigable.CreatePrerenderingBrowsingContext(document, record);
        record.PrerenderingNavigable = prerendering;
        prerendering.Navigate(url, document, NavigationHistoryBehavior.Replace, referrerPolicy: record.ReferrerPolicy);
    }

    /// <summary>Whether the <c>Supports-Loading-Mode</c> header of a response (a structured field list of tokens) has a loading mode.</summary>
    public static bool HasLoadingMode(IReadOnlyList<KeyValuePair<string, string>> headers, string loadingMode)
        => Infra.StructuredFields.GetList(headers, "Supports-Loading-Mode") is { } loadingModes
            && loadingModes.Exists(member => member.Item?.Value is Infra.StructuredFieldToken token && token.Value == loadingMode);

    /// <summary>Destroys a prerendering traversable whose record was discarded.</summary>
    public static void Cancel(BrowsingContext prerenderingNavigable)
    {
        if (!prerenderingNavigable.IsDestroyed)
        {
            prerenderingNavigable.Destroy();
        }
    }
}
