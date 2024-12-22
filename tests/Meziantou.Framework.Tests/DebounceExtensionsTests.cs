using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Tests;

public sealed class DebounceExtensionsTests
{
    [Fact]
    public void Debounce_CallActionsWithArgumentsOfTheLastCall()
    {
        using var resetEvent = new ManualResetEventSlim(initialState: false);
        var lastArg = 0;
        var count = 0;
        var debounced = DebounceExtensions.Debounce<int>(i =>
        {
            lastArg = i;
            Interlocked.CompareExchange(ref lastArg, i, 0);
            Interlocked.Increment(ref count);
            resetEvent.Set();
        }, TimeSpan.FromMilliseconds(200));

        debounced(1);
        debounced(2);

        resetEvent.Wait();
        count.Should().Be(1);
        lastArg.Should().Be(2);
    }
}
