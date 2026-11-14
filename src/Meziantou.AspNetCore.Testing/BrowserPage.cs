namespace Meziantou.AspNetCore.Testing;

/// <summary>A page of a <see cref="WebAppBrowser"/>: navigate it, act on its elements, and check the result.</summary>
public sealed class BrowserPage : TestPage
{
    internal BrowserPage(WebAppBrowser browser, TestPageSettings settings)
        : base(settings)
    {
        Browser = browser;
    }

    /// <summary>Gets the browser of the page.</summary>
    public WebAppBrowser Browser { get; }

    /// <summary>Navigates to a URL, and waits until the new document is loaded (its <c>load</c> event).</summary>
    /// <param name="url">The URL, relative to <see cref="WebAppBrowser.BaseAddress"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the document is loaded.</returns>
    public Task GoToAsync(string url, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        var absoluteUrl = new Uri(Browser.BaseAddress, url).AbsoluteUri;
        return NavigateAndWaitAsync(() => BrowsingContext.Navigate(absoluteUrl), cancellationToken);
    }

    /// <summary>Goes back in the history, and waits until the page is loaded. Does nothing at the first entry of the history.</summary>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is loaded.</returns>
    public Task GoBackAsync(CancellationToken cancellationToken = default) => NavigateAndWaitAsync(() => BrowsingContext.Window.History.Back(), cancellationToken);

    /// <summary>Goes forward in the history, and waits until the page is loaded. Does nothing at the last entry of the history.</summary>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is loaded.</returns>
    public Task GoForwardAsync(CancellationToken cancellationToken = default) => NavigateAndWaitAsync(() => BrowsingContext.Window.History.Forward(), cancellationToken);

    /// <summary>Reloads the page, and waits until the new document is loaded.</summary>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the document is loaded.</returns>
    public Task ReloadAsync(CancellationToken cancellationToken = default) => NavigateAndWaitAsync(BrowsingContext.Reload, cancellationToken);

    /// <inheritdoc />
    protected override bool IsIdle()
    {
        // The long-lived requests (server-sent events, long polling) never end: the page is idle when the other requests are done
        var eventLoop = BrowsingContext.EventLoop;
        var counters = Browser.GetRequestCounters(BrowsingContext);
        return !eventLoop.HasPendingWork && counters.Requests == 0 && (!eventLoop.HasPendingOperations || counters.LongLivedRequests > 0);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!IsDisposed)
        {
            Browser.OnPageDisposed(this);
        }

        base.Dispose(disposing);
    }

    private async Task NavigateAndWaitAsync(Action navigate, CancellationToken cancellationToken)
    {
        using (EnterEventLoop())
        {
            navigate();
        }

        // The event loop runs the traversals of the history before the condition is evaluated. A navigation is ongoing
        // until its document is active; a traversal may stay in the same document (pushState), or do nothing (no entry).
        await WaitForAsync(() => !BrowsingContext.IsNavigating && Document.ReadyState == Framework.WebPlatform.Dom.DocumentReadyState.Complete, timeout: null, cancellationToken).ConfigureAwait(false);
        await WaitForIdleAsync(timeout: null, cancellationToken).ConfigureAwait(false);
    }
}
