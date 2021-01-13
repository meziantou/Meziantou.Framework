using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Xunit;

namespace Meziantou.Framework.WPF.Tests
{
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

            while ((dispatcher = Volatile.Read(ref dispatcher)) == null)
            {
                await Task.Delay(1);
            }

            Assert.NotEqual(t.ManagedThreadId, Thread.CurrentThread.ManagedThreadId);
            await dispatcher.SwitchToDispatcherThread();
            Assert.Equal(t.ManagedThreadId, Thread.CurrentThread.ManagedThreadId);

            dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
        }
    }
}
