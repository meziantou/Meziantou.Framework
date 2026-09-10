using System.Threading.Tasks.Sources;
using Meziantou.Framework.Threading.Tasks;

namespace Meziantou.Framework.Threading.Tests;

public sealed class TaskExtensionsTests
{
    [Fact]
    public void ForgetTest_SuccessfullyCompleted()
    {
        var task = Task.FromResult(0);
        task.Forget(); // Should not throw exception
    }

    [Fact]
    public void ForgetTest_Faulted()
    {
        var task = Task.FromException(new InvalidOperationException(""));
        task.Forget(); // Should not throw exception
    }

    [Fact]
    public void ForgetTest_Canceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var task = Task.FromCanceled(cts.Token);
        task.Forget(); // Should not throw exception
    }

    [Fact]
    public async Task WhenAll()
    {
        var (a, b) = await (Task.FromResult(0), Task.FromResult("test"));
        Assert.Equal(0, a);
        Assert.Equal("test", b);
    }

    [Fact]
    public async Task WhenAll_ConfigureAwait()
    {
        var (a, b) = await (Task.FromResult(0), Task.FromResult("test")).ConfigureAwait(false);
        Assert.Equal(0, a);
        Assert.Equal("test", b);
    }

    [Fact]
    public async Task WhenAll_NonGenericTask()
    {
        await (Task.CompletedTask, Task.CompletedTask);
    }

    [Fact]
    public async Task WhenAll_NonGenericTask_ConfigureAwait_bool()
    {
        await (Task.CompletedTask, Task.CompletedTask).ConfigureAwait(false);
    }

    [Fact]
    public async Task WhenAll_NonGenericTask_ConfigureAwait_Options()
    {
        await (Task.CompletedTask, Task.CompletedTask).ConfigureAwait(ConfigureAwaitOptions.None);
    }

    [Fact]
    public void WhenAll_ConfigureAwait_SuppressThrowing_IsRejected()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => { _ = (Task.FromResult(0), Task.FromResult("test")).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing); });
        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void WhenAll_ConfigureAwait_SuppressThrowing_IsRejected_SingleTask()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => { _ = new ValueTuple<Task<int>>(Task.FromResult(0)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing); });
        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void WhenAll_ConfigureAwait_SuppressThrowing_IsRejected_SevenTasks()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => { _ = (Task.FromResult(0), Task.FromResult(1), Task.FromResult(2), Task.FromResult(3), Task.FromResult(4), Task.FromResult(5), Task.FromResult(6)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing); });
        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void WhenAll_ConfigureAwait_SuppressThrowing_IsRejectedBeforeAwaitingTheTasks()
    {
        var pending = new TaskCompletionSource<int>();
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = (pending.Task, Task.FromResult("test")).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing); });
        Assert.False(pending.Task.IsCompleted);
    }

    [Fact]
    public void WhenAll_ConfigureAwait_SuppressThrowing_IsRejected_CombinedWithOtherOptions()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => { _ = (Task.FromResult(0), Task.FromResult("test")).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing); });
        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public async Task WhenAll_ConfigureAwait_OtherOptionsAreSupported()
    {
        var (a, b) = await (Task.FromResult(0), Task.FromResult("test")).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        Assert.Equal(0, a);
        Assert.Equal("test", b);
    }

    [Fact]
    public async Task WhenAll_NonGenericTask_ConfigureAwait_SuppressThrowing()
    {
        await (Task.CompletedTask, Task.FromException(new InvalidOperationException("test"))).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2012:Use ValueTasks correctly", Justification = "For testing purpose")]
    public async Task WhenAll_ValueTask()
    {
        var (a, b) = await (ValueTask.FromResult(0), ValueTask.FromResult("test"));
        Assert.Equal(0, a);
        Assert.Equal("test", b);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2012:Use ValueTasks correctly", Justification = "For testing purpose")]
    public async Task WhenAll_ValueTask_Exception()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await (ValueTask.FromResult(0), ValueTask.FromException<string>(new InvalidOperationException("test"))));
        Assert.Equal("test", exception.Message);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2012:Use ValueTasks correctly", Justification = "For testing purpose")]
    public async Task WhenAll_ValueTask_AllFailuresAreReported()
    {
        var task = Meziantou.Framework.Threading.Tasks.TaskExtensions.WhenAll(ValueTask.FromException<int>(new InvalidOperationException("first")), ValueTask.FromException<string>(new InvalidOperationException("second"))).AsTask();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Equal("first", exception.Message);
        Assert.NotNull(task.Exception);
        Assert.Equal(new[] { "first", "second" }, task.Exception.InnerExceptions.Select(ex => ex.Message).ToArray());
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2012:Use ValueTasks correctly", Justification = "For testing purpose")]
    public async Task WhenAll_ValueTask_FaultedTaskCarryingAnOperationCanceledExceptionIsAFailure()
    {
        var task = Meziantou.Framework.Threading.Tasks.TaskExtensions.WhenAll(ValueTask.FromResult(0), ValueTask.FromException<string>(new OperationCanceledException("test"))).AsTask();

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => task);
        Assert.Equal("test", exception.Message);
        Assert.Equal(TaskStatus.Faulted, task.Status);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2012:Use ValueTasks correctly", Justification = "For testing purpose")]
    public async Task WhenAll_ValueTask_AllExceptionsOfAFaultedTaskAreReported()
    {
        var taskCompletionSource = new TaskCompletionSource<string>();
        taskCompletionSource.SetException([new InvalidOperationException("first"), new InvalidOperationException("second")]);

        var task = Meziantou.Framework.Threading.Tasks.TaskExtensions.WhenAll(ValueTask.FromResult(0), new ValueTask<string>(taskCompletionSource.Task)).AsTask();

        await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.NotNull(task.Exception);
        Assert.Equal(new[] { "first", "second" }, task.Exception.InnerExceptions.Select(ex => ex.Message).ToArray());
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2012:Use ValueTasks correctly", Justification = "For testing purpose")]
    public async Task WhenAll_ValueTask_CompletesSynchronouslyWhenAllTasksAreAlreadyCompleted()
    {
        var task = Meziantou.Framework.Threading.Tasks.TaskExtensions.WhenAll(ValueTask.FromResult(0), ValueTask.FromResult("test"));

        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal((0, "test"), await task);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2012:Use ValueTasks correctly", Justification = "For testing purpose")]
    public async Task WhenAll_ValueTask_Canceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var task = Meziantou.Framework.Threading.Tasks.TaskExtensions.WhenAll(ValueTask.FromResult(0), ValueTask.FromCanceled<string>(cts.Token)).AsTask();
        var exception = await Assert.ThrowsAsync<TaskCanceledException>(() => task);
        Assert.Equal(cts.Token, exception.CancellationToken);
        Assert.Equal(TaskStatus.Canceled, task.Status);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2012:Use ValueTasks correctly", Justification = "For testing purpose")]
    public async Task WhenAll_ValueTask_ExceptionTakesPrecedenceOverCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var task = Meziantou.Framework.Threading.Tasks.TaskExtensions.WhenAll(ValueTask.FromCanceled<int>(cts.Token), ValueTask.FromException<string>(new InvalidOperationException("test"))).AsTask();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Equal("test", exception.Message);
        Assert.Equal(TaskStatus.Faulted, task.Status);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2012:Use ValueTasks correctly", Justification = "For testing purpose")]
    public async Task WhenAll_ValueTask_AllTasksAreObservedWhenCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var awaited = false;
        var task = Meziantou.Framework.Threading.Tasks.TaskExtensions.WhenAll(ValueTask.FromCanceled<int>(cts.Token), Observe()).AsTask();
        await Assert.ThrowsAsync<TaskCanceledException>(() => task);
        Assert.True(awaited);

        async ValueTask<string> Observe()
        {
            await Task.Yield();
            awaited = true;
            return "test";
        }
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2012:Use ValueTasks correctly", Justification = "For testing purpose")]
    public async Task WhenAll_NonGenericValueTask_Canceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(async () => await (ValueTask.CompletedTask, ValueTask.FromCanceled(cts.Token)));
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2012:Use ValueTasks correctly", Justification = "For testing purpose")]
    public async Task WhenAll_NonGenericValueTask_ExceptionTakesPrecedenceOverCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await (ValueTask.FromCanceled(cts.Token), ValueTask.FromException(new InvalidOperationException("test"))));
        Assert.Equal("test", exception.Message);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2012:Use ValueTasks correctly", Justification = "For testing purpose")]
    public async Task WhenAll_ValueTask_ConsumesEachPendingTaskOnceWhenOneFails()
    {
        var source1 = new TrackingValueTaskSource<int>();
        var source2 = new TrackingValueTaskSource<int>();
        var source3 = new TrackingValueTaskSource<int>();
        var task = Meziantou.Framework.Threading.Tasks.TaskExtensions.WhenAll(source1.CreateValueTask(), source2.CreateValueTask(), source3.CreateValueTask()).AsTask();

        source1.SetResult(1);
        source2.SetException(new InvalidOperationException("test"));
        source3.SetResult(3);

        await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Equal(1, source1.ConsumedCount);
        Assert.Equal(1, source2.ConsumedCount);
        Assert.Equal(1, source3.ConsumedCount);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2012:Use ValueTasks correctly", Justification = "For testing purpose")]
    public async Task WhenAll_ValueTask_ConsumesEachPendingTaskOnceWhenOneIsCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var source1 = new TrackingValueTaskSource<int>();
        var source2 = new TrackingValueTaskSource<int>();
        var task = Meziantou.Framework.Threading.Tasks.TaskExtensions.WhenAll(source1.CreateValueTask(), source2.CreateValueTask()).AsTask();

        source1.SetCanceled(cts.Token);
        source2.SetResult(2);

        await Assert.ThrowsAsync<TaskCanceledException>(() => task);
        Assert.Equal(1, source1.ConsumedCount);
        Assert.Equal(1, source2.ConsumedCount);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2012:Use ValueTasks correctly", Justification = "For testing purpose")]
    public async Task WhenAll_ValueTask_ConsumesEachCompletedTaskOnceWhenOneFails()
    {
        var source1 = new TrackingValueTaskSource<int>();
        var source2 = new TrackingValueTaskSource<int>();
        source1.SetException(new InvalidOperationException("test"));
        source2.SetResult(2);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await (source1.CreateValueTask(), source2.CreateValueTask()));
        Assert.Equal(1, source1.ConsumedCount);
        Assert.Equal(1, source2.ConsumedCount);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2012:Use ValueTasks correctly", Justification = "For testing purpose")]
    public async Task WhenAll_ValueTask_ConsumesEachTaskOnceWhenAllSucceed()
    {
        var source1 = new TrackingValueTaskSource<int>();
        var source2 = new TrackingValueTaskSource<int>();
        source1.SetResult(1);
        source2.SetResult(2);

        var (result1, result2) = await (source1.CreateValueTask(), source2.CreateValueTask());

        Assert.Equal(1, result1);
        Assert.Equal(2, result2);
        Assert.Equal(1, source1.ConsumedCount);
        Assert.Equal(1, source2.ConsumedCount);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2012:Use ValueTasks correctly", Justification = "For testing purpose")]
    public async Task WhenAll_NonGenericValueTask_ConsumesEachPendingTaskOnceWhenOneFails()
    {
        var source1 = new TrackingValueTaskSource<int>();
        var source2 = new TrackingValueTaskSource<int>();
        var task = ConsumeAsync();

        source1.SetException(new InvalidOperationException("test"));
        source2.SetResult(2);

        await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Equal(1, source1.ConsumedCount);
        Assert.Equal(1, source2.ConsumedCount);

        async Task ConsumeAsync() => await (source1.CreateNonGenericValueTask(), source2.CreateNonGenericValueTask());
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2012:Use ValueTasks correctly", Justification = "For testing purpose")]
    public async Task WhenAll_NonGenericValueTask_ConsumesEachTaskOnceWhenAllSucceed()
    {
        var source1 = new TrackingValueTaskSource<int>();
        var source2 = new TrackingValueTaskSource<int>();
        source1.SetResult(1);
        source2.SetResult(2);

        await (source1.CreateNonGenericValueTask(), source2.CreateNonGenericValueTask());

        Assert.Equal(1, source1.ConsumedCount);
        Assert.Equal(1, source2.ConsumedCount);
    }

    /// <summary>
    /// A value task source counting how many times its result is consumed, as a <see cref="ValueTask"/> must be consumed exactly once.
    /// </summary>
    private sealed class TrackingValueTaskSource<T> : IValueTaskSource<T>, IValueTaskSource
    {
        private ManualResetValueTaskSourceCore<T> _core = new() { RunContinuationsAsynchronously = true };
        private int _consumedCount;

        public int ConsumedCount => Volatile.Read(ref _consumedCount);

        public ValueTask<T> CreateValueTask() => new(this, _core.Version);
        public ValueTask CreateNonGenericValueTask() => new(this, _core.Version);

        public void SetResult(T result) => _core.SetResult(result);
        public void SetException(Exception exception) => _core.SetException(exception);
        public void SetCanceled(CancellationToken cancellationToken) => _core.SetException(new OperationCanceledException(cancellationToken));

        T IValueTaskSource<T>.GetResult(short token)
        {
            Interlocked.Increment(ref _consumedCount);
            return _core.GetResult(token);
        }

        void IValueTaskSource.GetResult(short token)
        {
            Interlocked.Increment(ref _consumedCount);
            _core.GetResult(token);
        }

        ValueTaskSourceStatus IValueTaskSource<T>.GetStatus(short token) => _core.GetStatus(token);
        ValueTaskSourceStatus IValueTaskSource.GetStatus(short token) => _core.GetStatus(token);
        void IValueTaskSource<T>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) => _core.OnCompleted(continuation, state, token, flags);
        void IValueTaskSource.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) => _core.OnCompleted(continuation, state, token, flags);
    }
}
