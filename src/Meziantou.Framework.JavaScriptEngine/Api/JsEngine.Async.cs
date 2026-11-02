namespace Meziantou.Framework.JavaScriptEngine;

// The asynchronous embedding API: the work posted by the host, the promises of .NET tasks and the tasks of promises, the
// event loop that waits for them, and the reporting of the unhandled rejections.
public sealed partial class JsEngine
{
    private volatile bool _isDisposed;

    // Completed when a job is queued from outside the engine (the posted work, the generic and timeout jobs): wakes
    // WaitForJobsAsync. Created by the first waiter.
    private TaskCompletionSource? _jobSignal;

    // The tasks of ToTask that are not completed yet: canceled when the engine is disposed
    private readonly Lock _pendingTasksLock = new();
    private HashSet<PromiseTaskSource>? _pendingTasks;

    // The promises rejected without a handler since the last checkpoint, and the ones reported as unhandled
    private List<JsPromise>? _pendingRejections;
    private System.Runtime.CompilerServices.ConditionalWeakTable<JsPromise, object>? _reportedRejections;

    /// <summary>
    /// Occurs when a promise is rejected and has no handler at the end of <see cref="RunJobs"/> (once the job queue is empty:
    /// a handler attached by a job of the same <see cref="RunJobs"/> call, or by the script before the call, prevents it).
    /// It is raised on the thread of the engine. A promise rejected by a script that runs without a call to
    /// <see cref="RunJobs"/> is reported by the next call.
    /// </summary>
    public event EventHandler<JsPromiseRejectionEventArgs>? UnhandledRejection;

    /// <summary>Occurs when a handler is attached to a promise that <see cref="UnhandledRejection"/> reported.</summary>
    public event EventHandler<JsPromiseRejectionEventArgs>? RejectionHandled;

    /// <summary>
    /// Posts work to run on the thread of the engine. It can be called from any thread (e.g. the continuation of an I/O
    /// operation). The work runs from <see cref="RunJobs"/> as a task of the event loop: after the promise jobs already in
    /// the queue, in the order it was posted, and followed by the promise jobs it enqueues. A <see cref="JsException"/>
    /// thrown by the work goes to <see cref="JsHost.ReportJobException"/>; another exception comes out of
    /// <see cref="RunJobs"/>. A host whose <see cref="JsHost.EnqueueGenericJob"/> is overridden runs the work in its own
    /// event loop.
    /// </summary>
    /// <param name="action">The work.</param>
    /// <param name="realm">The realm that is current while the work runs, or null to keep the current realm.</param>
    /// <returns>false when the engine is disposed: the work does not run.</returns>
    public bool Post(Action action, JsRealm? realm = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_isDisposed)
            return false;

