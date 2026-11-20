namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The attribution of the tasks to the scripts that started them, as the task attribution of Chromium: the URL of the script
/// that runs (a classic script, a module script, the script of a worker) is set while it runs, and propagated, as the
/// interaction contexts of the soft navigations (<see cref="InteractionContext"/>), to the tasks and the callbacks scheduled
/// from it: the timers, the tasks and the microtasks, the promise reactions, the animation frame and idle callbacks, the
/// tasks of the schedulers, and the asynchronous operations of the library. It is the initiator URL of the fetches
/// (<see cref="PerformanceResourceTiming.InitiatorUrl"/>). An attribution only applies to the event loop of its script: the
/// task that a window queues on the event loop of a worker (e.g. a message) is not attributed to the script of the window.
/// </summary>
internal static class TaskAttribution
{
    /// <summary>The script to which a task is attributed: its URL, and the event loop that runs it.</summary>
    internal sealed record Attribution(string ScriptUrl, DomEventLoop EventLoop);

    // An AsyncLocal flows with the .NET asynchronous operations (e.g. the fetch of an XMLHttpRequest whose events are queued
    // when it completes). It is null outside of the tasks attributed to a script.
    private static readonly AsyncLocal<Attribution?> CurrentAttribution = new();

    // Whether a script was ever attributed: until then, the lookups of the AsyncLocal are skipped
    private static volatile bool s_used;

    /// <summary>Gets the script to which the running task is attributed, or null.</summary>
    public static Attribution? Current => s_used ? CurrentAttribution.Value : null;

    /// <summary>Gets the URL of the script to which the running task of <paramref name="eventLoop"/> is attributed, or null.</summary>
    public static string? GetScriptUrl(DomEventLoop eventLoop) => Current is { } attribution && ReferenceEquals(attribution.EventLoop, eventLoop) ? attribution.ScriptUrl : null;

    /// <summary>Attributes the running code to the script of <paramref name="scriptUrl"/>, run by <paramref name="eventLoop"/>, until the scope is disposed.</summary>
    public static Scope Enter(string scriptUrl, DomEventLoop eventLoop) => Enter(new Attribution(scriptUrl, eventLoop));

    /// <summary>Attributes the running code to a script (null: no script) until the scope is disposed.</summary>
    public static Scope Enter(Attribution? attribution)
    {
        if (attribution is not null)
        {
            s_used = true;
        }
        else if (!s_used)
        {
            return default;
        }

        var previous = CurrentAttribution.Value;
        if (!Equals(previous, attribution))
        {
            CurrentAttribution.Value = attribution;
        }

        return new Scope(previous, attribution, active: true);
    }

    /// <summary>Returns a callback that runs attributed to the current script, or the callback itself when there is none.</summary>
    public static Action Wrap(Action callback)
    {
        if (Current is not { } attribution)
            return callback;

        return () =>
        {
            using var scope = Enter(attribution);
            callback();
        };
    }

    /// <summary>The scope of <see cref="Enter(Attribution?)"/>: it restores the previous attribution.</summary>
    internal readonly struct Scope(Attribution? previous, Attribution? current, bool active) : IDisposable
    {
        public void Dispose()
        {
            if (active && !Equals(previous, current))
            {
                CurrentAttribution.Value = previous;
            }
        }
    }
}
