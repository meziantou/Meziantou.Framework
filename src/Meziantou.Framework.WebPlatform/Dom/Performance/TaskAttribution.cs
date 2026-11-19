namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The attribution of the tasks to the scripts that started them, as the task attribution of Chromium: the URL of the script
/// that runs (a classic script, a module script, the script of a worker) is set while it runs, and propagated, as the
/// interaction contexts of the soft navigations (<see cref="InteractionContext"/>), to the tasks and the callbacks scheduled
/// from it: the timers, the tasks and the microtasks, the promise reactions, the animation frame and idle callbacks, the
/// tasks of the schedulers, and the asynchronous operations of the library. It is the initiator URL of the fetches
/// (<see cref="PerformanceResourceTiming.InitiatorUrl"/>).
/// </summary>
internal static class TaskAttribution
{
    // An AsyncLocal flows with the .NET asynchronous operations (e.g. the fetch of an XMLHttpRequest whose events are queued
    // when it completes). It is null outside of the tasks attributed to a script.
    private static readonly AsyncLocal<string?> CurrentScriptUrl = new();

    // Whether a script was ever attributed: until then, the lookups of the AsyncLocal are skipped
    private static volatile bool s_used;

    /// <summary>Gets the URL of the script to which the running task is attributed, or null.</summary>
    public static string? Current => s_used ? CurrentScriptUrl.Value : null;

    /// <summary>Attributes the running code to the script of <paramref name="scriptUrl"/> (null: no script) until the scope is disposed.</summary>
    public static Scope Enter(string? scriptUrl)
    {
        if (scriptUrl is not null)
        {
            s_used = true;
        }
        else if (!s_used)
        {
            return default;
        }

        var previous = CurrentScriptUrl.Value;
        if (!string.Equals(previous, scriptUrl, StringComparison.Ordinal))
        {
            CurrentScriptUrl.Value = scriptUrl;
        }

        return new Scope(previous, scriptUrl, active: true);
    }

    /// <summary>Returns a callback that runs attributed to the current script, or the callback itself when there is none.</summary>
    public static Action Wrap(Action callback)
    {
        if (Current is not { } scriptUrl)
            return callback;

        return () =>
        {
            using var scope = Enter(scriptUrl);
            callback();
        };
    }

    /// <summary>Returns a callback that runs attributed to the current script, or the callback itself when there is none.</summary>
    public static Action<T> Wrap<T>(Action<T> callback)
    {
        if (Current is not { } scriptUrl)
            return callback;

        return value =>
        {
            using var scope = Enter(scriptUrl);
            callback(value);
        };
    }

    /// <summary>The scope of <see cref="Enter"/>: it restores the previous attribution.</summary>
    internal readonly struct Scope(string? previous, string? current, bool active) : IDisposable
    {
        public void Dispose()
        {
            if (active && !string.Equals(previous, current, StringComparison.Ordinal))
            {
                CurrentScriptUrl.Value = previous;
            }
        }
    }
}
