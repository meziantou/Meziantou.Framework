using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#scrollrestoration</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#scrollrestoration"/>
[IdlInterface("ScrollRestoration", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#scrollrestoration")]
public enum ScrollRestoration
{
    /// <summary>The user agent restores the scroll position when the entry is traversed.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-scrollrestoration-auto"/>
    [IdlMember("auto", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-scrollrestoration-auto")]
    Auto,

    /// <summary>The page restores the scroll position itself.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-scrollrestoration-manual"/>
    [IdlMember("manual", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-scrollrestoration-manual")]
    Manual,
}

/// <summary>
/// https://html.spec.whatwg.org/multipage/nav-history-apis.html#the-history-interface. The session history is the joint
/// session history of the top-level traversable (its entries and the ones of its child navigables). The documents of the
/// other entries are not kept alive (there is no back/forward cache): a traversal to another document loads it again.
/// </summary>
/// <remarks>
/// <para>
/// Each document has its own History object (https://html.spec.whatwg.org/multipage/nav-history-apis.html#doc-history):
/// a navigation that reuses the window of an initial about:blank document gives it a new History object.
/// </para>
/// <para>
/// The state is structured-serialized by <see cref="PushState"/> and <see cref="ReplaceState"/> (by the script engine of the
/// window, or, without a script engine, only the primitive values and the serializable platform objects can be serialized),
/// and <see cref="State"/> is a deserialization of the state of the session history entry.
/// </para>
/// </remarks>
/// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#history-3"/>
[IdlInterface("History", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#history-3")]
public sealed class History
{
    private readonly Window _window;

    // https://html.spec.whatwg.org/multipage/nav-history-apis.html#concept-history-state: the state is deserialized when it is
    // first read (the deserialization has no side effect, so the result is the same as a deserialization when it is restored)
    private object? _serializedState;
    private object? _state;
    private bool _isStateDeserialized = true;

    internal History(Window window)
    {
        _window = window;
    }

    /// <summary>The navigable of the associated document, when the document is fully active.</summary>
    private BrowsingContext? FullyActiveNavigable => _window.Document.IsFullyActive ? _window.Document.BrowsingContext : null;

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-history-length</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-history-length"/>
    [IdlMember("length", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-history-length")]
    public uint Length
    {
        get
        {
            var navigable = FullyActiveNavigable ?? throw new DomException("The document is not fully active", "SecurityError");
            return (uint)navigable.GetHistoryObjectLengthAndIndex().Length;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-history-scroll-restoration</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-history-scroll-restoration"/>
    [IdlMember("scrollRestoration", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-history-scroll-restoration")]
    public ScrollRestoration ScrollRestoration
    {
        get
        {
            var navigable = FullyActiveNavigable ?? throw new DomException("The document is not fully active", "SecurityError");
            return navigable.ActiveEntry.ScrollRestoration;
        }
        set
        {
            var navigable = FullyActiveNavigable ?? throw new DomException("The document is not fully active", "SecurityError");
            navigable.ActiveEntry.ScrollRestoration = value;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-history-state</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-history-state"/>
    [IdlMember("state", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-history-state")]
    public object? State
    {
        get
        {
            if (FullyActiveNavigable is null)
                throw new DomException("The document is not fully active", "SecurityError");

            return StateValue;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#concept-history-state</summary>
    internal object? StateValue
    {
        get
        {
            if (!_isStateDeserialized)
            {
                _state = DeserializeState(_serializedState);
                _isStateDeserialized = true;
            }

            return _state;
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/browsing-the-web.html#restore-the-history-object-state: the state becomes a
    /// deserialization of the classic history API state of the entry (a deserialization that fails gives null).
    /// </summary>
    internal void RestoreState(SessionHistoryEntry entry)
    {
        _serializedState = entry.ClassicHistoryApiState;
        _state = null;
        _isStateDeserialized = _serializedState is null;
    }

    /// <summary>StructuredSerializeForStorage (https://html.spec.whatwg.org/multipage/structured-data.html#structuredserializeforstorage) in the realm of the window: undefined (null) is not serialized.</summary>
    private object? SerializeState(object? value)
    {
        if (value is null)
            return null;

        return _window.ScriptEngine is { } engine ? engine.StructuredSerializeForStorage(_window, value) : StructuredClone.Serialize(value);
    }

    private object? DeserializeState(object? serialized)
    {
        if (serialized is null)
            return null;

        try
        {
            return _window.ScriptEngine is { } engine ? engine.StructuredDeserialize(_window, serialized) : StructuredClone.Deserialize(serialized);
        }
#pragma warning disable CA1031 // https://html.spec.whatwg.org/multipage/browsing-the-web.html#restore-the-history-object-state: an exception gives null
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-history-go</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-history-go"/>
    [IdlMember("go", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-history-go")]
    public void Go(int delta = 0) => DeltaTraverse(delta);

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-history-back</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-history-back"/>
    [IdlMember("back", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-history-back")]
    public void Back() => DeltaTraverse(-1);

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-history-forward</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-history-forward"/>
    [IdlMember("forward", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-history-forward")]
    public void Forward() => DeltaTraverse(1);

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#delta-traverse</summary>
    private void DeltaTraverse(int delta)
    {
        var navigable = FullyActiveNavigable ?? throw new DomException("The document is not fully active", "SecurityError");
        if (!navigable.IsAllowedToPerformNavigationOrHistoryUpdate())
            return;

        if (delta == 0)
        {
            navigable.ReloadNavigable();
            return;
        }

        navigable.TraverseByDelta(delta, _window.Document);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-history-pushstate</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-history-pushstate"/>
    [IdlMember("pushState", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-history-pushstate")]
    public void PushState(object? data, string unused, string? url = null)
    {
        ArgumentNullException.ThrowIfNull(unused);
        PushOrReplaceState(data, url, NavigationHistoryBehavior.Push);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-history-replacestate</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-history-replacestate"/>
    [IdlMember("replaceState", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-history-replacestate")]
    public void ReplaceState(object? data, string unused, string? url = null)
    {
        ArgumentNullException.ThrowIfNull(unused);
        PushOrReplaceState(data, url, NavigationHistoryBehavior.Replace);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#shared-history-push/replace-state-steps</summary>
    private void PushOrReplaceState(object? data, string? url, NavigationHistoryBehavior historyHandling)
    {
        var document = _window.Document;
        var navigable = FullyActiveNavigable ?? throw new DomException("The document is not fully active", "SecurityError");
        var serializedData = SerializeState(data);
        var newUrl = document.UrlRecord;
        if (!string.IsNullOrEmpty(url))
        {
            newUrl = UrlParser.Parse(url, document.DocumentBaseUrl, document.Encoding) ?? throw new DomException($"'{url}' is not a valid URL", "SecurityError");
            if (!CanHaveUrlRewritten(document.UrlRecord, newUrl))
                throw new DomException($"The URL of the document cannot be changed to '{url}'", "SecurityError");
        }

        if (!navigable.IsAllowedToPerformNavigationOrHistoryUpdate())
            return;

        // The navigate event, which can cancel the update (https://html.spec.whatwg.org/multipage/nav-history-apis.html#shared-history-push/replace-state-steps, step 8).
        // As in the browsers, the updates are throttled: an update from a navigate event listener nested too deeply throws.
        if (_window.Navigation.IsNavigateEventNestingTooDeep)
            throw new DomException("Too many history updates", "SecurityError");

        var navigationType = historyHandling == NavigationHistoryBehavior.Push ? NavigationType.Push : NavigationType.Replace;
        if (!_window.Navigation.FirePushReplaceReloadNavigateEvent(navigationType, newUrl, isSameDocument: true, classicHistoryApiState: new NavigationApiStateValue(serializedData)))
            return;

        navigable.UpdateUrlAndHistory(newUrl, serializedData, hasSerializedData: true, historyHandling);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#can-have-its-url-rewritten</summary>
    internal static bool CanHaveUrlRewritten(UrlRecord documentUrl, UrlRecord targetUrl)
    {
        if (documentUrl.Scheme != targetUrl.Scheme || documentUrl.Username != targetUrl.Username || documentUrl.Password != targetUrl.Password || documentUrl.Host?.Serialized != targetUrl.Host?.Serialized || documentUrl.Port != targetUrl.Port)
            return false;

        if (targetUrl.Scheme is "http" or "https")
            return true;

        if (targetUrl.Scheme == "file")
            return documentUrl.SerializePath() == targetUrl.SerializePath();

        return documentUrl.SerializePath() == targetUrl.SerializePath() && documentUrl.Query == targetUrl.Query;
    }
}
