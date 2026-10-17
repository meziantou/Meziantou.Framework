namespace Meziantou.Framework.WebPlatform.Dom;

// https://html.spec.whatwg.org/multipage/parsing.html#speculative-html-parsing (see Html.Parsing.SpeculativeHtmlParser)
public partial class Document
{
    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#list-of-speculative-fetch-urls</summary>
    internal HashSet<string> SpeculativeFetchUrls => _speculativeFetchUrls ??= new(StringComparer.Ordinal);

    private HashSet<string>? _speculativeFetchUrls;

    // The speculative fetches of the document that a service worker is handling (see ServiceWorkerFetchHandling.HandleFetchAsync)
    private Dictionary<ServiceWorkerFetchHandling.SpeculativeFetchKey, Task<FetchResponse?>>? _speculativeServiceWorkerFetches;

    /// <summary>Registers the fetch event of a speculative fetch of the document, until the service worker has handled it.</summary>
    internal void AddSpeculativeServiceWorkerFetch(ServiceWorkerFetchHandling.SpeculativeFetchKey key, Task<FetchResponse?> dispatch)
    {
        var fetches = LazyInitializer.EnsureInitialized(ref _speculativeServiceWorkerFetches);
        lock (fetches)
        {
            fetches[key] = dispatch;
        }

        _ = dispatch.ContinueWith(
            completed =>
            {
                lock (fetches)
                {
                    if (fetches.TryGetValue(key, out var current) && ReferenceEquals(current, completed))
                    {
                        fetches.Remove(key);
                    }
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Gets the fetch event of a speculative fetch of the document that the service worker is still handling, for the
    /// request of the parser that follows it (the event is used once).
    /// </summary>
    internal bool TryTakeSpeculativeServiceWorkerFetch(ServiceWorkerFetchHandling.SpeculativeFetchKey key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Task<FetchResponse?>? dispatch)
    {
        dispatch = null;
        if (_speculativeServiceWorkerFetches is not { } fetches)
            return false;

        lock (fetches)
        {
            return fetches.Remove(key, out dispatch);
        }
    }

    /// <summary>Whether the <c>Document-Policy</c> header of the response enables <c>expect-no-linked-resources</c>: the document is not parsed speculatively.</summary>
    internal bool ExpectsNoLinkedResources { get; set; }
}
