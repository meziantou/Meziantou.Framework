namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>A job enqueued by <see cref="JsEngine.EnqueueTimeoutJob(Action, TimeSpan, JsRealm?)"/>, which runs once its delay has elapsed unless it is canceled.</summary>
public sealed class JsTimeoutJob
{
    private readonly JsEngine _engine;
    private readonly Action _action;
    private volatile bool _isCanceled;

    internal JsTimeoutJob(JsEngine engine, Action action)
    {
        _engine = engine;
        _action = action;
    }

    /// <summary>Gets whether the job is canceled: it does not run.</summary>
    public bool IsCanceled => _isCanceled;

    /// <summary>
    /// Cancels the job: it does not run, and it no longer delays <see cref="JsEngine.WaitForJobsAsync"/>. It has no effect
    /// once the job has run. This method can be called from any thread.
    /// </summary>
    public void Cancel()
    {
        if (_isCanceled)
            return;

        _isCanceled = true;

        // A host waiting for the delay of the job computes the delay of the next one
        _engine.SignalJobs();
    }

    internal static void Run(object? state, JsValue argument)
    {
        var job = (JsTimeoutJob)state!;
        if (!job._isCanceled)
        {
            job._action();
        }
    }
}
