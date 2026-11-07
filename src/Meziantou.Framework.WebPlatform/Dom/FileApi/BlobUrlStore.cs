using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom.FileApi;

/// <summary>A blob URL entry (https://w3c.github.io/FileAPI/#blob-url-entry): the blob and the document that created the URL.</summary>
internal sealed record BlobUrlEntry(Blob Object, Document Environment);

/// <summary>
/// The blob URL store (https://w3c.github.io/FileAPI/#BlobURLStore). The documents of a browsing context group (a top-level
/// <see cref="BrowsingContext"/> and its nested browsing contexts) and their workers share a store; a document without
/// browsing context has its own store. The entries of a document or of a worker are removed when the document is destroyed
/// or the worker ends (https://w3c.github.io/FileAPI/#lifeTime). The store is used by the threads of the workers.
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
    public string Add(Blob blob, Document environment)
    {
        // https://w3c.github.io/FileAPI/#unicodeBlobURL
        var origin = environment.SerializeOrigin();
        var url = "blob:" + origin + "/" + Guid.NewGuid().ToString("D");
        lock (_lock)
        {
            _entries[url] = new BlobUrlEntry(blob, environment);
        }

        return url;
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

    /// <summary>https://w3c.github.io/FileAPI/#removeTheEntry</summary>
    public void Remove(UrlRecord url, Document environment)
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

            // A document can only revoke the URLs of its origin
            if (!Document.IsSameOrigin(entry.Environment, environment))
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
