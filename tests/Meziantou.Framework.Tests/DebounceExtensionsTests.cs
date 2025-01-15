using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Meziantou.Framework.Tests;

public sealed class DebounceExtensionsTests
{
    [Fact]
    public void Debounce_CallActionsWithArgumentsOfTheLastCall()
    {
        var timeProvider = new FakeTimeProvider();

        using var resetEvent = new ManualResetEventSlim(initialState: false);
        var lastArg = 0;
        var count = 0;
        var debounced = DebounceExtensions.Debounce<int>(i =>
        {
            lastArg = i;
            Interlocked.CompareExchange(ref lastArg, i, 0);
            Interlocked.Increment(ref count);
            resetEvent.Set();
        }, TimeSpan.FromMilliseconds(200), timeProvider);

        debounced(1);
        timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        debounced(2);
        timeProvider.Advance(TimeSpan.FromMilliseconds(150));
        Assert.False(resetEvent.Wait(TimeSpan.Zero));
        timeProvider.Advance(TimeSpan.FromMilliseconds(50));

        resetEvent.Wait();
        Assert.Equal(1, count);
        Assert.Equal(2, lastArg);
    }
}
