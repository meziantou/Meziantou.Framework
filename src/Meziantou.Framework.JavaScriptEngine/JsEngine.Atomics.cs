namespace Meziantou.Framework.JavaScriptEngine;

// The state of the agent used by Atomics (https://tc39.es/ecma262/#sec-agents)
public sealed partial class JsEngine
{
    private static readonly TimeSpan MaxAtomicsPollInterval = TimeSpan.FromMilliseconds(10);

    // Pulsed when the promise of an Atomics.waitAsync is about to be resolved (by another agent, or by a timeout)
    private readonly object _atomicsSignal = new();
    private int _pendingAtomicsWaitAsyncCount;

    /// <summary>Gets or sets the [[CanBlock]] field of the agent: when false, <c>Atomics.wait</c> throws a TypeError (e.g. the main thread of a browser).</summary>
    internal bool CanBlock { get; set; } = true;

    /// <summary>Gets the number of the waiters of <c>Atomics.waitAsync</c> whose promise is not resolved yet (the host keeps the agent alive while it is not zero).</summary>
    internal int PendingAtomicsWaitAsyncCount => Volatile.Read(ref _pendingAtomicsWaitAsyncCount);

    internal void IncrementPendingAtomicsWaitAsync() => Interlocked.Increment(ref _pendingAtomicsWaitAsyncCount);

    internal void DecrementPendingAtomicsWaitAsync() => Interlocked.Decrement(ref _pendingAtomicsWaitAsyncCount);

    /// <summary>
    /// EnqueueResolveInAgentJob: enqueues the job resolving the promise of an <c>Atomics.waitAsync</c> (HostEnqueueGenericJob).
    /// It can be called from any thread (the agent that calls <c>Atomics.notify</c>).
    /// </summary>
    internal void EnqueueAtomicsWaitAsyncResolution(JsJobAction action, object? state, JsValue argument, JsRealm realm)
    {
        EnqueueGenericJob(action, state, argument, realm);
        Monitor.Enter(_atomicsSignal);
        try
        {
            Monitor.PulseAll(_atomicsSignal);
        }
        finally
        {
            Monitor.Exit(_atomicsSignal);
        }
    }

    /// <summary>
    /// Runs the jobs (<see cref="RunJobs"/>), then waits for the promises of the pending <c>Atomics.waitAsync</c> calls:
    /// their notifications by other agents and their timeouts, running the jobs they enqueue. It returns when no
    /// waiter is pending; a waiter without a timeout that no agent notifies keeps it waiting until the cancellation
    /// token of the engine is canceled.
    /// </summary>
    /// <exception cref="JsTerminationException">The cancellation token of the engine was canceled.</exception>
    internal void RunJobsAndWaitForAtomicsWaiters()
    {
        RunJobs();
        while (PendingAtomicsWaitAsyncCount > 0)
        {
            if (Options.CancellationToken.IsCancellationRequested)
                throw new JsTerminationException(JsTerminationReason.Canceled, "The execution was canceled");

            if (!HasPendingJobs)
            {
                var delay = GetDelayUntilNextTimeoutJob() is { } next && next < MaxAtomicsPollInterval ? next : MaxAtomicsPollInterval;
                Monitor.Enter(_atomicsSignal);
                try
                {
                    if (!HasPendingJobs)
                    {
                        Monitor.Wait(_atomicsSignal, delay);
                    }
                }
                finally
                {
                    Monitor.Exit(_atomicsSignal);
                }
            }

            RunJobs();
        }
    }
}