        HostHooks.EnqueueGenericJob(this, new JsHostJob(this, new JsJob(static (state, _) => ((Action)state!)(), action, default, realm)));
        return true;
    }

    /// <summary>
    /// Waits until a job is ready to run: a promise job, work posted by <see cref="Post"/>, a generic job, or a timeout job
    /// whose delay has elapsed. It completes at once when one is already queued. A host calls it between two calls to
    /// <see cref="RunJobs"/>, while the engine does not run, to implement its event loop.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The engine is disposed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
    public async Task WaitForJobsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var signal = GetJobSignal();
        if (HasPendingJobs)
            return;

        var delay = GetDelayUntilNextTimeoutJob();
        if (delay == TimeSpan.Zero)
            return;

        if (delay is null)
        {
            await signal.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            try
            {
                await signal.Task.WaitAsync(delay.Value, Options.TimeProvider, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // A timeout job is due
            }
        }

        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    /// <summary>
    /// Gets a task that completes when the promise settles: its result is the value of a fulfilled promise, and the
    /// <see cref="JsException"/> of the rejection reason for a rejected promise. A value that is not a promise is resolved as
    /// <c>Promise.resolve</c> does. The promise settles while the engine runs its jobs (<see cref="RunJobs"/>, or
    /// <see cref="RunUntilSettledAsync"/>), and the continuations of the task run on the thread pool, after the job. The
    /// task is canceled when the engine is disposed first.
    /// </summary>
    /// <exception cref="InvalidOperationException">The promise belongs to another engine, or the engine has no realm to resolve a value that is not a promise.</exception>
    public Task<JsValue> ToTask(JsValue promise)
    {
        JsObject.CheckValueOwner(this, promise);
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var source = new PromiseTaskSource(this);
        using (Enter())
        {
            if (promise.Cell is not JsPromise jsPromise)
            {
                var realm = CurrentRealm ?? (_realms.Count > 0 ? _realms[0] : throw new InvalidOperationException("The engine has no realm"));
                jsPromise = JsPromise.PromiseResolve(realm, promise);
            }

            lock (_pendingTasksLock)
            {
                (_pendingTasks ??= []).Add(source);
            }

            JsPromise.PerformPromiseThen(
                jsPromise,
                static (state, value) => ((PromiseTaskSource)state!).Complete(value, exception: null),
                static (state, reason) => ((PromiseTaskSource)state!).Complete(default, new JsException(reason)),
                source);
        }

        return source.Task;
    }

    /// <summary>
    /// Runs the event loop of the engine until the promise settles: runs the jobs (<see cref="RunJobs"/>), and waits for
    /// posted work and timeout jobs (<see cref="WaitForJobsAsync"/>) while the promise is pending. The engine is free between
    /// two runs of the jobs: the work of the host (e.g. the continuations of its tasks) reaches it with <see cref="Post"/>.
    /// </summary>
    /// <returns>The value of the fulfilled promise.</returns>
    /// <exception cref="JsException">The promise is rejected, or a job threw an exception that the host does not handle.</exception>
    /// <exception cref="ObjectDisposedException">The engine is disposed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
    public async Task<JsValue> RunUntilSettledAsync(JsValue promise, CancellationToken cancellationToken = default)
    {
        var task = ToTask(promise);
        while (true)
        {
            RunJobs();
            if (task.IsCompleted)
                return await task.ConfigureAwait(false);

            await WaitForJobsAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Releases the resources of the engine: the posted work no longer runs, and the tasks of the promises are canceled.</summary>
    private void DisposeAsyncState()
    {
        _isDisposed = true;
        SignalJobs();
        PromiseTaskSource[] pending;
        lock (_pendingTasksLock)
        {
            if (_pendingTasks is null)
                return;

            pending = [.. _pendingTasks];
            _pendingTasks = null;
        }

        foreach (var source in pending)
        {
            source.TrySetCanceled();
        }
    }

    private TaskCompletionSource GetJobSignal()
    {
        var signal = Volatile.Read(ref _jobSignal);
        if (signal is null)
        {
            var created = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            signal = Interlocked.CompareExchange(ref _jobSignal, created, comparand: null) ?? created;
        }

        return signal;
    }

    /// <summary>Wakes the waiters of <see cref="WaitForJobsAsync"/>: a job was queued from outside the engine.</summary>
    internal void SignalJobs() => Interlocked.Exchange(ref _jobSignal, null)?.TrySetResult();

    /// <summary>Tracks the promises rejected without a handler for <see cref="UnhandledRejection"/> (HostPromiseRejectionTracker).</summary>
    private void TrackUnhandledRejection(JsPromise promise, JsPromiseRejectionOperation operation)
    {
        if (UnhandledRejection is null && RejectionHandled is null)
            return;

        if (operation is JsPromiseRejectionOperation.Reject)
        {
            (_pendingRejections ??= []).Add(promise);
        }
        else if (_pendingRejections?.Remove(promise) is not true && _reportedRejections is not null && _reportedRejections.Remove(promise))
        {
            RejectionHandled?.Invoke(this, new JsPromiseRejectionEventArgs(promise, promise.Result));
        }
    }

    /// <summary>Reports the promises that are still rejected without a handler (at the end of <see cref="RunJobs"/>).</summary>
    private void NotifyUnhandledRejections()
    {
        if (_pendingRejections is not { Count: > 0 } pending)
            return;

        var promises = pending.ToArray();
        pending.Clear();
        foreach (var promise in promises)
        {
            if (promise.IsHandled)
                continue;

            (_reportedRejections ??= []).AddOrUpdate(promise, value: null!);
            UnhandledRejection?.Invoke(this, new JsPromiseRejectionEventArgs(promise, promise.Result));
        }
    }

    private sealed class PromiseTaskSource(JsEngine engine) : TaskCompletionSource<JsValue>(TaskCreationOptions.RunContinuationsAsynchronously)
    {
        public void Complete(JsValue value, JsException? exception)
        {
            lock (engine._pendingTasksLock)
            {
                engine._pendingTasks?.Remove(this);
            }

            if (exception is null)
            {
                TrySetResult(value);
            }
            else
            {
                TrySetException(exception);
            }
        }
    }
}

/// <summary>The arguments of <see cref="JsEngine.UnhandledRejection"/> and <see cref="JsEngine.RejectionHandled"/>.</summary>
public sealed class JsPromiseRejectionEventArgs : EventArgs
{
    internal JsPromiseRejectionEventArgs(JsObject promise, JsValue reason)
    {
        Promise = promise;
        Reason = reason;
    }

    /// <summary>Gets the rejected promise.</summary>
    public JsObject Promise { get; }

    /// <summary>Gets the rejection reason of the promise.</summary>
    public JsValue Reason { get; }
}

// The promises of the .NET tasks
public sealed partial class JsRealm
{
    /// <summary>
    /// Creates a promise of the realm that settles when the task completes: it is fulfilled with undefined, or rejected
    /// (see <see cref="CreatePromise{T}(Task{T}, Func{T, JsValue})"/>).
    /// </summary>
    public JsObject CreatePromise(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return CreatePromiseCore(task, static _ => JsValue.Undefined);
    }

    /// <summary>
    /// Creates a promise of the realm that settles when the task completes (see <see cref="CreatePromise{T}(Task{T}, Func{T, JsValue})"/>).
    /// The value of the task must not be an object created out of the thread of the engine: use
    /// <see cref="CreatePromise{T}(Task{T}, Func{T, JsValue})"/> to create it when the promise settles.
    /// </summary>
    public JsObject CreatePromise(Task<JsValue> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return CreatePromiseCore(task, static task => ((Task<JsValue>)task).Result);
    }

    /// <summary>
    /// Creates a promise of the realm that settles when the task completes. The task can complete on any thread: the
    /// engine settles the promise on its own thread, as work posted by <see cref="JsEngine.Post"/>, so the host runs the
    /// jobs (<see cref="JsEngine.RunJobs"/>, or <see cref="JsEngine.RunUntilSettledAsync"/>). The promise is:
    /// <list type="bullet">
    /// <item><description>fulfilled with the result converted by <paramref name="convertResult"/>, which runs on the thread of the engine (it can create objects);</description></item>
    /// <item><description>rejected with the value of a <see cref="JsException"/> thrown by the task or by <paramref name="convertResult"/>;</description></item>
    /// <item><description>rejected with an <c>Error</c> whose message is the message of any other exception, or of the <see cref="OperationCanceledException"/> of a canceled task.</description></item>
    /// </list>
    /// When the engine is disposed before the task completes, the promise stays pending.
    /// </summary>
    public JsObject CreatePromise<T>(Task<T> task, Func<T, JsValue> convertResult)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(convertResult);
        return CreatePromiseCore(task, task => convertResult(((Task<T>)task).Result));
    }

    private JsPromise CreatePromiseCore(Task task, Func<Task, JsValue> getResult)
    {
        using var scope = Engine.Enter();
        var capability = PromiseCapability.CreateIntrinsic(this);
        if (task.IsCompleted)
        {
            Settle(capability, task, getResult);
        }
        else
        {
            _ = task.ContinueWith(
                completed => Engine.Post(() => Settle(capability, completed, getResult), this),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        return (JsPromise)capability.Promise;
    }

    private void Settle(PromiseCapability capability, Task task, Func<Task, JsValue> getResult)
    {
        JsValue result;
        try
        {
            if (!task.IsCompletedSuccessfully)
            {
                var exception = task.IsCanceled ? new TaskCanceledException(task) : task.Exception!.InnerExceptions.Count == 1 ? task.Exception.InnerException! : task.Exception;
                capability.Reject(exception is JsException jsException ? jsException.Value : new JsValue(CreateError(ErrorType.Error, exception.Message)));
                return;
            }

            result = getResult(task);
        }
        catch (JsException exception)
        {
            capability.Reject(exception.Value);
            return;
        }

        capability.Resolve(result);
    }
}
