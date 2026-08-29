using Meziantou.Xunit;

namespace Meziantou.Framework.Tests;

public sealed class SingleInstanceTests
{
    [Fact, RunIf(TestOperatingSystems.Windows)]
    public async Task TestSingleInstance_NotifyFirstInstance()
    {
        var applicationId = Guid.NewGuid();
        using var singleInstance = new SingleInstance(applicationId);

        var events = new List<SingleInstanceEventArgs>();
        var senders = new List<object?>();
        var bothReceived = new TaskCompletionSource();

        // Subscribed before StartApplication, so no notification can arrive unobserved
        singleInstance.NewInstance += SingleInstance_NewInstance;

        Assert.True(singleInstance.StartApplication());
        Assert.True(singleInstance.NotifyFirstInstance(["a", "b", "c"]));
        Assert.True(singleInstance.NotifyFirstInstance(["123"]));

        await bothReceived.Task.WaitAsync(TimeSpan.FromSeconds(30));

        lock (events)
        {
            Assert.HasCount(2, events);
            Assert.All(senders, sender => Assert.Equal(singleInstance, sender));

            var orderedEvents = events.OrderBy(args => args.Arguments.Length).ToList();
            Assert.Equal(["123"], orderedEvents[0].Arguments);
            Assert.Equal(["a", "b", "c"], orderedEvents[1].Arguments);
        }

        void SingleInstance_NewInstance(object? sender, SingleInstanceEventArgs e)
        {
            lock (events)
            {
                senders.Add(sender);
                events.Add(e);
                if (events.Count is 2)
                {
                    bothReceived.TrySetResult();
                }
            }
        }
    }

    [Fact]
    public void TestSingleInstance()
    {
        var applicationId = Guid.NewGuid();
        using var singleInstance = new SingleInstance(applicationId)
        {
            StartServer = false,
        };
        Assert.True(singleInstance.StartApplication());
        Assert.True(singleInstance.StartApplication());

        // Need to run on another thread because the lock is re-entrant
        var isStarted = false;
        var t = new Thread(() =>
        {
            using var singleInstance2 = new SingleInstance(applicationId);
            isStarted = singleInstance2.StartApplication();
        });
        t.Start();
        t.Join();
        Assert.False(Volatile.Read(ref isStarted));
    }
}
