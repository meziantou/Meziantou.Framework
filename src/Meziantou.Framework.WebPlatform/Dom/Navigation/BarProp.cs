namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// https://html.spec.whatwg.org/multipage/nav-history-apis.html#barprop: the visibility of a user interface element of the
/// window (<c>window.locationbar</c>, <c>window.menubar</c>...). There is no user interface: for compatibility, the
/// specification makes them all visible, except in a popup (<see cref="BrowsingContext.IsPopup"/>).
/// </summary>
[IdlInterface("BarProp", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#barprop")]
public sealed class BarProp
{
    private readonly Window _window;

    internal BarProp(Window window) => _window = window;

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-barprop-visible: false when the top-level browsing
    /// context of the window is a popup. The specification returns true for a window without browsing context, but the
    /// browsers return false (the window has no user interface anymore), which the web-platform-tests expect.
    /// </summary>
    [IdlMember("visible", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-barprop-visible")]
    public bool Visible => _window.BrowsingContext is { } browsingContext && !browsingContext.Top.IsPopup;
}
