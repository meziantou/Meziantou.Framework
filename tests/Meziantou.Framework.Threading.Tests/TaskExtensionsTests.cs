using Meziantou.Framework.Threading.Tasks;
using Xunit;

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
    public async Task WhenAll_NonGenericTask()
    {
        await (Task.CompletedTask, Task.CompletedTask);
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
