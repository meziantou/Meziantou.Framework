namespace Meziantou.Framework.JavaScriptEngine.WebApis;

/// <summary>
/// The web APIs installed in a realm by <see cref="JsWebApis.Install"/>: it holds their state (the active timers) and runs
/// the event loop of the realm.
/// </summary>
public sealed class JsWebApiHost
{
    private readonly Dictionary<int, TimerState> _timers = [];
    private int _lastTimerId;
    private int _timerNestingLevel;

    internal JsWebApiHost(JsRealm realm, JsWebApisOptions options)
    {
        Realm = realm;
        Options = options;
    }

    /// <summary>Gets the realm.</summary>
    public JsRealm Realm { get; }

    internal JsWebApisOptions Options { get; }

    /// <summary>Gets the number of timers (<c>setTimeout</c>, <c>setInterval</c>) that are not cleared and still have to run.</summary>
    public int ActiveTimerCount => _timers.Count;

    /// <summary>
    /// Runs the event loop of the engine until there is nothing left to run: the jobs (<see cref="JsEngine.RunJobs"/>),
    /// then each timer of the realm once its delay has elapsed (as measured by <see cref="JsEngineOptions.TimeProvider"/>),
    /// until no timer is active. An interval keeps the loop running until it is cleared.
    /// </summary>
    /// <param name="cancellationToken">A token to stop waiting for the timers.</param>
    /// <exception cref="JsException">A job or a timer threw an exception (the other ones stay in the queue).</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
    public async Task RunUntilIdleAsync(CancellationToken cancellationToken = default)
    {
        var engine = Realm.Engine;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            engine.RunJobs();
            if (_timers.Count == 0)
                return;

            await engine.WaitForJobsAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The timer initialization steps (https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#timer-initialisation-steps).</summary>
    internal int SetTimer(JsValue handler, JsValue timeout, JsValue[] arguments, bool repeat)
    {
        var milliseconds = ToWebIdlLong(Realm.Engine.ToNumber(timeout));
        var id = AllocateTimerId();
        var timer = new TimerState(id, handler, milliseconds, arguments, repeat);
        _timers.Add(id, timer);
        Schedule(timer, _timerNestingLevel);
        return id;
    }

    internal void ClearTimer(JsValue id)
    {
        var timerId = ToWebIdlLong(Realm.Engine.ToNumber(id));
        if (_timers.Remove(timerId, out var timer))
        {
            timer.Job?.Cancel();
        }
    }

    private void Schedule(TimerState timer, int nestingLevel)
    {
        var timeout = timer.Timeout < 0 ? 0 : timer.Timeout;
        if (nestingLevel > 5 && timeout < 4)
        {
            timeout = 4;
        }

        var taskNestingLevel = nestingLevel + 1;
        timer.Job = Realm.Engine.EnqueueTimeoutJob(() => Run(timer, taskNestingLevel), TimeSpan.FromMilliseconds(timeout), Realm);
    }

    private void Run(TimerState timer, int nestingLevel)
    {
        if (!_timers.TryGetValue(timer.Id, out var current) || !ReferenceEquals(current, timer))
            return;

        var previousNestingLevel = _timerNestingLevel;
        _timerNestingLevel = nestingLevel;
        try
        {
            Realm.Engine.Call(timer.Handler, Realm.GlobalThis, timer.Arguments);
        }
        finally
        {
            _timerNestingLevel = previousNestingLevel;

            // The callback can clear the timer, or (for an interval) the timer runs again with the same id
            if (_timers.TryGetValue(timer.Id, out current) && ReferenceEquals(current, timer))
            {
                if (timer.Repeat)
                {
                    Schedule(timer, nestingLevel);
                }
                else
                {
                    _timers.Remove(timer.Id);
                }
            }
        }
    }

    private int AllocateTimerId()
    {
        // The ids are positive and not reused while a timer is active
        do
        {
            _lastTimerId = _lastTimerId == int.MaxValue ? 1 : _lastTimerId + 1;
        }
        while (_timers.ContainsKey(_lastTimerId));

        return _lastTimerId;
    }

    /// <summary>The conversion of a number to the WebIDL <c>long</c> type (https://webidl.spec.whatwg.org/#es-long).</summary>
    internal static int ToWebIdlLong(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 0;

        var modulo = Math.Truncate(value) % 4294967296.0;
        if (modulo < 0)
        {
            modulo += 4294967296.0;
        }

        return unchecked((int)(uint)modulo);
    }

    private sealed class TimerState(int id, JsValue handler, int timeout, JsValue[] arguments, bool repeat)
    {
        public int Id { get; } = id;

        public JsValue Handler { get; } = handler;

        public int Timeout { get; } = timeout;

        public JsValue[] Arguments { get; } = arguments;

        public bool Repeat { get; } = repeat;

        public JsTimeoutJob? Job { get; set; }
    }
}
