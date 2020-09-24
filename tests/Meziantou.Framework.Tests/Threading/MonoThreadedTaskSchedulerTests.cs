using System;
using System.Threading;
using System.Threading.Tasks;
using Meziantou.Framework.Threading;
using Xunit;

namespace Meziantou.Framework.Tests.Threading
{
    public sealed class MonoThreadedTaskSchedulerTests : IDisposable
    {
        private const string ThreadName = "Test";

        private readonly MonoThreadedTaskScheduler _taskScheduler;
        private int _count;

        public MonoThreadedTaskSchedulerTests()
        {
            _taskScheduler = new MonoThreadedTaskScheduler(ThreadName);
        }

        public void Dispose()
        {
            _taskScheduler.Dispose();
        }

        [Fact]
        public async Task SequentialEnqueue()
        {
            const int Count = 1000;
            for (var i = 0; i < Count; i++)
            {
                await EnqueueTask();
            }

            Assert.Equal(Count, _count);
        }

        [Fact]
        public async Task ParallelEnqueue()
        {
            const int Count = 1000;
            var tasks = new Task[Count];
            for (var i = 0; i < Count; i++)
            {
                tasks[i] = EnqueueTask();
            }

            await Task.WhenAll(tasks);
            Assert.Equal(Count, _count);
        }

        private Task EnqueueTask()
        {
            return Task.Factory.StartNew(() =>
            {
                Assert.Equal(ThreadName, Thread.CurrentThread.Name);
                _count++;
            }, CancellationToken.None, TaskCreationOptions.None, _taskScheduler);
        }
    }
}
