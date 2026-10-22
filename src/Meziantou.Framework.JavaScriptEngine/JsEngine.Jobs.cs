using System.Collections.Concurrent;

namespace Meziantou.Framework.JavaScriptEngine;

// The job queue of the agent (https://tc39.es/ecma262/#sec-jobs) and the [[KeptAlive]] list of the WeakRefs.
public sealed partial class JsEngine
{
    // The generic jobs can be enqueued from any thread: they wait here until RunJobs moves them to the current queue (created
    // by the first one: most engines never get one)
    private ConcurrentQueue<JsJob>? _genericJobInbox;
    private readonly Lock _timeoutJobsLock = new();
    private PriorityQueue<JsJob, (long DueTimestamp, long Sequence)>? _timeoutJobs;
    private long _timeoutJobSequence;
    private JobQueue _jobQueue = new(parent: null);
    private List<JsCell>? _keptAlive;

    /// <summary>Gets or sets the host-defined operations (job scheduling, rejection tracking, job callbacks, error reporting).</summary>
    internal JsHost HostHooks { get; set; }

    /// <summary>Gets whether a job is ready to run (a promise job, or a generic job); the timeout jobs whose delay has not elapsed are not counted.</summary>
    internal bool HasPendingJobs => _jobQueue.PromiseJobs.Count > 0 || _jobQueue.Tasks.Count > 0 || Volatile.Read(ref _genericJobInbox) is { IsEmpty: false };

    /// <summary>
    /// Runs the jobs until the queue is empty: the promise jobs first (in the order they were enqueued), then the generic
    /// jobs and the timeout jobs whose delay has elapsed, each followed by the promise jobs it enqueued. The timeout jobs
    /// whose delay has not elapsed stay in the queue.
    /// </summary>
    /// <exception cref="JsException">A job threw an exception, and the host does not handle the job errors (the other jobs stay in the queue).</exception>
    public void RunJobs()
    {
        using var scope = Enter();
        while (TryDequeueJob(out var job))
        {
            RunJob(job);
        }
    }

    /// <summary>Gets the delay until the next timeout job can run (zero when one is due), or null when there is no timeout job.</summary>
    internal TimeSpan? GetDelayUntilNextTimeoutJob()
    {
        lock (_timeoutJobsLock)
        {
            if (_timeoutJobs is null || !_timeoutJobs.TryPeek(out _, out var priority))
                return null;

            var now = Options.TimeProvider.GetTimestamp();
            if (priority.DueTimestamp <= now)
                return TimeSpan.Zero;

            return Options.TimeProvider.GetElapsedTime(now, priority.DueTimestamp);
        }
    }

    /// <summary>
    /// Makes the jobs enqueued from now on go to a new queue, which <see cref="RunJobs"/> runs, until the returned scope
    /// is disposed. The jobs of the outer queue do not run in the meantime. When the scope is disposed, the jobs left in
    /// the nested queue are appended to the outer queue.
    /// </summary>
    private NestedJobQueueScope EnterNestedJobQueueCore()
    {
        var queue = new JobQueue(_jobQueue);
        _jobQueue = queue;
        return new NestedJobQueueScope(this, queue);
    }

    /// <summary>HostEnqueuePromiseJob(job, realm)</summary>
    internal void EnqueuePromiseJob(JsJobAction action, object? state, JsValue argument, JsRealm? realm) => HostHooks.EnqueuePromiseJob(this, new JsJob(action, state, argument, realm));

    /// <summary>HostEnqueueGenericJob(job, realm): it can be called from any thread.</summary>
    internal void EnqueueGenericJob(JsJobAction action, object? state, JsValue argument, JsRealm realm) => HostHooks.EnqueueGenericJob(this, new JsHostJob(this, new JsJob(action, state, argument, realm)));

    /// <summary>HostEnqueueTimeoutJob(timeoutJob, realm, milliseconds)</summary>
    internal void EnqueueTimeoutJob(JsJobAction action, object? state, JsValue argument, JsRealm realm, TimeSpan delay) => HostHooks.EnqueueTimeoutJob(this, new JsHostJob(this, new JsJob(action, state, argument, realm)), delay);

    /// <summary>The default implementation of HostEnqueuePromiseJob: adds the job to the current job queue.</summary>
    internal void EnqueuePromiseJobCore(in JsJob job) => _jobQueue.PromiseJobs.Enqueue(job);

    /// <summary>The default implementation of HostEnqueueGenericJob (thread-safe).</summary>
    internal void EnqueueGenericJobCore(in JsJob job)
    {
        var inbox = Volatile.Read(ref _genericJobInbox);
        if (inbox is null)
        {
            Interlocked.CompareExchange(ref _genericJobInbox, new ConcurrentQueue<JsJob>(), comparand: null);
            inbox = _genericJobInbox;
        }

        inbox.Enqueue(job);
    }

