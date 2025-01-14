using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Meziantou.Framework.Tests;

public class ThrottleExtensionsTests
{
    [Fact]
    public void Throttle_CallActionsWithArgumentsOfTheLastCall()
    {
        var timeProvider = new FakeTimeProvider();
        using var resetEvent = new ManualResetEventSlim(initialState: false);
        int lastArg = default;
        var count = 0;
        var throttle = ThrottleExtensions.Throttle<int>(i =>
        {
            lastArg = i;
            count++;
            resetEvent.Set();
        }, TimeSpan.FromMilliseconds(200), timeProvider);

        throttle(1);
        timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        throttle(2);
        timeProvider.Advance(TimeSpan.FromMilliseconds(100));

        resetEvent.Wait();
        Assert.Equal(1, count);
        Assert.Equal(2, lastArg);
    }
}
