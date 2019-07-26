using System;
using System.Threading.Tasks;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public class ThrottleExtensionsTests
    {
        [Fact(Skip = "Fails in CI")]
        public async Task Throttle()
        {
            var count = 0;
            var throttled = ThrottleExtensions.Throttle(() => count++, TimeSpan.FromMilliseconds(30));

            throttled();
            throttled();
            await Task.Delay(50).ConfigureAwait(false);
            Assert.Equal(1, count);

            throttled();
            await Task.Delay(15).ConfigureAwait(false);
            throttled();
            await Task.Delay(15).ConfigureAwait(false);
            throttled();
            await Task.Delay(15).ConfigureAwait(false);
            throttled();

            await Task.Delay(50).ConfigureAwait(false);
            Assert.Equal(3, count);
        }

        [Fact(Skip = "Fails in CI")]
        public async Task Throttle_CallActionsWithArgumentsOfTheLastCall()
        {
            int lastArg = default;
            var throttled = ThrottleExtensions.Throttle<int>((i) => lastArg = i, TimeSpan.FromMilliseconds(0));

            throttled(1);
            throttled(2);
            await Task.Delay(1).ConfigureAwait(false);
            Assert.Equal(2, lastArg);
        }
    }
}
