namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The steps of a job: a static method that receives the state and the argument captured when the job was created (no closure allocation).</summary>
internal delegate void JsJobAction(object? state, JsValue argument);

/// <summary>
/// A Job Abstract Closure (https://tc39.es/ecma262/#sec-jobs) and the realm it runs in. When <see cref="Realm"/> is not
/// null, it is the current realm while the job runs.
/// </summary>
internal readonly struct JsJob(JsJobAction action, object? state, JsValue argument, JsRealm? realm)
{
    public JsJobAction Action { get; } = action;

    public object? State { get; } = state;

    public JsValue Argument { get; } = argument;

    public JsRealm? Realm { get; } = realm;
}

/// <summary>A JobCallback Record (https://tc39.es/ecma262/#sec-jobcallback-records): a function and the host-defined state captured when it was registered.</summary>
public readonly struct JsJobCallback : IEquatable<JsJobCallback>
{
    /// <summary>Creates a JobCallback Record.</summary>
    public JsJobCallback(JsObject callback, object? hostDefined)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Callback = callback;
        HostDefined = hostDefined;
    }

    /// <summary>Gets the function ([[Callback]]).</summary>
    public JsObject Callback { get; }

    /// <summary>Gets the [[HostDefined]] value, captured by <see cref="JsHost.MakeJobCallback"/>.</summary>
    public object? HostDefined { get; }

    /// <inheritdoc/>
    public bool Equals(JsJobCallback other) => ReferenceEquals(Callback, other.Callback) && Equals(HostDefined, other.HostDefined);

    /// <inheritdoc/>
    public override bool Equals([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] object? obj) => obj is JsJobCallback other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Callback, HostDefined);

    /// <summary>Compares two records.</summary>
    public static bool operator ==(JsJobCallback left, JsJobCallback right) => left.Equals(right);

    /// <summary>Compares two records.</summary>
    public static bool operator !=(JsJobCallback left, JsJobCallback right) => !left.Equals(right);
}

/// <summary>
/// A job that the engine gives to the host to schedule (HostEnqueueGenericJob, HostEnqueueTimeoutJob): the host calls
/// <see cref="Run"/> later, on the thread of the engine (e.g. from a task of its event loop).
/// </summary>
public sealed class JsHostJob
{
    private readonly JsEngine _engine;
    private readonly JsJob _job;

    internal JsHostJob(JsEngine engine, in JsJob job)
    {
        _engine = engine;
        _job = job;
    }

    /// <summary>Gets the realm of the job: the current realm while it runs.</summary>
    public JsRealm? Realm => _job.Realm;

    internal JsJob Job => _job;

    /// <summary>
    /// Runs the job, in its realm. An exception goes to <see cref="JsHost.ReportJobException"/>. The promise jobs it
    /// enqueues are not run: the host runs them afterwards (<see cref="JsEngine.RunJobs"/>, e.g. a microtask checkpoint).
    /// </summary>
    public void Run()
    {
        using var scope = _engine.Enter();
        _engine.RunJob(_job);
    }
}

/// <summary>
/// A handler created by the engine for a promise reaction (e.g. the continuation of an <c>await</c>), which the host calls
/// from <see cref="JsHost.CallContinuation"/> to restore the state it captured with <see cref="JsHost.MakeContinuationHostDefined"/>.
/// </summary>
public readonly ref struct JsContinuation
{
    private readonly PromiseContinuation _continuation;
    private readonly object? _state;
    private readonly JsValue _argument;

    internal JsContinuation(PromiseContinuation continuation, object? state, JsValue argument)
    {
        _continuation = continuation;
        _state = state;
        _argument = argument;
    }

    /// <summary>Runs the handler.</summary>
    public void Invoke() => _continuation(_state, _argument);
}

/// <summary>The operation of HostPromiseRejectionTracker.</summary>
public enum JsPromiseRejectionOperation
{
    /// <summary>A promise was rejected without any handler.</summary>
    Reject,

    /// <summary>A handler was added to a rejected promise that had none.</summary>
    Handle,
}

/// <summary>
/// The host-defined operations of the engine (https://tc39.es/ecma262/#sec-host-hooks-summary). The default
/// implementations are the ones of the specification; a host (e.g. a browser) derives from this class to integrate the
/// engine with its event loop, and sets it in <see cref="JsEngineOptions.Host"/>.
/// </summary>
public class JsHost
{
    /// <summary>Gets the default host hooks.</summary>
    internal static JsHost Default { get; } = new();

