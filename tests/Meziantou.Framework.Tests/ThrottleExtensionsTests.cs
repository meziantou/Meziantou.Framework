using System;
using System.Threading;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public class ThrottleExtensionsTests
    {
        [Fact]
        public void Throttle_CallActionsWithArgumentsOfTheLastCall()
        {
            using var resetEvent = new ManualResetEventSlim(false);
            int lastArg = default;
            int count = 0;
            var debounced = ThrottleExtensions.Throttle<int>(i =>
            {
                lastArg = i;
                count++;
                resetEvent.Set();
            }, TimeSpan.FromMilliseconds(10));

            debounced(1);
            debounced(2);

            resetEvent.Wait();
            Assert.Equal(1, count);
            Assert.Equal(2, lastArg);
        }
    }
}
