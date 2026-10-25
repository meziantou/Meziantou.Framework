using Meziantou.Framework.WebPlatform.Playwright.Protocol;

namespace Meziantou.Framework.WebPlatform.Playwright.Dispatchers;

// The navigations started by the actions (waitForSignalsCreatedBy of the Playwright server)
internal sealed partial class PageDispatcher
{
    private readonly List<NavigationWatcher> _navigationWatchers = [];

    /// <summary>Starts recording the navigations requested by the frames of the page (e.g. during a click).</summary>
    public NavigationWatcher StartNavigationWatcher()
    {
        var watcher = new NavigationWatcher(this);
        _navigationWatchers.Add(watcher);
        return watcher;
    }

    internal void OnNavigationRequested(FrameDispatcher frame)
    {
        foreach (var watcher in _navigationWatchers)
        {
            watcher.Add(frame);
        }
    }

    internal void RemoveNavigationWatcher(NavigationWatcher watcher) => _navigationWatchers.Remove(watcher);
}

/// <summary>The navigations requested while an action runs: the action waits until their documents are committed.</summary>
internal sealed class NavigationWatcher(PageDispatcher page) : IDisposable
{
    private readonly List<FrameDispatcher> _frames = [];

    public void Add(FrameDispatcher frame)
    {
        if (!_frames.Contains(frame))
        {
            _frames.Add(frame);
        }
    }

    public async Task WaitAsync(Progress progress)
    {
        // The navigations of the action (e.g. the activation of a link) start from the tasks it queued
        await Task.Yield();
        await Task.Yield();
        foreach (var frame in _frames.ToArray())
        {
            if (!frame.IsDetached)
            {
                await frame.WaitForPendingNavigationAsync(progress).ConfigureAwait(true);
            }
        }
    }

    public void Dispose() => page.RemoveNavigationWatcher(this);
}