    /// <summary>The default implementation of HostEnqueueTimeoutJob: the job runs from <see cref="RunJobs"/> once the delay has elapsed.</summary>
    internal void EnqueueTimeoutJobCore(in JsJob job, TimeSpan delay)
    {
        var timeProvider = Options.TimeProvider;
        var ticks = delay <= TimeSpan.Zero ? 0 : (long)Math.Min(delay.TotalSeconds * timeProvider.TimestampFrequency, long.MaxValue / 2);
        var due = timeProvider.GetTimestamp() + ticks;
        lock (_timeoutJobsLock)
        {
            _timeoutJobs ??= new PriorityQueue<JsJob, (long, long)>();
            _timeoutJobs.Enqueue(job, (due, _timeoutJobSequence++));
        }
    }

    /// <summary>HostMakeJobCallback(callback)</summary>
    internal JsJobCallback MakeJobCallback(JsObject callback) => HostHooks.MakeJobCallback(this, callback);

    /// <summary>HostCallJobCallback(jobCallback, V, argumentsList)</summary>
    internal JsValue CallJobCallback(in JsJobCallback jobCallback, JsValue thisValue, ReadOnlySpan<JsValue> arguments) => HostHooks.CallJobCallback(this, jobCallback, thisValue, arguments);

    /// <summary>HostMakeJobCallback for a handler created by the engine (see <see cref="JsHost.MakeContinuationHostDefined"/>).</summary>
    internal object? MakeContinuationHostDefined() => HostHooks.MakeContinuationHostDefined(this);

    /// <summary>HostPromiseRejectionTracker(promise, operation)</summary>
    internal void TrackPromiseRejection(JsPromise promise, JsPromiseRejectionOperation operation) => HostHooks.PromiseRejectionTracker(this, promise, operation);

    /// <summary>AddToKeptObjects(value): keeps the target of a WeakRef alive until <see cref="ClearKeptObjects"/>.</summary>
    internal void AddToKeptObjects(JsCell value) => (_keptAlive ??= []).Add(value);

    /// <summary>ClearKeptObjects(): called when a synchronous sequence of executions completes (after each job, and by the host after a script).</summary>
    internal void ClearKeptObjects() => _keptAlive?.Clear();

    private bool TryDequeueJob(out JsJob job)
    {
        var queue = _jobQueue;
        if (queue.PromiseJobs.TryDequeue(out job))
            return true;

        if (queue.Tasks.Count == 0)
        {
            if (Volatile.Read(ref _genericJobInbox) is { } inbox)
            {
                while (inbox.TryDequeue(out var genericJob))
                {
                    queue.Tasks.Enqueue(genericJob);
                }
            }

            EnqueueDueTimeoutJobs(queue);
        }

        return queue.Tasks.TryDequeue(out job);
    }

    private void EnqueueDueTimeoutJobs(JobQueue queue)
    {
        lock (_timeoutJobsLock)
        {
            if (_timeoutJobs is null || _timeoutJobs.Count == 0)
                return;

            var now = Options.TimeProvider.GetTimestamp();
            while (_timeoutJobs.TryPeek(out _, out var priority) && priority.DueTimestamp <= now)
            {
                queue.Tasks.Enqueue(_timeoutJobs.Dequeue());
            }
        }
    }

    internal void RunJob(in JsJob job)
    {
        var previousRealm = CurrentRealm;
        if (job.Realm is not null)
        {
            CurrentRealm = job.Realm;
        }

        try
        {
            job.Action(job.State, job.Argument);
        }
        catch (JsException exception)
        {
            CurrentRealm = previousRealm;
            HostHooks.ReportJobException(this, exception);
        }
        finally
        {
            CurrentRealm = previousRealm;
            ClearKeptObjects();
        }
    }

    private sealed class JobQueue(JobQueue? parent)
    {
        public JobQueue? Parent { get; } = parent;

        public Queue<JsJob> PromiseJobs { get; } = new();

        // The generic jobs and the timeout jobs whose delay has elapsed
        public Queue<JsJob> Tasks { get; } = new();
    }

    private sealed class NestedJobQueueScope(JsEngine engine, JobQueue queue) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            if (!ReferenceEquals(engine._jobQueue, queue))
                throw new InvalidOperationException("The nested job queues must be exited in the reverse order they were entered");

            _disposed = true;
            var parent = queue.Parent!;
            while (queue.PromiseJobs.TryDequeue(out var job))
            {
                parent.PromiseJobs.Enqueue(job);
            }

            while (queue.Tasks.TryDequeue(out var job))
            {
                parent.Tasks.Enqueue(job);
            }

            engine._jobQueue = parent;
        }
    }
}
