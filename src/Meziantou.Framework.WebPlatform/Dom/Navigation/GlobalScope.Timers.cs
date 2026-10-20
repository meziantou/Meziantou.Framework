using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The <c>Function</c> callback function type (https://webidl.spec.whatwg.org/#Function): <c>any (any... arguments)</c>.</summary>
/// <seealso href="https://webidl.spec.whatwg.org/#Function"/>
[IdlInterface("Function", "https://webidl.spec.whatwg.org/#Function")]
public delegate object? FunctionCallback(params object?[] arguments);

/// <summary>https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#framerequestcallback</summary>
/// <param name="time">The time of the frame, in milliseconds relative to the time origin of the global object (see <see cref="Performance.Now"/>).</param>
/// <seealso href="https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#framerequestcallback"/>
[IdlInterface("FrameRequestCallback", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#framerequestcallback")]
public delegate void FrameRequestCallback(double time);

/// <summary>
/// The handler of <see cref="GlobalScope.SetTimeout(TimerHandler, int, object?[])"/> and <see cref="GlobalScope.SetInterval(TimerHandler, int, object?[])"/>
/// (https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#timerhandler): a callback, or the source text of a
/// script (a string or a <see cref="TrustedScript"/>), which only runs when the browsing context has a script engine.
/// </summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#timerhandler"/>
[IdlInterface("TimerHandler", "https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#timerhandler")]
public readonly struct TimerHandler : IUnionValue, IEquatable<TimerHandler>
{
    private TimerHandler(object value) => Value = value;

    /// <summary>Gets the value: a <see cref="FunctionCallback"/>, an <see cref="Action"/>, a <see cref="string"/> or a <see cref="TrustedScript"/>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Converts a callback function to a timer handler.</summary>
    [NotInIdl]
    public static implicit operator TimerHandler(FunctionCallback value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts a .NET delegate to a timer handler.</summary>
    [NotInIdl]
    public static implicit operator TimerHandler(Action value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts the source text of a script to a timer handler.</summary>
    [NotInIdl]
    public static implicit operator TimerHandler(string value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts a trusted script to a timer handler.</summary>
    [NotInIdl]
    public static implicit operator TimerHandler(TrustedScript value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates a timer handler from a callback function.</summary>
    [NotInIdl]
    public static TimerHandler FromFunction(FunctionCallback value) => value;

    /// <summary>Creates a timer handler from a .NET delegate.</summary>
    [NotInIdl]
    public static TimerHandler FromAction(Action value) => value;

    /// <summary>Creates a timer handler from the source text of a script.</summary>
    [NotInIdl]
    public static TimerHandler FromString(string value) => value;

    /// <summary>Creates a timer handler from a trusted script.</summary>
    [NotInIdl]
    public static TimerHandler FromTrustedScript(TrustedScript value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(TimerHandler other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is TimerHandler other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <inheritdoc/>
    [NotInIdl]
    public override string? ToString() => Value?.ToString();

    /// <summary>Returns whether the two timer handlers hold equal values.</summary>
    [NotInIdl]
    public static bool operator ==(TimerHandler left, TimerHandler right) => left.Equals(right);

    /// <summary>Returns whether the two timer handlers hold different values.</summary>
    [NotInIdl]
    public static bool operator !=(TimerHandler left, TimerHandler right) => !left.Equals(right);
}

public abstract partial class GlobalScope
{
    private readonly Dictionary<int, object> _activeTimers = [];
    private int _nextTimerId;

    // ----------------------------------------------------------------------------------------------------------------
    // Timers (https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#timers)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#dom-settimeout: runs <paramref name="handler"/>
    /// once, from a task, after <paramref name="timeout"/> milliseconds (measured with <see cref="DomEventLoop.TimeProvider"/>).
    /// </summary>
    /// <param name="handler">A callback, or the source text of a script, which only runs when the browsing context has a script engine.</param>
    /// <param name="timeout">The delay in milliseconds.</param>
    /// <param name="arguments">The arguments of the callback.</param>
    /// <returns>The identifier of the timer, for <see cref="ClearTimeout"/>.</returns>
    /// <seealso href="https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#dom-settimeout"/>
    [IdlMember("setTimeout", "https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#dom-settimeout")]
    public int SetTimeout(TimerHandler handler, int timeout = 0, params object?[] arguments) => InitializeTimer(handler, timeout, arguments, repeat: false, previousId: null);

    /// <summary>https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#dom-setinterval: runs <paramref name="handler"/> every <paramref name="timeout"/> milliseconds.</summary>
    /// <param name="handler">A callback, or the source text of a script, which only runs when the browsing context has a script engine.</param>
    /// <param name="timeout">The interval in milliseconds.</param>
    /// <param name="arguments">The arguments of the callback.</param>
    /// <returns>The identifier of the timer, for <see cref="ClearInterval"/>.</returns>
    /// <seealso href="https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#dom-setinterval"/>
    [IdlMember("setInterval", "https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#dom-setinterval")]
    public int SetInterval(TimerHandler handler, int timeout = 0, params object?[] arguments) => InitializeTimer(handler, timeout, arguments, repeat: true, previousId: null);

    /// <summary>Runs a .NET callback once, after a delay (see <see cref="SetTimeout(TimerHandler, int, object?[])"/>).</summary>
    [NotInIdl("A .NET overload of setTimeout()")]
    public int SetTimeout(Action handler, int timeout = 0) => SetTimeout((TimerHandler)handler, timeout);

    /// <summary>Runs a .NET callback repeatedly (see <see cref="SetInterval(TimerHandler, int, object?[])"/>).</summary>
    [NotInIdl("A .NET overload of setInterval()")]
    public int SetInterval(Action handler, int timeout = 0) => SetInterval((TimerHandler)handler, timeout);

    /// <summary>https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#dom-cleartimeout</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#dom-cleartimeout"/>
    [IdlMember("clearTimeout", "https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#dom-cleartimeout")]
    public void ClearTimeout(int id = 0) => _activeTimers.Remove(id);

    /// <summary>https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#dom-clearinterval</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#dom-clearinterval"/>
    [IdlMember("clearInterval", "https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#dom-clearinterval")]
    public void ClearInterval(int id = 0) => _activeTimers.Remove(id);

    /// <summary>https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#timer-initialisation-steps</summary>
    private int InitializeTimer(TimerHandler handler, int timeout, object?[] arguments, bool repeat, int? previousId)
    {
        if (handler.Value is null)
            throw new ArgumentNullException(nameof(handler));

        ArgumentNullException.ThrowIfNull(arguments);

        // The string handlers are checked when the timer is created, as in the browsers (the HTML Standard runs the checks in
        // the task of the timer, where the exceptions are reported): the exception of Trusted Types is thrown to the caller,
        // and a string whose compilation is blocked by the content security policies of the global object creates no timer
        // (the violation is reported, and the method returns 0)
        if (previousId is null && handler.Value is string or TrustedScript)
        {
            var source = GetCompliantTimerHandlerSource(handler.Value, repeat);
            handler = source;
            try
            {
                ContentSecurityPolicyEval.EnsureCanCompileStrings(this, [], source, source, StringCompilationType.Timer, [], bodyArg: null);
            }
            catch (EvalErrorException)
            {
                return 0;
            }
        }

        int id;
        if (previousId is { } existingId)
        {
            id = existingId;
        }
        else
        {
            do
            {
                id = _nextTimerId = _nextTimerId == int.MaxValue ? 1 : _nextTimerId + 1;
            }
            while (_activeTimers.ContainsKey(id));
        }

        var eventLoop = EventLoop;
        var nestingLevel = eventLoop.CurrentTimerNestingLevel;
        if (timeout < 0)
        {
            timeout = 0;
        }

        if (nestingLevel > 5 && timeout < 4)
        {
            timeout = 4;
        }

        // https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#timer-initialisation-steps: the base URL of the
        // initiating script (the active script), else the API base URL of the global object
        var baseUrl = ScriptEngine?.GetActiveScriptBaseUrl() ?? ApiBaseUrl.Serialize();

        // The fetch options of a string handler: the nonce, the credentials mode and the referrer policy of the initiating script.
        // The initiating script is unknown when the engine does not track the active script (e.g. in a promise job).
        var fetchOptions = ScriptEngine switch
        {
            { } engine when engine.GetActiveScriptFetchOptions() is { } initiatingOptions => new ModuleFetchOptions { Nonce = initiatingOptions.Nonce, IncludeCredentials = initiatingOptions.IncludeCredentials, ReferrerPolicy = initiatingOptions.ReferrerPolicy },
            { IsScriptRunning: true } => new ModuleFetchOptions { IsUnknownReferrer = true },
            _ => null,
        };

        var uniqueHandle = new object();
        void Task()
        {
            if (!_activeTimers.TryGetValue(id, out var handle) || !ReferenceEquals(handle, uniqueHandle))
                return;

            var previousNestingLevel = eventLoop.CurrentTimerNestingLevel;
            eventLoop.CurrentTimerNestingLevel = nestingLevel + 1;
            try
            {
                RunTimerHandler(handler, arguments, baseUrl, fetchOptions);
            }
            finally
            {
                eventLoop.CurrentTimerNestingLevel = previousNestingLevel;
            }

            if (!_activeTimers.TryGetValue(id, out handle) || !ReferenceEquals(handle, uniqueHandle))
                return;

            if (repeat)
            {
                InitializeTimer(handler, timeout, arguments, repeat: true, id);
            }
            else
            {
                _activeTimers.Remove(id);
            }
        }

        // https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#run-steps-after-a-timeout: the timers that
        // have the same timeout run in the order they were created
        eventLoop.QueueTimer(TimeSpan.FromMilliseconds(timeout), () => QueueGlobalTask(Task));
        _activeTimers[id] = uniqueHandle;
        return id;
    }

    private void RunTimerHandler(TimerHandler handler, object?[] arguments, string baseUrl, ModuleFetchOptions? fetchOptions)
    {
        switch (handler.Value)
        {
            case FunctionCallback function:
                try
                {
                    function(arguments);
                }
#pragma warning disable CA1031 // The exceptions of the callback are reported
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    ScriptErrors.Report(ex, this);
                }

                break;

            case Action action:
                try
                {
                    action();
                }
#pragma warning disable CA1031 // The exceptions of the callback are reported
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    ScriptErrors.Report(ex, this);
                }

                break;

            default:
                // A string handler only runs when scripting is enabled
                if (ScriptEngine is not { } engine || !IsScriptingEnabled)
                    return;

                // The handler was checked by Trusted Types and by the content security policies when the timer was created
                var source = handler.Value as string ?? ((TrustedScript)handler.Value).Data;
                var script = fetchOptions is null ? new ClassicScript(this, source, baseUrl) { Url = ScriptUrl } : new ClassicScript(this, source, baseUrl) { Url = ScriptUrl, FetchOptions = fetchOptions };
                ScriptExecution.RunClassicScript(engine, script);
                break;
        }
    }

    /// <summary>
    /// Gets the source text of a string handler of <c>setTimeout()</c> or <c>setInterval()</c>, checked by Trusted Types
    /// (https://w3c.github.io/trusted-types/dist/spec/#get-trusted-type-compliant-string-algorithm) with the sink
    /// <c>Window setTimeout</c> or <c>WorkerGlobalScope setTimeout</c> (https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#timer-initialisation-steps).
    /// </summary>
    private string GetCompliantTimerHandlerSource(object handler, bool repeat)
    {
        var document = EnvironmentDocument;
        var sink = (this is Window ? "Window " : "WorkerGlobalScope ") + (repeat ? "setInterval" : "setTimeout");
        return handler is TrustedScript trustedScript
            ? TrustedTypesAlgorithms.GetCompliantString(TrustedTypeName.TrustedScript, document, trustedScript, sink)
            : TrustedTypesAlgorithms.GetCompliantString(TrustedTypeName.TrustedScript, document, (string)handler, sink);
    }
}
