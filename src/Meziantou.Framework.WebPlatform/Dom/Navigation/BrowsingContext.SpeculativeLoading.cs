using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

public sealed partial class BrowsingContext
{
    /// <summary>
    /// https://wicg.github.io/nav-speculation/prefetch.html#wait-for-a-matching-prefetch-record, for the GET navigations of a
    /// top-level navigable: when a navigational prefetch of the source document matches the URL, its response is processed
    /// as the response of the navigation; when one that is expected to match is ongoing, the navigation waits for it, and
    /// fetches the URL (<paramref name="startNavigationFetch"/>) if the prefetch fails. Returns false when the navigation
    /// fetches the URL itself.
    /// </summary>
    private bool TryUsePrefetchRecord(int navigationId, UrlRecord url, Document? sourceDocument, PostResource? postResource, DocumentParameters parameters, Action startNavigationFetch, CancellationToken cancellationToken)
    {
        if (postResource is not null || Parent is not null || sourceDocument is null)
            return false;

        return NavigationalPrefetch.WaitForMatchingRecord(this, sourceDocument, url, record =>
        {
            if (_ongoingNavigation != navigationId || IsDestroyed)
                return;

            if (record is null)
            {
                startNavigationFetch();
                return;
            }

            NavigationalPrefetch.Activate(record, this, parameters.Timing);
            var request = record.Request!;
            parameters.ReservedClient = request.ReservedClient;
            if (record.Redirects.Count == 0)
            {
                parameters.PrefetchNavigationUrl = url;
            }

            QueueTask(() => ProcessNavigationResponse(navigationId, url, request, parameters, record.Response!, cancellationToken));
        });
    }
}
