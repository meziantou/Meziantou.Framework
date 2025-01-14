using FluentAssertions;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.Tests;

public sealed class SingleInstanceTests
{
    [Fact, RunIf(FactOperatingSystem.Windows)]
    public async Task TestSingleInstance_NotifyFirstInstance()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var applicationId = Guid.NewGuid();
        using var singleInstance = new SingleInstance(applicationId);
        Assert.True(singleInstance.StartApplication());

        // Be sure the server is ready
        await Task.Delay(50);

        var events = new List<SingleInstanceEventArgs>();
        singleInstance.NewInstance += SingleInstance_NewInstance;
        Assert.True(singleInstance.NotifyFirstInstance(["a", "b", "c"]));
        await Task.Delay(50);
        Assert.True(singleInstance.NotifyFirstInstance(["123"]));

        while (!cts.Token.IsCancellationRequested && events.Count < 2)
        {
            await Task.Delay(50);
        }

        events.Should().HaveCount(2);
        var orderedEvents = events.OrderBy(args => args.Arguments.Length).ToList();
        Assert.Equal(["123"], orderedEvents[0].Arguments);
        Assert.Equal(["a", "b", "c"], orderedEvents[1].Arguments);

        void SingleInstance_NewInstance(object sender, SingleInstanceEventArgs e)
        {
            Assert.Equal(singleInstance, sender);
            lock (events)
            {
                events.Add(e);
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
