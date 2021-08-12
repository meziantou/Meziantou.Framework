using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Meziantou.Framework.Threading;
using Xunit;

namespace Meziantou.Framework.Tests.Threading;

public sealed class MonoThreadedTaskSchedulerTests : IDisposable
{
    private const string ThreadName = "Test";

    private readonly MonoThreadedTaskScheduler _taskScheduler;
    private int _count;

    public MonoThreadedTaskSchedulerTests()
    {
        _taskScheduler = new MonoThreadedTaskScheduler(ThreadName);
    }

    public void Dispose()
    {
        _taskScheduler.Dispose();
    }

    [Fact]
    public async Task SequentialEnqueue()
    {
        const int Count = 1000;
        for (var i = 0; i < Count; i++)
        {
            await EnqueueTask();
        }

        _count.Should().Be(Count);
    }

    [Fact]
    public async Task ParallelEnqueue()
    {
        const int Count = 1000;
        var tasks = new Task[Count];
        for (var i = 0; i < Count; i++)
        {
            tasks[i] = EnqueueTask();
        }

        await Task.WhenAll(tasks);
        _count.Should().Be(Count);
    }

    private Task EnqueueTask()
    {
        return Task.Factory.StartNew(() =>
        {
            Thread.CurrentThread.Name.Should().Be(ThreadName);
            _count++;
        }, CancellationToken.None, TaskCreationOptions.None, _taskScheduler);
    }
}
