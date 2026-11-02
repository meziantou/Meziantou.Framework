namespace Meziantou.Framework.JavaScriptEngine.Samples;

#region JavaScriptEngine_HostEventLoop
/// <summary>
/// An event loop that gives <c>setTimeout</c>, <c>clearTimeout</c> and <c>queueMicrotask</c> to the scripts, and runs the
/// timers and the jobs on the thread that calls <see cref="RunAsync"/>.
/// </summary>
public sealed class HostEventLoop(JsEngine engine, TimeProvider timeProvider)
{
    private readonly PriorityQueue<Timer, (DateTimeOffset DueTime, long Order)> _timers = new();
    private readonly HashSet<int> _activeTimers = [];
    private int _lastTimerId;
    private long _order;

    /// <summary>Adds the functions to the global object of the realm. Call it inside a scope of the engine.</summary>
    public void Install(JsRealm realm)
    {
        var global = realm.GlobalObject;
        global.SetProperty("setTimeout", realm.CreateFunction("setTimeout", 2, (_, arguments) =>
        {
            if (arguments.Length is 0 || !arguments[0].IsCallable)
                throw realm.CreateException(JsErrorType.TypeError, "The callback must be a function");

            var delay = arguments.Length > 1 ? engine.ToNumber(arguments[1]) : 0;
            var id = ++_lastTimerId;
            var dueTime = timeProvider.GetUtcNow() + TimeSpan.FromMilliseconds(double.IsFinite(delay) ? Math.Max(0, delay) : 0);
            _activeTimers.Add(id);
            _timers.Enqueue(new Timer(id, arguments[0], arguments.Length > 2 ? arguments[2..].ToArray() : []), (dueTime, _order++));
            return id;
        }));

        global.SetProperty("clearTimeout", realm.CreateFunction("clearTimeout", 1, (_, arguments) =>
        {
            if (arguments.Length > 0 && arguments[0].IsNumber)
            {
                _activeTimers.Remove((int)arguments[0].AsNumber());
            }

            return JsValue.Undefined;
        }));

        // A microtask is a job of the engine: it runs with the jobs of the promises, before the next timer
        global.SetProperty("queueMicrotask", realm.CreateFunction("queueMicrotask", 1, (_, arguments) =>
        {
            if (arguments.Length is 0 || !arguments[0].IsCallable)
                throw realm.CreateException(JsErrorType.TypeError, "The callback must be a function");

            var callback = arguments[0];
            engine.EnqueueJob(() => engine.Call(callback, JsValue.Undefined));
            return JsValue.Undefined;
        }));
    }

    /// <summary>Runs the jobs, then each timer when it is due, followed by the jobs it starts, until no timer is left.</summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        engine.RunJobs();
        while (_timers.TryPeek(out _, out var priority))
        {
            var delay = priority.DueTime - timeProvider.GetUtcNow();
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, timeProvider, cancellationToken);
            }

            var timer = _timers.Dequeue();
            if (_activeTimers.Remove(timer.Id))
            {
                engine.Call(timer.Callback, JsValue.Undefined, timer.Arguments);
                engine.RunJobs();
            }
        }
    }

    private sealed record Timer(int Id, JsValue Callback, JsValue[] Arguments);
}
#endregion
