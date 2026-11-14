using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using NavigationOptions = Microsoft.AspNetCore.Components.NavigationOptions;

namespace Meziantou.AspNetCore.Testing.Components.Navigation;

/// <summary>
/// The <see cref="NavigationManager"/> of a <see cref="ComponentTestHost"/>. The navigations within the base URI update the
/// URL of the document (<c>history.pushState()</c>) and notify the router; the other navigations are recorded in
/// <see cref="History"/> without leaving the page.
/// </summary>
public sealed class TestNavigationManager : NavigationManager
{
    private readonly List<NavigationHistoryEntry> _history = [];
    private Func<BrowsingContext?>? _browsingContext;

    internal TestNavigationManager()
    {
    }

    /// <summary>Gets the navigations, in the order they were requested.</summary>
    public IReadOnlyList<NavigationHistoryEntry> History => _history;

    internal void Initialize(string baseUri, string uri, Func<BrowsingContext?> browsingContext)
    {
        _browsingContext = browsingContext;
        Initialize(baseUri, uri);
    }

    internal void NavigateFromLink(string uri) => _ = PerformNavigationAsync(uri, new NavigationOptions(), isInterceptedLink: true);

    /// <inheritdoc />
    protected override void NavigateToCore(string uri, NavigationOptions options) => _ = PerformNavigationAsync(uri, options, isInterceptedLink: false);

    private async Task PerformNavigationAsync(string uri, NavigationOptions options, bool isInterceptedLink)
    {
        var absoluteUri = ToAbsoluteUri(uri).AbsoluteUri;
        var shouldContinue = await NotifyLocationChangingAsync(absoluteUri, options.HistoryEntryState, isInterceptedLink).ConfigureAwait(true);
        if (!shouldContinue)
            return;

        _history.Add(new NavigationHistoryEntry(absoluteUri, options, isInterceptedLink));
        var isInternal = absoluteUri.StartsWith(BaseUri, StringComparison.Ordinal);
        if (isInternal && !options.ForceLoad && _browsingContext?.Invoke() is { } context)
        {
            if (options.ReplaceHistoryEntry)
            {
                context.Window.History.ReplaceState(data: null, "", absoluteUri);
            }
            else
            {
                context.Window.History.PushState(data: null, "", absoluteUri);
            }
        }

        Uri = absoluteUri;
        HistoryEntryState = options.HistoryEntryState;
        NotifyLocationChanged(isInterceptedLink);
    }

    /// <inheritdoc />
    protected override void HandleLocationChangingHandlerException(Exception ex, LocationChangingContext context) => throw ex;
}
