using FluentAssertions;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.Tests;

public sealed class SingleInstanceTests
{
    [RunIfFact(FactOperatingSystem.Windows)]
    public async Task TestSingleInstance_NotifyFirstInstance()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var applicationId = Guid.NewGuid();
        using var singleInstance = new SingleInstance(applicationId);
        singleInstance.StartApplication().Should().BeTrue("it should start the instance");

        // Be sure the server is ready
        await Task.Delay(50);

        var events = new List<SingleInstanceEventArgs>();
        singleInstance.NewInstance += SingleInstance_NewInstance;

        singleInstance.NotifyFirstInstance(new[] { "a", "b", "c" }).Should().BeTrue("it should notify first instance (1)");
        await Task.Delay(50);
        singleInstance.NotifyFirstInstance(new[] { "123" }).Should().BeTrue("it should notify the first instance (2)");

        while (!cts.Token.IsCancellationRequested && events.Count < 2)
        {
            await Task.Delay(50);
        }

        events.Should().HaveCount(2);
        var orderedEvents = events.OrderBy(args => args.Arguments.Length).ToList();
        orderedEvents[0].Arguments.Should().Equal(new[] { "123" });
        orderedEvents[1].Arguments.Should().Equal(new[] { "a", "b", "c" });

        void SingleInstance_NewInstance(object sender, SingleInstanceEventArgs e)
        {
            sender.Should().Be(singleInstance);
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

        singleInstance.StartApplication().Should().BeTrue();
        singleInstance.StartApplication().Should().BeTrue();

        // Need to run on another thread because the lock is re-entrant
        var isStarted = false;
        var t = new Thread(() =>
        {
            using var singleInstance2 = new SingleInstance(applicationId);
            isStarted = singleInstance2.StartApplication();
        });
        t.Start();
        t.Join();

        Volatile.Read(ref isStarted).Should().BeFalse("the second instance should not be able to start");
    }
}
