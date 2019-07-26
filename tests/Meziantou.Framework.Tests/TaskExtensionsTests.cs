using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public sealed class TaskExtensionsTests
    {
        [Fact]
        public void ForgetTest_SuccessfullyCompleted()
        {
            var task = Task.FromResult(0);
            task.Forget(); // Should not throw exception
        }

        [Fact]
        public void ForgetTest_Faulted()
        {
            var task = Task.FromException(new Exception(""));
            task.Forget(); // Should not throw exception
        }

        [Fact]
        public void ForgetTest_Canceled()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var task = Task.FromCanceled(cts.Token);
            task.Forget(); // Should not throw exception
        }
    }
}
