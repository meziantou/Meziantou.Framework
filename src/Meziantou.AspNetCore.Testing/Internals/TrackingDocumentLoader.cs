using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Internals;

/// <summary>
/// Counts the requests in flight of each browsing context, apart from the long-lived requests (server-sent events, long
/// polling...), which never end while the page is open and so must not prevent the page from being idle.
/// </summary>
internal sealed class TrackingDocumentLoader(DocumentLoader inner, Func<DocumentRequest, bool>? isLongLived) : DocumentLoader
{
    private readonly ConditionalWeakTable<BrowsingContext, RequestCounters> _counters = [];

    public RequestCounters GetCounters(BrowsingContext context) => _counters.GetOrCreateValue(context);

    public override async ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
    {
        var counters = request.BrowsingContext is { } context ? GetCounters(context) : null;
        var longLived = isLongLived?.Invoke(request) ?? false;
        counters?.Increment(longLived);
        try
        {
            return await inner.LoadAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            counters?.Decrement(longLived);
        }
    }

    public override ValueTask<DocumentResource?> ConnectWebSocketAsync(DocumentRequest request, CancellationToken cancellationToken) => inner.ConnectWebSocketAsync(request, cancellationToken);

    internal sealed class RequestCounters
    {
        private int _requests;
        private int _longLivedRequests;

        public int Requests => Volatile.Read(ref _requests);

        public int LongLivedRequests => Volatile.Read(ref _longLivedRequests);

        public void Increment(bool longLived) => Interlocked.Increment(ref longLived ? ref _longLivedRequests : ref _requests);

        public void Decrement(bool longLived) => Interlocked.Decrement(ref longLived ? ref _longLivedRequests : ref _requests);
    }
}
