using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public sealed class SingleInstanceTests
    {
        [RunIfFact(FactOperatingSystem.Windows)]
        public async Task TestSingleInstance_NotifyFirstInstance()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var applicationId = Guid.NewGuid();
            using var singleInstance = new SingleInstance(applicationId);
            Assert.True(singleInstance.StartApplication(), "Cannot start the instance");

            // Be sure the server is ready
            await Task.Delay(50);

            var events = new List<SingleInstanceEventArgs>();
            singleInstance.NewInstance += SingleInstance_NewInstance;

            Assert.True(singleInstance.NotifyFirstInstance(new[] { "a", "b", "c" }), "Cannot notify first instance 1");
            await Task.Delay(50);
            Assert.True(singleInstance.NotifyFirstInstance(new[] { "123" }), "Cannot notify first instance 2");

            while (!cts.Token.IsCancellationRequested && events.Count < 2)
            {
                await Task.Delay(50);
            }

            Assert.Equal(2, events.Count);
            var orderedEvents = events.OrderBy(args => args.Arguments.Length).ToList();
            Assert.Equal(new[] { "123" }, orderedEvents[0].Arguments);
            Assert.Equal(new[] { "a", "b", "c" }, orderedEvents[1].Arguments);

            void SingleInstance_NewInstance(object sender, SingleInstanceEventArgs e)
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

            Assert.False(Volatile.Read(ref isStarted), "Second instance should not be able to start");
        }
    }
}
