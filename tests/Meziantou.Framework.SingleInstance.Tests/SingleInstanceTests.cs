using System.IO.Pipes;

namespace Meziantou.Framework.Tests;

public sealed class SingleInstanceTests
{
    [Fact]
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
    public void TestSingleInstance_NotifyFirstInstanceReturnsFalseWhenNobodyIsListening()
    {
        using var singleInstance = new SingleInstance(Guid.NewGuid())
        {
            ClientConnectionTimeout = TimeSpan.FromMilliseconds(200),
        };

        Assert.False(singleInstance.NotifyFirstInstance(["a", "b"]));
    }

    [Fact]
    public void TestSingleInstance_ServerIsStartedByDefault()
    {
        using var singleInstance = new SingleInstance(Guid.NewGuid());

        Assert.True(singleInstance.StartServer);
        Assert.True(singleInstance.StartApplication());
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

    [Fact]
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

    [Fact]
    public void TestSingleInstance_StartApplicationAfterDisposeThrows()
    {
        var singleInstance = new SingleInstance(Guid.NewGuid())
        {
            StartServer = false,
        };
        Assert.True(singleInstance.StartApplication());
        singleInstance.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = singleInstance.StartApplication());
    }

    [Fact]
    public void TestSingleInstance_DisposeIsIdempotent()
    {
        var singleInstance = new SingleInstance(Guid.NewGuid())
        {
            StartServer = false,
        };
        Assert.True(singleInstance.StartApplication());

        singleInstance.Dispose();
        singleInstance.Dispose();
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
