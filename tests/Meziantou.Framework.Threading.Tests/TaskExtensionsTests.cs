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
        var exception = await Assert.ThrowsAsync<AggregateException>(async () => await (ValueTask.FromResult(0), ValueTask.FromException<string>(new InvalidOperationException("test"))));
        Assert.Equal("One or more errors occurred. (test)", exception.Message);
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
        var exception = await Assert.ThrowsAsync<AggregateException>(() => task);
        Assert.Equal("One or more errors occurred. (test)", exception.Message);
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

        var exception = await Assert.ThrowsAsync<AggregateException>(async () => await (ValueTask.FromCanceled(cts.Token), ValueTask.FromException(new InvalidOperationException("test"))));
        Assert.Equal("One or more errors occurred. (test)", exception.Message);
    }
}
