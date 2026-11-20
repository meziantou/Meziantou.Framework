using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom.FileApi;

/// <summary>
/// A blob URL entry (https://w3c.github.io/FileAPI/#blob-url-entry): the blob and the document that created the URL, and the
/// storage key of its environment (the partition of the URL, null for an opaque origin): the key of the environment, or the
/// first-party key of its origin for a URL created by a <see cref="StorageAccessHandle"/>.
/// </summary>
internal sealed record BlobUrlEntry(Blob Object, Document Environment, StorageKey? StorageKey);

/// <summary>
/// The blob URL store (https://w3c.github.io/FileAPI/#BlobURLStore). The documents of a browsing context group (a top-level
/// <see cref="BrowsingContext"/> and its nested browsing contexts) and their workers share a store; a document without
/// browsing context has its own store. The entries of a document or of a worker are removed when the document is destroyed
/// or the worker ends (https://w3c.github.io/FileAPI/#lifeTime). The store is used by the threads of the workers. The URLs are
/// partitioned by storage key (https://w3c.github.io/FileAPI/#check-for-same-partition-blob-url-usage): an environment of
/// another partition (e.g. a document of the origin embedded by another top-level site) cannot fetch nor revoke them.
/// </summary>
internal sealed class BlobUrlStore
{
    private static readonly ConditionalWeakTable<Document, BlobUrlStore> DocumentStores = [];

    private readonly Dictionary<string, BlobUrlEntry> _entries = new(StringComparer.Ordinal);

    // The URLs of the media sources (https://w3c.github.io/media-source/#mediasource-object-url): they are not blob URL
    // entries for the fetches (fetching them is a network error); a media element resolves them to attach the source
    private readonly Dictionary<string, (MediaSource Source, Document Environment)> _mediaSources = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    /// <summary>Gets the store used by a document: the one of its browsing context group (the one of the creator of a worker for the document of a worker).</summary>
    public static BlobUrlStore Get(Document document)
    {
        return document.EnvironmentBrowsingContext?.Top.BlobUrls ?? DocumentStores.GetValue(document.EnvironmentRootDocument, static _ => new BlobUrlStore());
    }

    /// <summary>https://w3c.github.io/FileAPI/#add-an-entry</summary>
    public string Add(Blob blob, Document environment) => Add(blob, environment, StorageKey.Obtain(environment));

    /// <summary>Adds an entry whose partition is <paramref name="storageKey"/> (the first-party key of a <see cref="StorageAccessHandle"/>).</summary>
    public string Add(Blob blob, Document environment, StorageKey? storageKey)
    {
        // https://w3c.github.io/FileAPI/#unicodeBlobURL
        var origin = environment.SerializeOrigin();
        var url = "blob:" + origin + "/" + Guid.NewGuid().ToString("D");
        lock (_lock)
        {
            _entries[url] = new BlobUrlEntry(blob, environment, storageKey);
        }

        return url;
    }

    /// <summary>
    /// https://w3c.github.io/FileAPI/#check-for-same-partition-blob-url-usage: whether the storage key of an environment is
    /// the one of the entry. A document whose storage access was granted (https://privacycg.github.io/saa-non-cookie-storage/),
    /// same site with its top-level document but embedded by a cross-site document, also uses the URLs of the first-party
    /// contexts of its origin, as in Chromium: the storage access clears the cross-site ancestor bit of its key.
    /// </summary>
    public static bool IsSamePartition(BlobUrlEntry entry, Document environment)
    {
        var storageKey = StorageKey.Obtain(environment);
        if (entry.StorageKey == storageKey)
            return true;

        return (environment.EnvironmentHasStorageAccess || environment.StorageAccessGranted) && storageKey is { HasCrossSiteAncestor: true } partitionedKey && entry.StorageKey == partitionedKey.ToFirstParty();
    }

    /// <summary>Adds an entry for a media source (https://w3c.github.io/media-source/#mediasource-object-url).</summary>
    public string Add(MediaSource source, Document environment)
    {
        var url = "blob:" + environment.SerializeOrigin() + "/" + Guid.NewGuid().ToString("D");
        lock (_lock)
        {
            _mediaSources[url] = (source, environment);
        }

        return url;
    }

    /// <summary>Returns the media source a <c>blob:</c> URL references, or null.</summary>
    public MediaSource? ResolveMediaSource(UrlRecord url)
    {
        if (url.Scheme != "blob")
            return null;

        lock (_lock)
        {
            return _mediaSources.TryGetValue(url.Serialize(excludeFragment: true), out var entry) ? entry.Source : null;
        }
    }

    /// <summary>https://w3c.github.io/FileAPI/#dfn-revokeObjectURL: removes the entry of a URL of the origin and of the partition of the environment.</summary>
    public void Remove(UrlRecord url, Document environment) => Remove(url, environment, StorageKey.Obtain(environment));

    /// <summary>Removes the entry of a URL of the origin of the environment whose partition is <paramref name="storageKey"/> (https://w3c.github.io/FileAPI/#removeTheEntry).</summary>
    public void Remove(UrlRecord url, Document environment, StorageKey? storageKey)
    {
        // The key is the URL with its fragment: revoking "blob:...#fragment" does nothing
        var key = url.Serialize();
        lock (_lock)
        {
            if (_mediaSources.TryGetValue(key, out var mediaSource))
            {
                if (Document.IsSameOrigin(mediaSource.Environment, environment))
                {
                    _mediaSources.Remove(key);
                }

                return;
            }

            if (!_entries.TryGetValue(key, out var entry))
                return;

            // A document can only revoke the URLs of its origin and of its partition
            if (!Document.IsSameOrigin(entry.Environment, environment) || entry.StorageKey != storageKey)
                return;

            _entries.Remove(key);
        }
    }

    /// <summary>https://w3c.github.io/FileAPI/#blob-url-resolve</summary>
    public BlobUrlEntry? Resolve(UrlRecord url)
    {
        if (url.Scheme != "blob")
            return null;

        lock (_lock)
        {
            return _entries.GetValueOrDefault(url.Serialize(excludeFragment: true));
        }
    }

    /// <summary>Removes the entries created by a document (when it is destroyed).</summary>
    public void RemoveEntries(Document environment)
    {
        lock (_lock)
        {
            foreach (var (key, entry) in _mediaSources.ToList())
            {
                if (ReferenceEquals(entry.Environment, environment))
                {
                    _mediaSources.Remove(key);
                }
            }

            if (_entries.Count == 0)
                return;

            foreach (var (key, entry) in _entries.ToList())
            {
                if (ReferenceEquals(entry.Environment, environment))
                {
                    _entries.Remove(key);
                }
            }
        }
    }
}
