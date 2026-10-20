namespace Meziantou.Framework.WebPlatform.Dom;

public sealed partial class Window
{
    /// <summary>
    /// Gets the local storage area of the origin of the document
    /// (https://html.spec.whatwg.org/multipage/webstorage.html#dom-localstorage), from the storage shed of the browsing
    /// context (<see cref="BrowsingContextOptions.LocalStorage"/>). Throws a <c>SecurityError</c> when the origin of the
    /// document is opaque, or when the <see cref="WebStoragePersistence"/> of the shed cannot load the items.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/webstorage.html#dom-localstorage"/>
    [IdlMember("localStorage", "https://html.spec.whatwg.org/multipage/webstorage.html#dom-localstorage")]
    public Storage LocalStorage
    {
        get
        {
            var document = Document;
            if (document.LocalStorageHolder is { } holder)
                return holder;

            // https://storage.spec.whatwg.org/#obtain-a-local-storage-bottle-map
            if (document.BrowsingContext is not { } browsingContext || GetStorageKey(document) is not { } storageKey)
                throw new DomException("The document is not allowed to access the local storage", "SecurityError");

            StorageArea area;
            try
            {
                area = browsingContext.LocalStorageShed.ObtainArea(storageKey);
            }
            catch (Exception ex) when (ex is not DomException)
            {
                // The persistence cannot load the items: the storage bottle map is failure
                throw new DomException("The local storage cannot be loaded", "SecurityError");
            }

            var storage = new Storage(this, area, isSession: false);
            document.LocalStorageHolder = storage;
            return storage;
        }
    }

    /// <summary>
    /// Gets the session storage area of the origin of the document in its top-level browsing context
    /// (https://html.spec.whatwg.org/multipage/webstorage.html#dom-sessionstorage). Throws a <c>SecurityError</c> when the
    /// origin of the document is opaque.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/webstorage.html#dom-sessionstorage"/>
    [IdlMember("sessionStorage", "https://html.spec.whatwg.org/multipage/webstorage.html#dom-sessionstorage")]
    public Storage SessionStorage
    {
        get
        {
            var document = Document;
            if (document.SessionStorageHolder is { } holder)
                return holder;

            // https://storage.spec.whatwg.org/#obtain-a-session-storage-bottle-map: the storage shed of the top-level traversable
            if (document.BrowsingContext is not { } browsingContext || GetStorageKey(document) is not { } storageKey)
                throw new DomException("The document is not allowed to access the session storage", "SecurityError");

            var storage = new Storage(this, browsingContext.Top.SessionStorageShed.ObtainArea(storageKey), isSession: true);
            document.SessionStorageHolder = storage;
            return storage;
        }
    }

    /// <summary>
    /// The page credentialless nonce of a top-level window (https://wicg.github.io/anonymous-iframe/#page-credentialless-nonce):
    /// the partition nonce of the storage keys of the credentialless iframes of the page.
    /// </summary>
    internal Guid PageCredentiallessNonce
    {
        get
        {
            if (field == Guid.Empty)
            {
                field = Guid.NewGuid();
            }

            return field;
        }
    }

    /// <summary>
    /// https://storage.spec.whatwg.org/#obtain-a-storage-key: the origin of the document, or null (failure) when it is
    /// opaque. The documents of the credentialless iframes are partitioned by the page credentialless nonce of the top-level
    /// window (https://wicg.github.io/anonymous-iframe/#spec-storage-key).
    /// </summary>
    internal static StorageKey? GetStorageKey(Document document)
    {
        var origin = document.Origin;
        if (origin.IsOpaque)
            return null;

        var nonce = document.IsCredentialless && document.BrowsingContext?.Top.Window is { } topLevelWindow ? topLevelWindow.PageCredentiallessNonce : (Guid?)null;
        return new StorageKey(origin.Serialize(), nonce);
    }
}
