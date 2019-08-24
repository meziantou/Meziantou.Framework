using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public sealed class SingleInstanceTests : IDisposable
    {
        private readonly SingleInstance _singleInstance;

        public SingleInstanceTests()
        {
            var applicationId = Guid.NewGuid();
            _singleInstance = new SingleInstance(applicationId);
        }

        public void Dispose()
        {
            _singleInstance.Dispose();
        }

        [Fact(Timeout = 10000)]
        public void TestSingleInstance()
        {
            Assert.True(_singleInstance.StartApplication());

            var events = new List<SingleInstanceEventArgs>();
            _singleInstance.NewInstance += SingleInstance_NewInstance;

            Assert.True(_singleInstance.NotifyFirstInstance(new[] { "a", "b", "c" }));
            Assert.True(_singleInstance.NotifyFirstInstance(new[] { "123" }));

            while (events.Count < 2)
            {
                Thread.Sleep(50);
            }

            Assert.Equal(2, events.Count);
            Assert.Equal(new[] { "a", "b", "c" }, events[0].Arguments);
            Assert.Equal(new[] { "123" }, events[1].Arguments);

            void SingleInstance_NewInstance(object sender, SingleInstanceEventArgs e)
            {
                Assert.Equal(_singleInstance, sender);
                events.Add(e);
            }
        }
    }
}
