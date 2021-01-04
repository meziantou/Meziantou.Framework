using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Xunit;

namespace Meziantou.Framework.WPF.Tests
{
    public sealed class DispatcherExtensionsTests
    {
        [Fact(Timeout = 5000)]
        public async Task SwitchToUIThreadTests()
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var currentThreadId = Thread.CurrentThread;

            var manualResetEvent = new ManualResetEventSlim();
            var t = Task.Run(async () =>
            {
                Assert.NotEqual(currentThreadId, Thread.CurrentThread);

                var switchTask = dispatcher.SwitchToUiThread();
                manualResetEvent.Set();
                await switchTask;

                Assert.Equal(currentThreadId, Thread.CurrentThread);
            });

            manualResetEvent.Wait();
            DoEvents();

            await t;
        }

        private static void DoEvents()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new DispatcherOperationCallback(ExitFrame), frame);
            Dispatcher.PushFrame(frame);
        }

        private static object ExitFrame(object frame)
        {
            ((DispatcherFrame)frame).Continue = false;
            return null;
        }
    }
}
