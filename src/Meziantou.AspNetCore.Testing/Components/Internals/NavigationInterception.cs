using Meziantou.AspNetCore.Testing.Components.Navigation;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.AspNetCore.Components.Routing;

namespace Meziantou.AspNetCore.Testing.Components.Internals;

/// <summary>
/// Intercepts the clicks on the links of the base URI once the router enables it, as <c>NavigationManager.ts</c> of Blazor
/// does, and scrolls to the fragment of the URL.
/// </summary>
internal sealed class NavigationInterception(TestNavigationManager navigationManager, Func<BrowsingContext> browsingContext, Func<EventLoopDispatcher> dispatcher) : INavigationInterception, IScrollToLocationHash
{
    private bool _enabled;

    public Task EnableNavigationInterceptionAsync()
    {
        if (!_enabled)
        {
            _enabled = true;

            // The listener of the window runs after the event handlers of the components (on the document), so that
            // @onclick:preventDefault cancels the navigation
            browsingContext().Window.AddEventListener("click", OnClick);
        }

        return Task.CompletedTask;
    }

    public Task RefreshScrollPositionForHash(string locationAbsolute)
    {
        var fragmentIndex = locationAbsolute.IndexOf('#', StringComparison.Ordinal);
        if (fragmentIndex >= 0 && browsingContext().Document.GetElementById(Uri.UnescapeDataString(locationAbsolute[(fragmentIndex + 1)..])) is { } element)
        {
            _ = element.ScrollIntoView();
        }

        return Task.CompletedTask;
    }

    private void OnClick(Event domEvent)
    {
        if (domEvent is not MouseEvent { Button: 0, CtrlKey: false, ShiftKey: false, AltKey: false, MetaKey: false } || domEvent.DefaultPrevented)
            return;

        var anchor = (domEvent.Target as Node) switch
        {
            Element element => element.Closest("a[href], area[href]"),
            { ParentElement: { } parent } => parent.Closest("a[href], area[href]"),
            _ => null,
        };

        if (anchor is null || anchor.HasAttribute("download"))
            return;

        var target = anchor.GetAttribute("target");
        if (!string.IsNullOrEmpty(target) && !string.Equals(target, "_self", StringComparison.OrdinalIgnoreCase))
            return;

        var href = anchor.GetAttribute("href")!;
        var absoluteUri = navigationManager.ToAbsoluteUri(href).AbsoluteUri;
        if (!absoluteUri.StartsWith(navigationManager.BaseUri, StringComparison.Ordinal))
            return;

        domEvent.PreventDefault();
        using (dispatcher().Enter())
        {
            navigationManager.NavigateFromLink(absoluteUri);
        }
    }
}
