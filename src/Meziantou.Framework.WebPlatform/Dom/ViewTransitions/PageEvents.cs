namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#pageswapeventinit</summary>
[IdlInterface("PageSwapEventInit", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#pageswapeventinit")]
public sealed class PageSwapEventInit : EventInit
{
    /// <summary>Gets the activation of the navigation, or null for a cross-origin navigation.</summary>
    [IdlMember("activation", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-pageswapeventinit-activation")]
    public NavigationActivation? Activation { get; init; }

    /// <summary>Gets the view transition of the navigation, or null.</summary>
    [IdlMember("viewTransition", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-pageswapeventinit-viewtransition")]
    public ViewTransition? ViewTransition { get; init; }
}

/// <summary>
/// https://html.spec.whatwg.org/multipage/nav-history-apis.html#the-pageswapevent-interface: the <c>pageswap</c> event, fired at
/// the window of a document that is about to be replaced by a navigation.
/// </summary>
[IdlInterface("PageSwapEvent", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#pageswapevent")]
public sealed class PageSwapEvent : Event
{
    /// <summary>Creates a page swap event with the given type and initialization dictionary.</summary>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-pageswapevent-pageswapevent")]
    public PageSwapEvent(string type, PageSwapEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        Activation = eventInitDict?.Activation;
        ViewTransition = eventInitDict?.ViewTransition;
    }

    /// <summary>Gets the activation of the navigation (the entries and the type of the navigation), or null for a cross-origin navigation.</summary>
    [IdlMember("activation", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-pageswapevent-activation")]
    public NavigationActivation? Activation { get; }

    /// <summary>Gets the view transition of the navigation, or null when the navigation has no cross-document view transition.</summary>
    [IdlMember("viewTransition", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-pageswapevent-viewtransition")]
    public ViewTransition? ViewTransition { get; }
}

/// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#pagerevealeventinit</summary>
[IdlInterface("PageRevealEventInit", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#pagerevealeventinit")]
public sealed class PageRevealEventInit : EventInit
{
    /// <summary>Gets the view transition of the navigation, or null.</summary>
    [IdlMember("viewTransition", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-pagerevealeventinit-viewtransition")]
    public ViewTransition? ViewTransition { get; init; }
}

/// <summary>
/// https://html.spec.whatwg.org/multipage/nav-history-apis.html#the-pagerevealevent-interface: the <c>pagereveal</c> event, fired
/// at the window of a document at its first rendering opportunity after it was activated.
/// </summary>
[IdlInterface("PageRevealEvent", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#pagerevealevent")]
public sealed class PageRevealEvent : Event
{
    /// <summary>Creates a page reveal event with the given type and initialization dictionary.</summary>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-pagerevealevent-pagerevealevent")]
    public PageRevealEvent(string type, PageRevealEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        ViewTransition = eventInitDict?.ViewTransition;
    }

    /// <summary>Gets the inbound view transition of the navigation, or null when the navigation has no cross-document view transition.</summary>
    [IdlMember("viewTransition", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-pagerevealevent-viewtransition")]
    public ViewTransition? ViewTransition { get; }
}
