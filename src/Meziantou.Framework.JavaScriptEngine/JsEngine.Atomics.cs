using System.Collections.Concurrent;

namespace Meziantou.Framework.JavaScriptEngine;

// The state of the agent used by Atomics (https://tc39.es/ecma262/#sec-agents)
public sealed partial class JsEngine
{
    // TEMPORARY: the jobs resolving the promises of Atomics.waitAsync, until they go to the job queue of the engine
    // (HostEnqueueGenericJob, which can be called from any thread).
    private readonly ConcurrentQueue<Action> _atomicsJobs = new();
    private int _pendingAtomicsWaitAsyncCount;

    /// <summary>Gets or sets the [[CanBlock]] field of the agent: when false, <c>Atomics.wait</c> throws a TypeError (e.g. the main thread of a browser).</summary>
    internal bool CanBlock { get; set; } = true;

    /// <summary>Gets the number of the waiters of <c>Atomics.waitAsync</c> that are not resolved yet (the host keeps the agent alive while it is not zero).</summary>
    internal int PendingAtomicsWaitAsyncCount => Volatile.Read(ref _pendingAtomicsWaitAsyncCount);

    /// <summary>Gets whether a job enqueued by <see cref="EnqueueAtomicsJob"/> is waiting to run.</summary>
    internal bool HasPendingAtomicsJobs => !_atomicsJobs.IsEmpty;

    /// <summary>
    /// Enqueues a job that runs on the thread of the agent (the resolution of the promise of an <c>Atomics.waitAsync</c>).
    /// It can be called from any thread. The jobs run from <see cref="RunAtomicsJobs"/>.
    /// </summary>
    internal void EnqueueAtomicsJob(Action job) => _atomicsJobs.Enqueue(job);

    /// <summary>Runs the jobs enqueued by <see cref="EnqueueAtomicsJob"/>, on the thread of the engine; returns whether a job ran.</summary>
    internal bool RunAtomicsJobs()
    {
        using var scope = Enter();
        var ran = false;
        while (_atomicsJobs.TryDequeue(out var job))
        {
            job();
            ran = true;
        }

        return ran;
    }

    internal void IncrementPendingAtomicsWaitAsync() => Interlocked.Increment(ref _pendingAtomicsWaitAsyncCount);

    internal void DecrementPendingAtomicsWaitAsync() => Interlocked.Decrement(ref _pendingAtomicsWaitAsyncCount);
}
