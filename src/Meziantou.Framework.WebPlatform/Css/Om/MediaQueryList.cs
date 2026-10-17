using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Om;

/// <summary>
/// A media query list whose state can be observed (https://drafts.csswg.org/cssom-view/#the-mediaquerylist-interface),
/// created by <see cref="Window.MatchMedia"/>.
/// </summary>
/// <remarks>
/// The media query list is evaluated against the environment of its document (see <see cref="ViewportOptions"/>; the
/// viewport of a nested browsing context is the size of its <c>iframe</c>). The <c>change</c> events are fired when the
/// event loop updates the rendering (e.g. before the animation frame callbacks), as specified.
/// </remarks>
[IdlInterface("MediaQueryList", "https://drafts.csswg.org/cssom-view/#mediaquerylist")]
public sealed class MediaQueryList : EventTarget, IRealmBoundPlatformObject
{
    private readonly List<string> _queries;
    private bool _keptAlive;

    internal MediaQueryList(Document document, string query)
    {
        Document = document;
        _queries = MediaQueries.ParseList(query);
        Media = string.Join(", ", _queries);
        LastMatches = Matches;
        document.AddMediaQueryList(this);
    }

    /// <summary>The document of the media query list.</summary>
    internal Document Document { get; }

    /// <summary>The matches state when the changes were last reported (https://drafts.csswg.org/cssom-view/#mediaquerylist-matches-state).</summary>
    internal bool LastMatches { get; set; }

    internal override Window? RelevantGlobal => Document.RelevantWindow;

    Window? IRealmBoundPlatformObject.RealmWindow => Document.RelevantWindow;

    /// <summary>Gets the serialized media query list (https://drafts.csswg.org/cssom-view/#dom-mediaquerylist-media).</summary>
    [IdlMember("media", "https://drafts.csswg.org/cssom-view/#dom-mediaquerylist-media")]
    public string Media { get; }

    /// <summary>Gets whether the media query list matches the environment of the document (https://drafts.csswg.org/cssom-view/#dom-mediaquerylist-matches).</summary>
    [IdlMember("matches", "https://drafts.csswg.org/cssom-view/#dom-mediaquerylist-matches")]
    public bool Matches => MediaQueryEvaluator.Matches(_queries, StyleEngine.CreateEnvironment(Document));

    /// <summary>Adds a listener of the <c>change</c> events (https://drafts.csswg.org/cssom-view/#dom-mediaquerylist-addlistener).</summary>
    [IdlMember("addListener", "https://drafts.csswg.org/cssom-view/#dom-mediaquerylist-addlistener")]
    public void AddListener(IEventListener? callback) => AddEventListenerCore("change", callback, options: null);

    /// <summary>Adds a listener of the <c>change</c> events (https://drafts.csswg.org/cssom-view/#dom-mediaquerylist-addlistener).</summary>
    [NotInIdl("Convenience overload for delegates; the delegate is the identity of the listener")]
    public void AddListener(Action<Event>? callback) => AddEventListenerCore("change", callback, options: null);

    /// <summary>Removes a listener of the <c>change</c> events (https://drafts.csswg.org/cssom-view/#dom-mediaquerylist-removelistener).</summary>
    [IdlMember("removeListener", "https://drafts.csswg.org/cssom-view/#dom-mediaquerylist-removelistener")]
    public void RemoveListener(IEventListener? callback) => RemoveEventListenerCore("change", callback, capture: false);

    /// <summary>Removes a listener of the <c>change</c> events (https://drafts.csswg.org/cssom-view/#dom-mediaquerylist-removelistener).</summary>
    [NotInIdl("Convenience overload for delegates")]
    public void RemoveListener(Action<Event>? callback) => RemoveEventListenerCore("change", callback, capture: false);

    /// <summary>Gets or sets the event handler of the <c>change</c> event.</summary>
    [IdlMember("onchange", "https://drafts.csswg.org/cssom-view/#dom-mediaquerylist-onchange")]
    public Action<Event>? OnChange
    {
        get => GetEventHandler("change");
        set => SetEventHandler("change", value);
    }

    private protected override void OnEventListenerAdded(EventListenerEntry listener)
    {
        // A media query list with event listeners must not be garbage collected while its document is alive
        if (!_keptAlive)
        {
            _keptAlive = true;
            Document.KeepMediaQueryListAlive(this);
        }
    }
}

/// <summary>https://drafts.csswg.org/cssom-view/#dictdef-mediaquerylisteventinit</summary>
[IdlInterface("MediaQueryListEventInit", "https://drafts.csswg.org/cssom-view/#dictdef-mediaquerylisteventinit")]
public sealed class MediaQueryListEventInit : EventInit
{
    /// <summary>Gets the serialized media query list of the event.</summary>
    [IdlMember("media", "https://drafts.csswg.org/cssom-view/#dom-mediaquerylisteventinit-media")]
    public string Media { get; init; } = "";

    /// <summary>Gets a value indicating whether the document matches the media query list.</summary>
    [IdlMember("matches", "https://drafts.csswg.org/cssom-view/#dom-mediaquerylisteventinit-matches")]
    public bool Matches { get; init; }
}

/// <summary>The event fired when the state of a <see cref="MediaQueryList"/> changes (https://drafts.csswg.org/cssom-view/#mediaquerylistevent).</summary>
[IdlInterface("MediaQueryListEvent", "https://drafts.csswg.org/cssom-view/#mediaquerylistevent")]
public sealed class MediaQueryListEvent : Event, IRealmBoundPlatformObject
{
    /// <summary>
    /// Creates an event of the given type with the media and matching state of the initialization dictionary.
    /// </summary>
    [IdlMember("constructor", "https://drafts.csswg.org/cssom-view/#dom-mediaquerylistevent-mediaquerylistevent")]
    public MediaQueryListEvent(string type, MediaQueryListEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        Media = eventInitDict?.Media ?? "";
        Matches = eventInitDict?.Matches ?? false;
    }

    /// <summary>The window in whose realm the event is created (the window of the media query list), or null for the current realm.</summary>
    internal Window? RealmWindow { get; init; }

    Window? IRealmBoundPlatformObject.RealmWindow => RealmWindow;

    /// <summary>Gets the serialized media query list that changed.</summary>
    [IdlMember("media", "https://drafts.csswg.org/cssom-view/#dom-mediaquerylistevent-media")]
    public string Media { get; }

    /// <summary>Gets a value indicating whether the document matches the media query list.</summary>
    [IdlMember("matches", "https://drafts.csswg.org/cssom-view/#dom-mediaquerylistevent-matches")]
    public bool Matches { get; }
}
