using System.Runtime.CompilerServices;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The storage of the storage endpoints (https://storage.spec.whatwg.org/#registered-storage-endpoints) of a global
/// object whose storage is given by a backend (IndexedDB, the Cache API): the backend of the browsing context, and the
/// origin of the storage key.
/// </summary>
internal static class StorageEndpoints
{
    // The storage of the documents without browsing context, and of the credentialless iframes of a page, by backend type
    private static readonly ConditionalWeakTable<Document, Dictionary<Type, object>> DocumentBackends = [];
    private static readonly ConditionalWeakTable<Window, Dictionary<Type, object>> PartitionedBackends = [];

    /// <summary>
    /// Returns the backend and the origin of the storage of a global object (https://storage.spec.whatwg.org/#obtain-a-storage-key),
    /// or null (failure) when the origin is opaque. The credentialless iframes of a page (and their workers) have their own
    /// in-memory storage.
    /// </summary>
    /// <param name="global">The window or the worker.</param>
    /// <param name="configured">The backend of the options of the global object, or null.</param>
    /// <param name="fromBrowsingContext">The backend of a browsing context (the backend of its group).</param>
    /// <param name="createInMemory">Creates an in-memory backend.</param>
    public static (TBackend Backend, string Origin)? Resolve<TBackend>(GlobalScope global, TBackend? configured, Func<BrowsingContext, TBackend> fromBrowsingContext, Func<TBackend> createInMemory)
        where TBackend : class
    {
        var document = global.EnvironmentDocument;
        var origin = document.Origin;
        if (origin.IsOpaque)
            return null;

        var windowDocument = document.EnvironmentRootDocument;
        var browsingContext = document.EnvironmentBrowsingContext;
        TBackend backend;
        if (document.IsCredentialless && browsingContext?.Top.Window is { } topLevelWindow)
        {
            backend = GetOrCreate(PartitionedBackends.GetValue(topLevelWindow, static _ => []), createInMemory);
        }
        else
        {
            backend = configured ?? (browsingContext is not null ? fromBrowsingContext(browsingContext) : GetOrCreate(DocumentBackends.GetValue(windowDocument, static _ => []), createInMemory));
        }

        return (backend, origin.Serialize());
    }

    private static TBackend GetOrCreate<TBackend>(Dictionary<Type, object> backends, Func<TBackend> createInMemory)
        where TBackend : class
    {
        lock (backends)
        {
            if (!backends.TryGetValue(typeof(TBackend), out var backend))
            {
                backend = createInMemory();
                backends.Add(typeof(TBackend), backend);
            }

            return (TBackend)backend;
        }
    }
}
