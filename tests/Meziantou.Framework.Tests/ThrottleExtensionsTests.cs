using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Tests;

public class ThrottleExtensionsTests
{
    [Fact]
    public void Throttle_CallActionsWithArgumentsOfTheLastCall()
    {
        using var resetEvent = new ManualResetEventSlim(initialState: false);
        int lastArg = default;
        var count = 0;
        var throttled = ThrottleExtensions.Throttle<int>(i =>
        {
            lastArg = i;
            count++;
            resetEvent.Set();
        }, TimeSpan.FromMilliseconds(200));

        throttled(1);
        throttled(2);

        resetEvent.Wait();
        count.Should().Be(1);
        lastArg.Should().Be(2);
    }
}
