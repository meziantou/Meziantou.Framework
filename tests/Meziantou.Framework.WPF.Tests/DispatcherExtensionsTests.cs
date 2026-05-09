using System.Windows.Threading;
using Xunit;

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
            await Task.Delay(1);
        }

        var currentDispatcher = Volatile.Read(ref dispatcher);
        Assert.NotNull(currentDispatcher);

        Assert.NotEqual(t.ManagedThreadId, Environment.CurrentManagedThreadId);
        await currentDispatcher.SwitchToDispatcherThread();
        Assert.Equal(t.ManagedThreadId, Environment.CurrentManagedThreadId);

        currentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
    }
}
