using System;
using System.Threading.Tasks;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public class DebouceExtensionsTests
    {
        [Fact(Skip = "Fails in CI")]
        public async Task DebounceTests()
        {
            var count = 0;
            var debounced = DebounceExtensions.Debounce(() => count++, TimeSpan.FromMilliseconds(30));

            debounced();
            debounced();
            await Task.Delay(70).ConfigureAwait(false);
            Assert.Equal(1, count);

            debounced();
            await Task.Delay(15).ConfigureAwait(false);
            debounced();
            await Task.Delay(15).ConfigureAwait(false);
            debounced();
            await Task.Delay(15).ConfigureAwait(false);
            debounced();

            await Task.Delay(50).ConfigureAwait(false);
            Assert.Equal(2, count);
        }

        [Fact(Skip = "Fails in CI")]
        public async Task Debounce_CallActionsWithArgumentsOfTheLastCall()
        {
            int lastArg = default;
            var debounced = DebounceExtensions.Debounce<int>(i => lastArg = i, TimeSpan.FromMilliseconds(0));

            debounced(1);
            debounced(2);
            await Task.Delay(1).ConfigureAwait(false);
            Assert.Equal(2, lastArg);
        }
    }
}
