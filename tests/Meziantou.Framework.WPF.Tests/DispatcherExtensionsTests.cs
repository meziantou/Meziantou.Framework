using System.Windows.Threading;

namespace Meziantou.Framework.WPF.Tests;

public sealed class DispatcherExtensionsTests
{
    [Fact(Timeout = 95000)]
    public async Task SwitchToUIThreadTests()
    {
        Dispatcher? dispatcher = null;
        var t = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            Dispatcher.Run();
        })
        {
            IsBackground = true,
        };
        t.Start();

        while (Volatile.Read(ref dispatcher) is null)
        {
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }

        var currentDispatcher = Volatile.Read(ref dispatcher);
        Assert.NotNull(currentDispatcher);

        Assert.NotEqual(t.ManagedThreadId, Environment.CurrentManagedThreadId);
        await currentDispatcher.SwitchToDispatcherThread();
        Assert.Equal(t.ManagedThreadId, Environment.CurrentManagedThreadId);

        currentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
    }

    [Fact(Timeout = 95000)]
    public async Task SwitchToDispatcherThreadDocumentationExample()
    {
        var dispatcherReady = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            dispatcherReady.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        })
        {
            IsBackground = true,
        };
        thread.Start();
        var dispatcher = await dispatcherReady.Task.WaitAsync(TestContext.Current.CancellationToken);

        #region DispatcherExtensions_SwitchToDispatcherThread
        // Do the slow work on a thread-pool thread
        var lines = await Task.Run(() => Enumerable.Range(1, 1000).Select(i => $"Line {i}").ToList());

        // Continue on the thread of the dispatcher, usually Application.Current.Dispatcher, to update the controls
        await dispatcher.SwitchToDispatcherThread();
        Assert.True(dispatcher.CheckAccess());
        #endregion

        Assert.HasCount(1000, lines);
        dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
    }

    [Fact]
    public void DefaultAwaitableThrows()
    {
        var awaitable = default(DispatcherExtensions.SwitchToUiAwaitable);

        Assert.Throws<InvalidOperationException>(() => awaitable.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => awaitable.OnCompleted(() => { }));
    }

    [Fact(Timeout = 95000)]
    public async Task SwitchToUIThreadAfterShutdownDoesNotHang()
    {
        Dispatcher? dispatcher = null;
        var t = new Thread(() =>
        {
            Volatile.Write(ref dispatcher, Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        })
        {
            IsBackground = true,
        };
        t.Start();

        while (Volatile.Read(ref dispatcher) is null)
        {
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }

        var currentDispatcher = Volatile.Read(ref dispatcher)!;
        currentDispatcher.InvokeShutdown();

        while (!currentDispatcher.HasShutdownFinished)
        {
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }

        // The dispatcher aborts the operation instead of running it. The await must still resume: leaving the
        // continuation unscheduled would make this task hang forever.
        var switchTask = Task.Run(async () => await currentDispatcher.SwitchToDispatcherThread(), TestContext.Current.CancellationToken);

        var completed = await Task.WhenAny(switchTask, Task.Delay(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        Assert.Same(switchTask, completed);
        await switchTask;
    }
}