    /// <summary>HostPromiseRejectionTracker(promise, operation). The default implementation does nothing.</summary>
    public virtual void PromiseRejectionTracker(JsEngine engine, JsObject promise, JsPromiseRejectionOperation operation)
    {
    }

    /// <summary>HostMakeJobCallback(callback): called when a callback is registered (e.g. by <c>then</c>), to capture a host-defined state (e.g. the incumbent settings object).</summary>
    public virtual JsJobCallback MakeJobCallback(JsEngine engine, JsObject callback) => new(callback, hostDefined: null);

    /// <summary>HostCallJobCallback(jobCallback, V, argumentsList): calls the callback, restoring the host-defined state captured by <see cref="MakeJobCallback"/>.</summary>
    public virtual JsValue CallJobCallback(JsEngine engine, in JsJobCallback jobCallback, JsValue thisValue, ReadOnlySpan<JsValue> arguments) => jobCallback.Callback.Call(thisValue, arguments);

    /// <summary>
    /// HostMakeJobCallback(callback) for the handlers that the engine creates without a function object (the continuations
    /// of <c>await</c>, of the async generators, of the module loading...): returns the [[HostDefined]] value, which
    /// <see cref="CallContinuation"/> restores when the handler runs, or null when there is no state to restore (the
    /// default: the handler is then called directly).
    /// </summary>
    public virtual object? MakeContinuationHostDefined(JsEngine engine) => null;

    /// <summary>
    /// HostCallJobCallback(jobCallback, V, argumentsList) for the handlers created by the engine whose [[HostDefined]] value
    /// (<see cref="MakeContinuationHostDefined"/>) is not null: calls <see cref="JsContinuation.Invoke"/>, restoring the
    /// host-defined state.
    /// </summary>
    public virtual void CallContinuation(JsEngine engine, object hostDefined, JsContinuation continuation) => continuation.Invoke();

    /// <summary>HostEnqueuePromiseJob(job, realm). The default implementation adds the job to the job queue of the engine.</summary>
    internal virtual void EnqueuePromiseJob(JsEngine engine, in JsJob job) => engine.EnqueuePromiseJobCore(job);

    /// <summary>
    /// HostEnqueueGenericJob(job, realm): e.g. the cleanup of a FinalizationRegistry, or the resolution of the promise of
    /// <c>Atomics.waitAsync</c>. It can be called from any thread (the finalizer thread, the thread of another agent). The
    /// default implementation adds the job to the queue of the engine: <see cref="JsEngine.RunJobs"/> runs it.
    /// </summary>
    public virtual void EnqueueGenericJob(JsEngine engine, JsHostJob job)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(job);
        engine.EnqueueGenericJobCore(job.Job);
    }

    /// <summary>
    /// HostEnqueueTimeoutJob(timeoutJob, realm, milliseconds): e.g. the timeout of <c>Atomics.waitAsync</c>. The default
    /// implementation runs the job from <see cref="JsEngine.RunJobs"/> once the delay has elapsed (measured with the
    /// <see cref="JsEngineOptions.TimeProvider"/>).
    /// </summary>
    public virtual void EnqueueTimeoutJob(JsEngine engine, JsHostJob job, TimeSpan delay)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(job);
        engine.EnqueueTimeoutJobCore(job.Job, delay);
    }

    /// <summary>HostGetCodeForEval(argument): the code of a value given to eval (a string; the host may accept other objects, e.g. a TrustedScript), or null.</summary>
    public virtual string? GetCodeForEval(JsEngine engine, JsValue argument) => argument.IsString ? argument.AsString() : null;

    /// <summary>HostEnsureCanCompileStrings: throws (e.g. an EvalError) when the host forbids the compilation of the code (a content security policy).</summary>
    public virtual void EnsureCanCompileStrings(JsEngine engine, JsRealm realm, IReadOnlyList<string> parameterStrings, string bodyString, string codeString, JsCompilationType compilationType, IReadOnlyList<JsValue> parameterArgs, JsValue bodyArg)
    {
    }

    /// <summary>Reports the exception thrown by a job (HostReportErrors). The default implementation rethrows it: it propagates out of <see cref="JsEngine.RunJobs"/>, and the other jobs stay in the queue.</summary>
    public virtual void ReportJobException(JsEngine engine, JsException exception) => System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(exception);
}
