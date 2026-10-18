namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The fetch group of a document (https://fetch.spec.whatwg.org/#concept-fetch-group): the ongoing fetches of
/// <c>fetch()</c>, <see cref="XmlHttpRequest"/> and <c>navigator.sendBeacon()</c> whose client is the document. They are
/// aborted when the document is aborted (<c>window.stop()</c>, <c>document.open()</c> during a navigation,
/// https://html.spec.whatwg.org/multipage/document-lifecycle.html#abort-a-document) or destroyed, except the keepalive
/// requests, which outlive their document.
/// </summary>
internal sealed class FetchGroup
{
    /// <summary>The quota of the bodies of the inflight keepalive requests (https://fetch.spec.whatwg.org/#http-network-or-cache-fetch, step 8.10.5).</summary>
    public const int KeepaliveQuota = 64 * 1024;

    private readonly List<FetchRecord> _records = [];

    /// <summary>https://fetch.spec.whatwg.org/#fetch-group-deferred-fetch-records: the pending requests of <c>fetchLater()</c>.</summary>
    public List<DeferredFetchRecord> DeferredFetchRecords { get; } = [];

    /// <summary>
    /// Adds an ongoing fetch: <paramref name="abort"/> aborts it, unless <paramref name="request"/> is a keepalive request.
    /// Dispose the record when the fetch is done (https://fetch.spec.whatwg.org/#done-flag).
    /// </summary>
    public IDisposable Add(Action? abort, FetchRequest? request = null)
    {
        var record = new FetchRecord(this, abort, request);
        _records.Add(record);
        return record;
    }

    /// <summary>
    /// The total length of the bodies of the keepalive requests that are not done, except <paramref name="except"/>
    /// (https://fetch.spec.whatwg.org/#http-network-or-cache-fetch, step 8.10).
    /// </summary>
    public long GetInflightKeepaliveBytes(FetchRequest? except = null)
    {
        long result = 0;
        foreach (var record in _records)
        {
            if (record.Request is { Keepalive: true } request && request != except)
            {
                result += request.Body?.Length ?? 0;
            }
        }

        return result;
    }

    /// <summary>Aborts the ongoing fetches that are not keepalive requests (https://fetch.spec.whatwg.org/#concept-fetch-group-terminate).</summary>
    public void Abort()
    {
        if (_records.Count == 0)
            return;

        var records = _records.FindAll(record => record.Request is not { Keepalive: true });
        _records.RemoveAll(record => record.Request is not { Keepalive: true });
        foreach (var record in records)
        {
            record.Abort();
        }
    }

    private sealed class FetchRecord(FetchGroup group, Action? abort, FetchRequest? request) : IDisposable
    {
        public FetchRequest? Request => request;

        public void Abort() => abort?.Invoke();

        public void Dispose() => group._records.Remove(this);
    }
}
