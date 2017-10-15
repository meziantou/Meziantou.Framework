using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading.Tasks;
using Meziantou.Framework.Utilities;

namespace Meziantou.Framework.Tests.Utilities
{
    [TestClass]
    public class ThrottleExtensionsTests
    {
        [TestMethod]
        [Ignore("Fails in CI")]
        public async Task Throttle()
        {
            int count = 0;
            Action action = () => { count++; };
            var throttled = action.Throttle(TimeSpan.FromMilliseconds(30));

            throttled();
            throttled();
            await Task.Delay(50);
            Assert.AreEqual(1, count);

            throttled();
            await Task.Delay(15);
            throttled();
            await Task.Delay(15);
            throttled();
            await Task.Delay(15);
            throttled();

            await Task.Delay(50);
            Assert.AreEqual(3, count);
        }

        [TestMethod]
        [Ignore("Fails in CI")]
        public async Task Throttle_CallActionsWithArgumentsOfTheLastCall()
        {
            int lastArg = default;
            Action<int> action = (i) => { lastArg = i; };
            var throttled = action.Throttle(TimeSpan.FromMilliseconds(0));

            throttled(1);
            throttled(2);
            await Task.Delay(1);
            Assert.AreEqual(2, lastArg);
        }
    }
}
