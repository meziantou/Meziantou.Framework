namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://dom.spec.whatwg.org/#interface-abortcontroller</summary>
[IdlInterface("AbortController", "https://dom.spec.whatwg.org/#abortcontroller")]
public class AbortController
{
    /// <summary>Creates a controller with a new signal.</summary>
    [IdlMember("constructor", "https://dom.spec.whatwg.org/#dom-abortcontroller-abortcontroller")]
    public AbortController()
    {
        Signal = new AbortSignal();
    }

    /// <summary>Creates a controller of a signal of an interface that inherits from <see cref="AbortSignal"/> (e.g. a <see cref="TaskSignal"/>).</summary>
    private protected AbortController(AbortSignal signal)
    {
        Signal = signal;
    }

    /// <summary>Gets the signal controlled by the controller.</summary>
    [IdlMember("signal", "https://dom.spec.whatwg.org/#dom-abortcontroller-signal")]
    public AbortSignal Signal { get; }

    /// <summary>https://dom.spec.whatwg.org/#dom-abortcontroller-abort</summary>
    [IdlMember("abort", "https://dom.spec.whatwg.org/#dom-abortcontroller-abort")]
    public void Abort(object? reason = null) => Signal.SignalAbort(reason);
}

/// <summary>https://dom.spec.whatwg.org/#interface-AbortSignal</summary>
[IdlInterface("AbortSignal", "https://dom.spec.whatwg.org/#abortsignal")]
public class AbortSignal : EventTarget
{
    private const int MinDependentSignalsPruneThreshold = 8;

    private readonly List<Action> _abortAlgorithms = [];

    // https://dom.spec.whatwg.org/#abortsignal-source-signals and https://dom.spec.whatwg.org/#abortsignal-dependent-signals are weak sets:
    // a source signal that is collected can no longer be aborted, and a dependent signal that is collected can no longer be observed
    private List<WeakReference<AbortSignal>>? _sourceSignals;
    private List<WeakReference<AbortSignal>>? _dependentSignals;
    private int _dependentSignalsPruneThreshold = MinDependentSignalsPruneThreshold;

    // The dependent signals that must not be garbage collected (https://dom.spec.whatwg.org/#abort-signal-garbage-collection)
    private HashSet<AbortSignal>? _retainedDependentSignals;
    private bool _retainedBySourceSignals;
    private bool _dependent;

    internal AbortSignal()
    {
    }

    /// <summary>The abort reason; null when the signal is not aborted.</summary>
    internal object? AbortReason { get; private set; }

    /// <summary>Gets a value indicating whether the signal has been aborted.</summary>
    [IdlMember("aborted", "https://dom.spec.whatwg.org/#dom-abortsignal-aborted")]
    public bool Aborted => AbortReason is not null;

    /// <summary>Gets the reason the signal was aborted, or null when it is not aborted.</summary>
    [IdlMember("reason", "https://dom.spec.whatwg.org/#dom-abortsignal-reason")]
    public object? Reason => AbortReason;

