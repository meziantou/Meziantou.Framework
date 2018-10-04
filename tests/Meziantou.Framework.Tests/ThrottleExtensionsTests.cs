using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests
{
    [TestClass]
    public class ThrottleExtensionsTests
    {
        [TestMethod]
        [Ignore("Fails in CI")]
        public async Task Throttle()
        {
            var count = 0;
            var throttled = ThrottleExtensions.Throttle(() => count++, TimeSpan.FromMilliseconds(30));

            throttled();
            throttled();
            await Task.Delay(50).ConfigureAwait(false);
            Assert.AreEqual(1, count);

            throttled();
            await Task.Delay(15).ConfigureAwait(false);
            throttled();
            await Task.Delay(15).ConfigureAwait(false);
            throttled();
            await Task.Delay(15).ConfigureAwait(false);
            throttled();

            await Task.Delay(50).ConfigureAwait(false);
            Assert.AreEqual(3, count);
        }

        [TestMethod]
        [Ignore("Fails in CI")]
        public async Task Throttle_CallActionsWithArgumentsOfTheLastCall()
        {
            int lastArg = default;
            var throttled = ThrottleExtensions.Throttle<int>((i) => lastArg = i, TimeSpan.FromMilliseconds(0));

            throttled(1);
            throttled(2);
            await Task.Delay(1).ConfigureAwait(false);
            Assert.AreEqual(2, lastArg);
        }
    }
}
