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
internal readonly struct JobCallback(JsObject callback, object? hostDefined)
{
    public JsObject Callback { get; } = callback;

    /// <summary>Gets the [[HostDefined]] value, captured by <see cref="JsHostHooks.MakeJobCallback"/>.</summary>
    public object? HostDefined { get; } = hostDefined;
}

/// <summary>The operation of HostPromiseRejectionTracker.</summary>
internal enum PromiseRejectionOperation
{
    /// <summary>A promise was rejected without any handler.</summary>
    Reject,

    /// <summary>A handler was added to a rejected promise that had none.</summary>
    Handle,
}

/// <summary>
/// The host-defined operations of the engine (https://tc39.es/ecma262/#sec-host-hooks-summary). The default
/// implementations are the ones of the specification; a host (e.g. WebPlatform) derives from this class to integrate the
/// jobs with its event loop.
/// </summary>
internal class JsHostHooks
{
    /// <summary>Gets the default host hooks.</summary>
    public static JsHostHooks Default { get; } = new();

    /// <summary>HostPromiseRejectionTracker(promise, operation). The default implementation does nothing.</summary>
    public virtual void PromiseRejectionTracker(JsEngine engine, JsPromise promise, PromiseRejectionOperation operation)
    {
    }

    /// <summary>HostMakeJobCallback(callback): called when a callback is registered (e.g. by <c>then</c>), to capture a host-defined state.</summary>
    public virtual JobCallback MakeJobCallback(JsEngine engine, JsObject callback) => new(callback, hostDefined: null);

    /// <summary>HostCallJobCallback(jobCallback, V, argumentsList): calls the callback, restoring the host-defined state captured by <see cref="MakeJobCallback"/>.</summary>
    public virtual JsValue CallJobCallback(JsEngine engine, in JobCallback jobCallback, JsValue thisValue, ReadOnlySpan<JsValue> arguments) => jobCallback.Callback.Call(thisValue, arguments);

    /// <summary>HostEnqueuePromiseJob(job, realm). The default implementation adds the job to the job queue of the engine.</summary>
    public virtual void EnqueuePromiseJob(JsEngine engine, in JsJob job) => engine.EnqueuePromiseJobCore(job);

    /// <summary>HostEnqueueGenericJob(job, realm). It can be called from any thread (e.g. the cleanup of a FinalizationRegistry, from the finalizer thread).</summary>
    public virtual void EnqueueGenericJob(JsEngine engine, in JsJob job) => engine.EnqueueGenericJobCore(job);

    /// <summary>HostEnqueueTimeoutJob(timeoutJob, realm, milliseconds). The default implementation runs the job from <see cref="JsEngine.RunJobs"/> once the delay has elapsed (measured with the <see cref="JsEngineOptions.TimeProvider"/>).</summary>
    public virtual void EnqueueTimeoutJob(JsEngine engine, in JsJob job, TimeSpan delay) => engine.EnqueueTimeoutJobCore(job, delay);

    /// <summary>Reports the exception thrown by a job (HostReportErrors). The default implementation rethrows it: it propagates out of <see cref="JsEngine.RunJobs"/>, and the other jobs stay in the queue.</summary>
    public virtual void ReportJobException(JsEngine engine, JsException exception) => System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(exception);
}
