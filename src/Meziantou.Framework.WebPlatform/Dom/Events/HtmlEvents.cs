using System.Collections.ObjectModel;

namespace Meziantou.Framework.WebPlatform.Dom;

// Event interfaces defined by the HTML specification. There is no navigation, messaging or storage, so these events are
// only created by constructors and document.createEvent().

/// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#beforeunloadevent</summary>
[IdlInterface("BeforeUnloadEvent", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#beforeunloadevent")]
public sealed class BeforeUnloadEvent : Event
{
    private BeforeUnloadEvent()
    {
    }

    internal static BeforeUnloadEvent CreateUninitializedBeforeUnloadEvent() => new();

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-beforeunloadevent-returnvalue</summary>
    [IdlMember("returnValue", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-beforeunloadevent-returnvalue")]
    public new string ReturnValue { get; set; } = "";
}

/// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#hashchangeeventinit</summary>
[IdlInterface("HashChangeEventInit", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#hashchangeeventinit")]
public sealed class HashChangeEventInit : EventInit
{
    /// <summary>Gets the URL of the document before the navigation.</summary>
    [IdlMember("oldURL", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-hashchangeeventinit-oldurl")]
    public string OldUrl { get; init; } = "";

    /// <summary>Gets the URL of the document after the navigation.</summary>
    [IdlMember("newURL", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-hashchangeeventinit-newurl")]
    public string NewUrl { get; init; } = "";
}

/// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#hashchangeevent</summary>
[IdlInterface("HashChangeEvent", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#hashchangeevent")]
public sealed class HashChangeEvent : Event
{
    /// <summary>Creates a hash change event with the given type and initialization dictionary.</summary>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-hashchangeevent-hashchangeevent")]
    public HashChangeEvent(string type, HashChangeEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        OldUrl = eventInitDict?.OldUrl ?? "";
        NewUrl = eventInitDict?.NewUrl ?? "";
    }

    private HashChangeEvent()
    {
    }

    internal static HashChangeEvent CreateUninitializedHashChangeEvent() => new();

    /// <summary>Gets the URL of the document before the navigation.</summary>
    [IdlMember("oldURL", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-hashchangeevent-oldurl")]
    public string OldUrl { get; } = "";

    /// <summary>Gets the URL of the document after the navigation.</summary>
    [IdlMember("newURL", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-hashchangeevent-newurl")]
    public string NewUrl { get; } = "";
}

/// <summary>https://html.spec.whatwg.org/multipage/comms.html#messageeventinit</summary>
[IdlInterface("MessageEventInit", "https://html.spec.whatwg.org/multipage/comms.html#messageeventinit")]
public sealed class MessageEventInit : EventInit
{
    /// <summary>Gets the data of the message.</summary>
    [IdlMember("data", "https://html.spec.whatwg.org/multipage/comms.html#dom-messageeventinit-data")]
    public object? Data { get; init; }

    /// <summary>Gets the origin of the message sender.</summary>
    [IdlMember("origin", "https://html.spec.whatwg.org/multipage/comms.html#dom-messageeventinit-origin")]
    public string Origin { get; init; } = "";

    /// <summary>Gets the last event ID of the event source that sent the message.</summary>
    [IdlMember("lastEventId", "https://html.spec.whatwg.org/multipage/comms.html#dom-messageeventinit-lasteventid")]
    public string LastEventId { get; init; } = "";

    /// <summary>Gets the object that sent the message, such as a window or a message port.</summary>
    [IdlMember("source", "https://html.spec.whatwg.org/multipage/comms.html#dom-messageeventinit-source")]
    public EventTarget? Source { get; init; }

    /// <summary>Gets the message ports sent with the message.</summary>
    [IdlMember("ports", "https://html.spec.whatwg.org/multipage/comms.html#dom-messageeventinit-ports")]
    public IReadOnlyList<MessagePort> Ports { get; init; } = [];
}

/// <summary>https://html.spec.whatwg.org/multipage/comms.html#messageevent</summary>
[IdlInterface("MessageEvent", "https://html.spec.whatwg.org/multipage/comms.html#messageevent")]
public sealed class MessageEvent : Event
{
    /// <summary>Creates a message event with the given type and initialization dictionary.</summary>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/comms.html#dom-messageevent-messageevent")]
    public MessageEvent(string type, MessageEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        Data = eventInitDict?.Data;
        Origin = eventInitDict?.Origin ?? "";
        LastEventId = eventInitDict?.LastEventId ?? "";
        Source = eventInitDict?.Source;
        Ports = CreatePorts(eventInitDict?.Ports);
    }

    private MessageEvent()
    {
    }

    internal static MessageEvent CreateUninitializedMessageEvent() => new();

    /// <summary>Gets the data of the message.</summary>
    [IdlMember("data", "https://html.spec.whatwg.org/multipage/comms.html#dom-messageevent-data")]
    public object? Data { get; private set; }

    /// <summary>Gets the origin of the message sender.</summary>
    [IdlMember("origin", "https://html.spec.whatwg.org/multipage/comms.html#dom-messageevent-origin")]
    public string Origin { get; private set; } = "";

    /// <summary>
    /// The origin of a message event fired by the user agent (https://html.spec.whatwg.org/multipage/comms.html#concept-MessageEvent-origin),
    /// whose serialization is <see cref="Origin"/>, or null when the origin is a string (e.g. a constructed event).
    /// </summary>
    internal OriginRecord? OriginRecord
    {
        get;
        set
        {
            field = value;
            Origin = value?.Serialize() ?? Origin;
        }
    }

    /// <summary>Gets the last event ID of the event source that sent the message.</summary>
    [IdlMember("lastEventId", "https://html.spec.whatwg.org/multipage/comms.html#dom-messageevent-lasteventid")]
    public string LastEventId { get; private set; } = "";

    /// <summary>Gets the object that sent the message, such as a window or a message port.</summary>
    [IdlMember("source", "https://html.spec.whatwg.org/multipage/comms.html#dom-messageevent-source")]
    public EventTarget? Source { get; private set; }

    /// <summary>Gets the message ports sent with the message (https://html.spec.whatwg.org/multipage/comms.html#dom-messageevent-ports).</summary>
    [IdlMember("ports", "https://html.spec.whatwg.org/multipage/comms.html#dom-messageevent-ports")]
    public IReadOnlyList<MessagePort> Ports { get; private set; } = CreatePorts(null);

    /// <summary>https://html.spec.whatwg.org/multipage/comms.html#dom-messageevent-initmessageevent</summary>
    [IdlMember("initMessageEvent", "https://html.spec.whatwg.org/multipage/comms.html#dom-messageevent-initmessageevent")]
    public void InitMessageEvent(string type, bool bubbles = false, bool cancelable = false, object? data = null, string origin = "", string lastEventId = "", EventTarget? source = null, IReadOnlyList<MessagePort>? ports = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (DispatchFlag)
            return;

        Initialize(type, bubbles, cancelable);
        Data = data;
        OriginRecord = null;
        Origin = origin ?? "";
        LastEventId = lastEventId ?? "";
        Source = source;
        Ports = CreatePorts(ports);
    }

    // Each event has its own frozen array (a new collection, as the frozen arrays of the script engine are cached per collection)
    private static ReadOnlyCollection<MessagePort> CreatePorts(IReadOnlyList<MessagePort>? ports) => new([.. ports ?? []]);
}

/// <summary>https://html.spec.whatwg.org/multipage/webstorage.html#storageeventinit</summary>
[IdlInterface("StorageEventInit", "https://html.spec.whatwg.org/multipage/webstorage.html#storageeventinit")]
public sealed class StorageEventInit : EventInit
{
    /// <summary>Gets the key that was changed, or null when the storage was cleared.</summary>
    [IdlMember("key", "https://html.spec.whatwg.org/multipage/webstorage.html#dom-storageeventinit-key")]
    public string? Key { get; init; }

    /// <summary>Gets the previous value of the key, or null when it did not exist.</summary>
    [IdlMember("oldValue", "https://html.spec.whatwg.org/multipage/webstorage.html#dom-storageeventinit-oldvalue")]
    public string? OldValue { get; init; }

    /// <summary>Gets the new value of the key, or null when it was removed.</summary>
    [IdlMember("newValue", "https://html.spec.whatwg.org/multipage/webstorage.html#dom-storageeventinit-newvalue")]
    public string? NewValue { get; init; }

    /// <summary>Gets the URL of the document whose storage changed.</summary>
    [IdlMember("url", "https://html.spec.whatwg.org/multipage/webstorage.html#dom-storageeventinit-url")]
    public string Url { get; init; } = "";

    /// <summary>Gets the storage area that changed.</summary>
    [IdlMember("storageArea", "https://html.spec.whatwg.org/multipage/webstorage.html#dom-storageeventinit-storagearea")]
    public Storage? StorageArea { get; init; }
}

/// <summary>https://html.spec.whatwg.org/multipage/webstorage.html#storageevent</summary>
[IdlInterface("StorageEvent", "https://html.spec.whatwg.org/multipage/webstorage.html#storageevent")]
public sealed class StorageEvent : Event, IRealmBoundPlatformObject
{
    /// <summary>Creates a storage event with the given type and initialization dictionary.</summary>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/webstorage.html#dom-storageevent-storageevent")]
    public StorageEvent(string type, StorageEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        Key = eventInitDict?.Key;
        OldValue = eventInitDict?.OldValue;
        NewValue = eventInitDict?.NewValue;
        Url = eventInitDict?.Url ?? "";
        StorageArea = eventInitDict?.StorageArea;
    }

    private StorageEvent()
    {
    }

    internal static StorageEvent CreateUninitializedStorageEvent() => new();

    /// <summary>The window in whose realm the event is created (the window at which the user agent fires it), or null for the current realm.</summary>
    internal Window? RealmWindow { get; init; }

    Window? IRealmBoundPlatformObject.RealmWindow => RealmWindow;

    /// <summary>Gets the key that was changed, or null when the storage was cleared.</summary>
    [IdlMember("key", "https://html.spec.whatwg.org/multipage/webstorage.html#dom-storageevent-key")]
    public string? Key { get; private set; }

    /// <summary>Gets the previous value of the key, or null when it did not exist.</summary>
    [IdlMember("oldValue", "https://html.spec.whatwg.org/multipage/webstorage.html#dom-storageevent-oldvalue")]
    public string? OldValue { get; private set; }

    /// <summary>Gets the new value of the key, or null when it was removed.</summary>
    [IdlMember("newValue", "https://html.spec.whatwg.org/multipage/webstorage.html#dom-storageevent-newvalue")]
    public string? NewValue { get; private set; }

    /// <summary>Gets the URL of the document whose storage changed.</summary>
    [IdlMember("url", "https://html.spec.whatwg.org/multipage/webstorage.html#dom-storageevent-url")]
    public string Url { get; private set; } = "";

    /// <summary>Gets the storage area that changed.</summary>
    [IdlMember("storageArea", "https://html.spec.whatwg.org/multipage/webstorage.html#dom-storageevent-storagearea")]
    public Storage? StorageArea { get; private set; }

    /// <summary>https://html.spec.whatwg.org/multipage/webstorage.html#dom-storageevent-initstorageevent</summary>
    [IdlMember("initStorageEvent", "https://html.spec.whatwg.org/multipage/webstorage.html#dom-storageevent-initstorageevent")]
    public void InitStorageEvent(string type, bool bubbles = false, bool cancelable = false, string? key = null, string? oldValue = null, string? newValue = null, string url = "", Storage? storageArea = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (DispatchFlag)
            return;

        Initialize(type, bubbles, cancelable);
        Key = key;
        OldValue = oldValue;
        NewValue = newValue;
        Url = url ?? "";
        StorageArea = storageArea;
    }
}

/// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#erroreventinit</summary>
[IdlInterface("ErrorEventInit", "https://html.spec.whatwg.org/multipage/webappapis.html#erroreventinit")]
public sealed class ErrorEventInit : EventInit
{
    /// <summary>Gets the error message.</summary>
    [IdlMember("message", "https://html.spec.whatwg.org/multipage/webappapis.html#dom-erroreventinit-message")]
    public string Message { get; init; } = "";

    /// <summary>Gets the URL of the script in which the error occurred.</summary>
    [IdlMember("filename", "https://html.spec.whatwg.org/multipage/webappapis.html#dom-erroreventinit-filename")]
    public string Filename { get; init; } = "";

    /// <summary>Gets the line number at which the error occurred.</summary>
    [IdlMember("lineno", "https://html.spec.whatwg.org/multipage/webappapis.html#dom-erroreventinit-lineno")]
    public uint Lineno { get; init; }

    /// <summary>Gets the column number at which the error occurred.</summary>
    [IdlMember("colno", "https://html.spec.whatwg.org/multipage/webappapis.html#dom-erroreventinit-colno")]
    public uint Colno { get; init; }

    /// <summary>Gets the thrown value that caused the error.</summary>
    [IdlMember("error", "https://html.spec.whatwg.org/multipage/webappapis.html#dom-erroreventinit-error")]
    public object? Error { get; init; }
}

/// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#errorevent</summary>
[IdlInterface("ErrorEvent", "https://html.spec.whatwg.org/multipage/webappapis.html#errorevent")]
public sealed class ErrorEvent : Event
{
    /// <summary>Creates an error event with the given type and initialization dictionary.</summary>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/webappapis.html#dom-errorevent-errorevent")]
    public ErrorEvent(string type, ErrorEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        Message = eventInitDict?.Message ?? "";
        Filename = eventInitDict?.Filename ?? "";
        Lineno = eventInitDict?.Lineno ?? 0;
        Colno = eventInitDict?.Colno ?? 0;
        Error = eventInitDict?.Error;
    }

    /// <summary>Gets the error message.</summary>
    [IdlMember("message", "https://html.spec.whatwg.org/multipage/webappapis.html#dom-errorevent-message")]
    public string Message { get; }

    /// <summary>Gets the URL of the script in which the error occurred.</summary>
    [IdlMember("filename", "https://html.spec.whatwg.org/multipage/webappapis.html#dom-errorevent-filename")]
    public string Filename { get; }

    /// <summary>Gets the line number at which the error occurred.</summary>
    [IdlMember("lineno", "https://html.spec.whatwg.org/multipage/webappapis.html#dom-errorevent-lineno")]
    public uint Lineno { get; }

    /// <summary>Gets the column number at which the error occurred.</summary>
    [IdlMember("colno", "https://html.spec.whatwg.org/multipage/webappapis.html#dom-errorevent-colno")]
    public uint Colno { get; }

    /// <summary>Gets the thrown value that caused the error.</summary>
    [IdlMember("error", "https://html.spec.whatwg.org/multipage/webappapis.html#dom-errorevent-error")]
    public object? Error { get; }
}

/// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#promiserejectioneventinit</summary>
[IdlInterface("PromiseRejectionEventInit", "https://html.spec.whatwg.org/multipage/webappapis.html#promiserejectioneventinit")]
public sealed class PromiseRejectionEventInit : EventInit
{
    /// <summary>The promise: a value of the script engine.</summary>
    [IdlMember("promise", "https://html.spec.whatwg.org/multipage/webappapis.html#dom-promiserejectioneventinit-promise")]
    public required object Promise { get; init; }

    /// <summary>Gets the reason the promise was rejected.</summary>
    [IdlMember("reason", "https://html.spec.whatwg.org/multipage/webappapis.html#dom-promiserejectioneventinit-reason")]
    public object? Reason { get; init; }
}

/// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#promiserejectionevent</summary>
[IdlInterface("PromiseRejectionEvent", "https://html.spec.whatwg.org/multipage/webappapis.html#promiserejectionevent")]
public sealed class PromiseRejectionEvent : Event
{
    /// <summary>Creates a promise rejection event with the given type and initialization dictionary.</summary>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/webappapis.html#dom-promiserejectionevent-constructor")]
    public PromiseRejectionEvent(string type, PromiseRejectionEventInit eventInitDict)
        : base(type, eventInitDict)
    {
        ArgumentNullException.ThrowIfNull(eventInitDict);
        Promise = eventInitDict.Promise;
        Reason = eventInitDict.Reason;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#dom-promiserejectionevent-promise: a value of the script engine.</summary>
    [IdlMember("promise", "https://html.spec.whatwg.org/multipage/webappapis.html#dom-promiserejectionevent-promise")]
    public object Promise { get; }

    /// <summary>Gets the reason the promise was rejected.</summary>
    [IdlMember("reason", "https://html.spec.whatwg.org/multipage/webappapis.html#dom-promiserejectionevent-reason")]
    public object? Reason { get; }
}

/// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#pagetransitioneventinit</summary>
[IdlInterface("PageTransitionEventInit", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#pagetransitioneventinit")]
public sealed class PageTransitionEventInit : EventInit
{
    /// <summary>Gets a value indicating whether the page was loaded from a cache.</summary>
    [IdlMember("persisted", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-pagetransitioneventinit-persisted")]
    public bool Persisted { get; init; }
}

/// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#pagetransitionevent</summary>
[IdlInterface("PageTransitionEvent", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#pagetransitionevent")]
public sealed class PageTransitionEvent : Event
{
    /// <summary>Creates a page transition event with the given type and initialization dictionary.</summary>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-pagetransitionevent-pagetransitionevent")]
    public PageTransitionEvent(string type, PageTransitionEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        Persisted = eventInitDict?.Persisted ?? false;
    }

    /// <summary>Gets a value indicating whether the page was loaded from a cache.</summary>
    [IdlMember("persisted", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-pagetransitionevent-persisted")]
    public bool Persisted { get; }
}

/// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#popstateeventinit</summary>
[IdlInterface("PopStateEventInit", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#popstateeventinit")]
public sealed class PopStateEventInit : EventInit
{
    /// <summary>Gets the state object of the history entry that was navigated to.</summary>
    [IdlMember("state", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-popstateeventinit-state")]
    public object? State { get; init; }

    /// <summary>
    /// Gets a value indicating whether the user agent already performed a visual transition for the navigation.
    /// </summary>
    [IdlMember("hasUAVisualTransition", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-popstateeventinit-hasuavisualtransition")]
    public bool HasUAVisualTransition { get; init; }
}

/// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#popstateevent</summary>
[IdlInterface("PopStateEvent", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#popstateevent")]
public sealed class PopStateEvent : Event
{
    /// <summary>Creates a pop state event with the given type and initialization dictionary.</summary>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-popstateevent-popstateevent")]
    public PopStateEvent(string type, PopStateEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        State = eventInitDict?.State;
        HasUAVisualTransition = eventInitDict?.HasUAVisualTransition ?? false;
    }

    /// <summary>Gets the state object of the history entry that was navigated to.</summary>
    [IdlMember("state", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-popstateevent-state")]
    public object? State { get; }

    /// <summary>
    /// Gets a value indicating whether the user agent already performed a visual transition for the navigation.
    /// </summary>
    [IdlMember("hasUAVisualTransition", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-popstateevent-hasuavisualtransition")]
    public bool HasUAVisualTransition { get; }
}

/// <summary>https://xhr.spec.whatwg.org/#progresseventinit</summary>
[IdlInterface("ProgressEventInit", "https://xhr.spec.whatwg.org/#progresseventinit")]
public sealed class ProgressEventInit : EventInit
{
    /// <summary>Gets a value indicating whether the total amount of work is known.</summary>
    [IdlMember("lengthComputable", "https://xhr.spec.whatwg.org/#dom-progresseventinit-lengthcomputable")]
    public bool LengthComputable { get; init; }

    /// <summary>Gets the amount of work already performed.</summary>
    [IdlMember("loaded", "https://xhr.spec.whatwg.org/#dom-progresseventinit-loaded")]
    public double Loaded { get; init; }

    /// <summary>Gets the total amount of work to perform.</summary>
    [IdlMember("total", "https://xhr.spec.whatwg.org/#dom-progresseventinit-total")]
    public double Total { get; init; }
}

/// <summary>https://xhr.spec.whatwg.org/#interface-progressevent</summary>
[IdlInterface("ProgressEvent", "https://xhr.spec.whatwg.org/#progressevent")]
public sealed class ProgressEvent : Event
{
    /// <summary>Creates a progress event with the given type and initialization dictionary.</summary>
    [IdlMember("constructor", "https://xhr.spec.whatwg.org/#dom-progressevent-progressevent")]
    public ProgressEvent(string type, ProgressEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        LengthComputable = eventInitDict?.LengthComputable ?? false;
        Loaded = eventInitDict?.Loaded ?? 0;
        Total = eventInitDict?.Total ?? 0;
    }

    /// <summary>Gets a value indicating whether the total amount of work is known.</summary>
    [IdlMember("lengthComputable", "https://xhr.spec.whatwg.org/#dom-progressevent-lengthcomputable")]
    public bool LengthComputable { get; }

    /// <summary>Gets the amount of work already performed.</summary>
    [IdlMember("loaded", "https://xhr.spec.whatwg.org/#dom-progressevent-loaded")]
    public double Loaded { get; }

    /// <summary>Gets the total amount of work to perform.</summary>
    [IdlMember("total", "https://xhr.spec.whatwg.org/#dom-progressevent-total")]
    public double Total { get; }
}

/// <summary>https://html.spec.whatwg.org/multipage/interaction.html#toggleeventinit</summary>
[IdlInterface("ToggleEventInit", "https://html.spec.whatwg.org/multipage/interaction.html#toggleeventinit")]
public sealed class ToggleEventInit : EventInit
{
    /// <summary>Gets the state of the element before the toggle.</summary>
    [IdlMember("oldState", "https://html.spec.whatwg.org/multipage/interaction.html#dom-toggleeventinit-oldstate")]
    public string OldState { get; init; } = "";

    /// <summary>Gets the state of the element after the toggle.</summary>
    [IdlMember("newState", "https://html.spec.whatwg.org/multipage/interaction.html#dom-toggleeventinit-newstate")]
    public string NewState { get; init; } = "";

    /// <summary>Gets the element that triggered the toggle.</summary>
    [IdlMember("source", "https://html.spec.whatwg.org/multipage/interaction.html#dom-toggleeventinit-source")]
    public Element? Source { get; init; }
}

/// <summary>https://html.spec.whatwg.org/multipage/interaction.html#toggleevent</summary>
[IdlInterface("ToggleEvent", "https://html.spec.whatwg.org/multipage/interaction.html#toggleevent")]
public sealed class ToggleEvent : Event
{
    private readonly Element? _source;

    /// <summary>Creates a toggle event with the given type and initialization dictionary.</summary>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/interaction.html#dom-toggleevent-toggleevent")]
    public ToggleEvent(string type, ToggleEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        OldState = eventInitDict?.OldState ?? "";
        NewState = eventInitDict?.NewState ?? "";
        _source = eventInitDict?.Source;
    }

    /// <summary>Gets the state of the element before the toggle.</summary>
    [IdlMember("oldState", "https://html.spec.whatwg.org/multipage/interaction.html#dom-toggleevent-oldstate")]
    public string OldState { get; }

    /// <summary>Gets the state of the element after the toggle.</summary>
    [IdlMember("newState", "https://html.spec.whatwg.org/multipage/interaction.html#dom-toggleevent-newstate")]
    public string NewState { get; }

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#dom-toggleevent-source: the source retargeted against the current target.</summary>
    [IdlMember("source", "https://html.spec.whatwg.org/multipage/interaction.html#dom-toggleevent-source")]
    public Element? Source => EventDispatcher.Retarget(_source, CurrentTarget) as Element;
}

/// <summary>https://html.spec.whatwg.org/multipage/interaction.html#commandeventinit</summary>
[IdlInterface("CommandEventInit", "https://html.spec.whatwg.org/multipage/interaction.html#commandeventinit")]
public sealed class CommandEventInit : EventInit
{
    /// <summary>Gets the element that invoked the command.</summary>
    [IdlMember("source", "https://html.spec.whatwg.org/multipage/interaction.html#commandeventinit")]
    public Element? Source { get; init; }

    /// <summary>Gets the command that was invoked.</summary>
    [IdlMember("command", "https://html.spec.whatwg.org/multipage/interaction.html#commandeventinit")]
    public string Command { get; init; } = "";
}

/// <summary>https://html.spec.whatwg.org/multipage/interaction.html#commandevent</summary>
[IdlInterface("CommandEvent", "https://html.spec.whatwg.org/multipage/interaction.html#commandevent")]
public sealed class CommandEvent : Event
{
    private readonly Element? _source;

    /// <summary>Creates a command event with the given type and initialization dictionary.</summary>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/interaction.html#commandevent")]
    public CommandEvent(string type, CommandEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        Command = eventInitDict?.Command ?? "";
        _source = eventInitDict?.Source;
    }

    /// <summary>Gets the command that was invoked.</summary>
    [IdlMember("command", "https://html.spec.whatwg.org/multipage/interaction.html#dom-commandevent-command")]
    public string Command { get; }

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#dom-commandevent-source: the source retargeted against the current target.</summary>
    [IdlMember("source", "https://html.spec.whatwg.org/multipage/interaction.html#dom-commandevent-source")]
    public Element? Source => EventDispatcher.Retarget(_source, CurrentTarget) as Element;
}
