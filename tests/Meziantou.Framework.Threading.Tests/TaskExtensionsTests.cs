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
        var exception = await Assert.ThrowsAsync<AggregateException>(async () => await (ValueTask.FromResult(0), ValueTask.FromException<string>(new InvalidOperationException("test"))));
        Assert.Equal("One or more errors occurred. (test)", exception.Message);
    }
}
