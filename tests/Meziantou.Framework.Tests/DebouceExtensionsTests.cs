using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public class DebouceExtensionsTests
    {
        [Fact]
        public void Debounce_CallActionsWithArgumentsOfTheLastCall()
        {
            using var resetEvent = new ManualResetEventSlim(false);
            int lastArg = default;
            int count = 0;
            var debounced = DebounceExtensions.Debounce<int>(i =>
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
