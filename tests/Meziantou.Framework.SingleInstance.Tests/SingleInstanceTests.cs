using System.IO.Pipes;
using Meziantou.Xunit;

namespace Meziantou.Framework.Tests;

public sealed class SingleInstanceTests
{
    [Fact, RunIf(TestOperatingSystems.Windows)]
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

        Assert.HasCount(2, events);
        var orderedEvents = events.OrderBy(args => args.Arguments.Length).ToList();
        Assert.Equal(["123"], orderedEvents[0].Arguments);
        Assert.Equal(["a", "b", "c"], orderedEvents[1].Arguments);

        void SingleInstance_NewInstance(object? sender, SingleInstanceEventArgs e)
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

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public async Task TestSingleInstance_MalformedMessagesDoNotStopTheServer()
    {
        var applicationId = Guid.NewGuid();
        using var singleInstance = new SingleInstance(applicationId);

        var events = new List<SingleInstanceEventArgs>();
        var received = new TaskCompletionSource();
        singleInstance.NewInstance += (sender, e) =>
        {
            lock (events)
            {
                events.Add(e);
            }

            received.TrySetResult();
        };

        Assert.True(singleInstance.StartApplication());

        // Connects and disconnects without writing anything
        await SendRawMessageAsync(singleInstance.PipeName, []);

        // Announces far more arguments than the payload contains
        await SendRawMessageAsync(singleInstance.PipeName, [1, .. BitConverter.GetBytes(42), .. BitConverter.GetBytes(int.MaxValue)]);

        // Announces a negative argument count
        await SendRawMessageAsync(singleInstance.PipeName, [1, .. BitConverter.GetBytes(42), .. BitConverter.GetBytes(-1)]);

        // Stops in the middle of the arguments
        await SendRawMessageAsync(singleInstance.PipeName, [1, .. BitConverter.GetBytes(42), .. BitConverter.GetBytes(2)]);

        // Unknown message type
        await SendRawMessageAsync(singleInstance.PipeName, [0xFF]);

        // None of the above may take the process down or stop the server
        Assert.True(singleInstance.NotifyFirstInstance(["still", "alive"]));
        await received.Task.WaitAsync(TimeSpan.FromSeconds(30));

        lock (events)
        {
            var args = Assert.Single(events);
            Assert.Equal(["still", "alive"], args.Arguments);
        }
    }

    private static async Task SendRawMessageAsync(string pipeName, byte[] payload)
    {
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
        await client.ConnectAsync(30_000);
        if (payload.Length > 0)
        {
            await client.WriteAsync(payload);
            await client.FlushAsync();
        }
    }
}
