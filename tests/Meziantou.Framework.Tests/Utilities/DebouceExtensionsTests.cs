using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading.Tasks;
using Meziantou.Framework.Utilities;

namespace Meziantou.Framework.Tests.Utilities
{
    [TestClass]
    public class DebouceExtensionsTests
    {
        [TestMethod]
        public async Task DebounceTests()
        {
            int count = 0;
            Action action = () => { count++; };
            var debounced = action.Debounce(TimeSpan.FromMilliseconds(30));

            debounced();
            debounced();
            await Task.Delay(70);
            Assert.AreEqual(1, count);

            debounced();
            await Task.Delay(15);
            debounced();
            await Task.Delay(15);
            debounced();
            await Task.Delay(15);
            debounced();

            await Task.Delay(50);
            Assert.AreEqual(2, count);
        }

        [TestMethod]
        public async Task Debounce_CallActionsWithArgumentsOfTheLastCall()
        {
            int lastArg = default;
            Action<int> action = (i) => { lastArg = i; };
            var debounced = action.Debounce(TimeSpan.FromMilliseconds(0));

            debounced(1);
            debounced(2);
            await Task.Delay(1);
            Assert.AreEqual(2, lastArg);
        }
    }
}
