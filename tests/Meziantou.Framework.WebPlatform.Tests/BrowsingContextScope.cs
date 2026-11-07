using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests;

/// <summary>Creates the browsing contexts of a test, and disposes them (and their event loops) at the end of the test.</summary>
internal sealed class BrowsingContextScope : IDisposable
{
    private readonly List<BrowsingContext> _contexts = [];

    /// <summary>Gets the event loop of the last browsing context created by the scope.</summary>
    public DomEventLoop EventLoop => _contexts[^1].EventLoop;

    public BrowsingContext Create(BrowsingContextOptions? options = null)
    {
        var context = new BrowsingContext(options);
        _contexts.Add(context);
        return context;
    }

    public void Dispose()
    {
        foreach (var context in _contexts)
        {
            context.Dispose();
        }

        _contexts.Clear();
    }
}
