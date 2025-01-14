using System.Windows.Threading;
using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.WPF.Tests;

public sealed class DispatcherExtensionsTests
{
    [Fact(Timeout = 95000)]
    public async Task SwitchToUIThreadTests()
    {
        Dispatcher dispatcher = null;
        var t = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            Dispatcher.Run();
        })
        {
            IsBackground = true,
        };
        t.Start();

        while ((dispatcher = Volatile.Read(ref dispatcher)) is null)
        {
            await Task.Delay(1);
        }

        Environment.CurrentManagedThreadId.Should().NotBe(t.ManagedThreadId);
        await dispatcher.SwitchToDispatcherThread();
        Assert.Equal(t.ManagedThreadId, Environment.CurrentManagedThreadId);

        dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
    }
}
