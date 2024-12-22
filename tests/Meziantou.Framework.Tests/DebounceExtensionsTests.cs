using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Tests;

public class DebouceExtensionsTests
{
    [Fact]
    public void Debounce_CallActionsWithArgumentsOfTheLastCall()
    {
        using var resetEvent = new ManualResetEventSlim(initialState: false);
        int lastArg = default;
        var count = 0;
        var debounced = DebounceExtensions.Debounce<int>(i =>
        {
            lastArg = i;
            count++;
            resetEvent.Set();
        }, TimeSpan.FromMilliseconds(200));

        debounced(1);
        debounced(2);

        resetEvent.Wait();
        count.Should().Be(1);
        lastArg.Should().Be(2);
    }
}
