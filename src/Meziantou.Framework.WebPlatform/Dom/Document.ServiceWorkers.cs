namespace Meziantou.Framework.WebPlatform.Dom;

public partial class Document
{
    private ServiceWorkerRecord? _activeServiceWorker;
    private string? _environmentId;

    /// <summary>
    /// The active service worker of the environment the document represents (https://html.spec.whatwg.org/multipage/webappapis.html#concept-environment-active-service-worker):
    /// the service worker that controls the document (or the worker), or null. It is read by the threads of the other
    /// environments and of the service workers.
    /// </summary>
    internal ServiceWorkerRecord? ActiveServiceWorker
    {
        get => Volatile.Read(ref _activeServiceWorker);
        set => Volatile.Write(ref _activeServiceWorker, value);
    }

    /// <summary>The id of the environment the document represents (https://html.spec.whatwg.org/multipage/webappapis.html#concept-environment-id).</summary>
    internal string EnvironmentId
    {
        get => Volatile.Read(ref _environmentId) ?? Interlocked.CompareExchange(ref _environmentId, Guid.NewGuid().ToString("D"), null) ?? _environmentId;
        set => _environmentId = value;
    }

    /// <summary>The entry of the environment in the service worker clients of its browsing context group, or null.</summary>
    internal ServiceWorkerClientEntry? ServiceWorkerClient { get; set; }
}