    /// <summary>Gets or sets the event handler of the <c>abort</c> event.</summary>
    [IdlMember("onabort", "https://dom.spec.whatwg.org/#dom-abortsignal-onabort")]
    public Action<Event>? OnAbort
    {
        get => GetEventHandler("abort");
        set => SetEventHandler("abort", value);
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-abortsignal-abort</summary>
    [IdlMember("abort", "https://dom.spec.whatwg.org/#dom-abortsignal-abort")]
    public static AbortSignal Abort(object? reason = null)
    {
        var signal = new AbortSignal
        {
            AbortReason = reason ?? new DomException("The operation was aborted", "AbortError"),
        };
        return signal;
    }

    /// <summary>
    /// https://dom.spec.whatwg.org/#dom-abortsignal-timeout: the timer runs on the event loop of the current global object
    /// (the window or the worker of the running script). Without one, the signal is never aborted: use <see cref="Timeout(GlobalScope, ulong)"/>.
    /// </summary>
    [IdlMember("timeout", "https://dom.spec.whatwg.org/#dom-abortsignal-timeout")]
    public static AbortSignal Timeout(ulong milliseconds)
    {
        var signal = new AbortSignal();
        if (signal.RelevantGlobal is { } window)
        {
            QueueTimeout(signal, window, milliseconds);
        }

        return signal;
    }

    /// <summary>
    /// https://dom.spec.whatwg.org/#dom-abortsignal-timeout: returns a signal aborted after <paramref name="milliseconds"/>,
    /// measured by the timers of the event loop of <paramref name="window"/>.
    /// </summary>
    [NotInIdl("The .NET overload of timeout() whose global object is explicit")]
    public static AbortSignal Timeout(GlobalScope window, ulong milliseconds)
    {
        ArgumentNullException.ThrowIfNull(window);
        var signal = new AbortSignal();
        QueueTimeout(signal, window, milliseconds);
        return signal;
    }

    private static void QueueTimeout(AbortSignal signal, GlobalScope window, ulong milliseconds)
    {
        window.EventLoop.QueueTimer(TimeSpan.FromMilliseconds(milliseconds), () => window.QueueGlobalTask(() => signal.SignalAbort(new DomException("The operation timed out", "TimeoutError"))));
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-abortsignal-any</summary>
    [IdlMember("any", "https://dom.spec.whatwg.org/#dom-abortsignal-any")]
    public static AbortSignal Any(IEnumerable<AbortSignal> signals)
    {
        ArgumentNullException.ThrowIfNull(signals);
        var resultSignal = new AbortSignal();
        InitializeDependentSignal(resultSignal, signals);
        return resultSignal;
    }

    /// <summary>
    /// https://dom.spec.whatwg.org/#create-a-dependent-abort-signal, for a new signal of the interface of the caller
    /// (<see cref="AbortSignal"/> or an interface that inherits from it).
    /// </summary>
    private protected static void InitializeDependentSignal(AbortSignal resultSignal, IEnumerable<AbortSignal> signals)
    {
        var signalList = signals.ToList();
        foreach (var signal in signalList)
        {
            if (signal.Aborted)
            {
                resultSignal.AbortReason = signal.AbortReason;
                return;
            }
        }

        resultSignal._dependent = true;
        var sourceSignals = new HashSet<AbortSignal>(ReferenceEqualityComparer.Instance);
        foreach (var signal in signalList)
        {
            if (!signal._dependent)
            {
                AddSource(resultSignal, signal, sourceSignals);
            }
            else if (signal._sourceSignals is { } signalSourceSignals)
            {
                foreach (var reference in signalSourceSignals)
                {
                    if (reference.TryGetTarget(out var sourceSignal))
                    {
                        AddSource(resultSignal, sourceSignal, sourceSignals);
                    }
                }
            }
        }

        return;

        static void AddSource(AbortSignal resultSignal, AbortSignal sourceSignal, HashSet<AbortSignal> sourceSignals)
        {
            // Source signals and dependent signals are sets
            if (!sourceSignals.Add(sourceSignal))
                return;

            (resultSignal._sourceSignals ??= []).Add(new WeakReference<AbortSignal>(sourceSignal));
            sourceSignal.AddDependentSignal(resultSignal);
        }
    }

    private void AddDependentSignal(AbortSignal dependentSignal)
    {
        // Remove the references to the collected dependent signals, so a long-lived source signal used by many AbortSignal.any() calls does not grow forever
        var dependentSignals = _dependentSignals ??= [];
        if (dependentSignals.Count >= _dependentSignalsPruneThreshold)
        {
            dependentSignals.RemoveAll(static reference => !reference.TryGetTarget(out _));
            _dependentSignalsPruneThreshold = Math.Max(MinDependentSignalsPruneThreshold, dependentSignals.Count * 2);
        }

        dependentSignals.Add(new WeakReference<AbortSignal>(dependentSignal));
    }

    /// <summary>
    /// https://dom.spec.whatwg.org/#abort-signal-garbage-collection: a non-aborted dependent signal must not be garbage collected while its
    /// source signals is non-empty and it has abort event listeners or abort algorithms. The source signals only reference their dependent
    /// signals weakly, so they also reference this signal strongly while it is in that state.
    /// </summary>
    private void UpdateRetentionBySourceSignals()
    {
        if (!_dependent)
            return;

        var retain = !Aborted && (_abortAlgorithms.Count > 0 || HasEventListener("abort"));
        if (retain == _retainedBySourceSignals)
            return;

        _retainedBySourceSignals = retain;
        if (_sourceSignals is null)
            return;

        foreach (var reference in _sourceSignals)
        {
            if (!reference.TryGetTarget(out var sourceSignal))
                continue;

            if (retain)
            {
                (sourceSignal._retainedDependentSignals ??= new HashSet<AbortSignal>(ReferenceEqualityComparer.Instance)).Add(this);
            }
            else
            {
                sourceSignal._retainedDependentSignals?.Remove(this);
            }
        }
    }

    private protected override void OnEventListenerAdded(EventListenerEntry listener)
    {
        if (listener.Type == "abort")
        {
            UpdateRetentionBySourceSignals();
        }
    }

    private protected override void OnEventListenerRemoved(EventListenerEntry listener)
    {
        if (listener.Type == "abort")
        {
            UpdateRetentionBySourceSignals();
        }
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-abortsignal-throwifaborted</summary>
    [IdlMember("throwIfAborted", "https://dom.spec.whatwg.org/#dom-abortsignal-throwifaborted")]
    public void ThrowIfAborted()
    {
        if (!Aborted)
            return;

        throw AbortReason as Exception ?? new AbortSignalException(AbortReason);
    }

    /// <summary>https://dom.spec.whatwg.org/#abortsignal-add</summary>
    internal void AddAbortAlgorithm(Action algorithm)
    {
        if (Aborted)
            return;

        _abortAlgorithms.Add(algorithm);
        UpdateRetentionBySourceSignals();
    }

    /// <summary>https://dom.spec.whatwg.org/#abortsignal-remove</summary>
    internal void RemoveAbortAlgorithm(Action algorithm)
    {
        if (_abortAlgorithms.Remove(algorithm))
        {
            UpdateRetentionBySourceSignals();
        }
    }

    /// <summary>https://dom.spec.whatwg.org/#abortsignal-signal-abort</summary>
    internal void SignalAbort(object? reason)
    {
        if (Aborted)
            return;

        AbortReason = reason ?? new DomException("The operation was aborted", "AbortError");
        var dependentSignalsToAbort = new List<AbortSignal>();
        if (_dependentSignals is { } dependentSignals)
        {
            foreach (var reference in dependentSignals)
            {
                if (reference.TryGetTarget(out var dependentSignal) && !dependentSignal.Aborted)
                {
                    dependentSignal.AbortReason = AbortReason;
                    dependentSignalsToAbort.Add(dependentSignal);
                }
            }
        }

        // All the dependent signals are now aborted: nothing can observe them through this signal anymore
        _dependentSignals = null;
        _retainedDependentSignals = null;
        RunAbortSteps();
        foreach (var dependentSignal in dependentSignalsToAbort)
        {
            dependentSignal.RunAbortSteps();
        }
    }

    /// <summary>https://dom.spec.whatwg.org/#run-the-abort-steps</summary>
    private void RunAbortSteps()
    {
        foreach (var algorithm in _abortAlgorithms.ToArray())
        {
            algorithm();
        }

        _abortAlgorithms.Clear();

        // An aborted dependent signal no longer needs to be kept alive by its other source signals, and is not a dependent of them anymore
        UpdateRetentionBySourceSignals();
        _sourceSignals = null;

        var @event = new Event("abort") { IsTrusted = true };
        EventDispatcher.Dispatch(@event, this);
    }
}

/// <summary>Thrown by <see cref="AbortSignal.ThrowIfAborted"/> when the abort reason is not an exception.</summary>
public sealed class AbortSignalException : Exception
{
    /// <summary>Creates an exception with a default message.</summary>
    public AbortSignalException()
    {
    }

    /// <summary>Creates an exception with the given message.</summary>
    public AbortSignalException(string? message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with the given message and inner exception.</summary>
    public AbortSignalException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    internal AbortSignalException(object? reason)
        : base("The operation was aborted")
    {
        Reason = reason;
    }

    /// <summary>Gets the abort reason.</summary>
    public object? Reason { get; }
}
