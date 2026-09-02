using Microsoft.Extensions.Time.Testing;

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

    [Fact]
    public void Throttle_UsesAConsistentSetOfArguments()
    {
        var timeProvider = new FakeTimeProvider();
        var observed = new List<(int First, int Second)>();
        var throttled = ((Action<int, int>)((first, second) => observed.Add((first, second))))
            .Throttle(TimeSpan.FromSeconds(1), timeProvider);

        // Every call passes a matching pair, so the invocation must observe a matching pair
        for (var i = 0; i < 100; i++)
        {
            throttled(i, i);
        }

        timeProvider.Advance(TimeSpan.FromSeconds(2));
        SpinWait.SpinUntil(() => observed.Count > 0, TimeSpan.FromSeconds(5));

        Assert.Single(observed);
        Assert.Equal(observed[0].First, observed[0].Second);
    }

    [Fact]
    public void Throttle_KeepsWorkingAfterTheActionThrows()
    {
        var timeProvider = new FakeTimeProvider();
        var callCount = 0;
        var throttled = ((Action)(() =>
        {
            Interlocked.Increment(ref callCount);
            throw new InvalidOperationException("boom");
        })).Throttle(TimeSpan.FromSeconds(1), timeProvider);

        throttled();
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        Assert.True(
            SpinWait.SpinUntil(() => Volatile.Read(ref callCount) >= 1, TimeSpan.FromSeconds(30)),
            "The action was not invoked");

        // The first invocation threw; the throttle must not be stuck. It is reset after the action
        // returns, which is after the counter is incremented, so the call below can still be
        // throttled by the invocation that just threw: keep calling until one gets through.
        Assert.True(
            SpinWait.SpinUntil(
                () =>
                {
                    throttled();
                    timeProvider.Advance(TimeSpan.FromSeconds(2));
                    return Volatile.Read(ref callCount) >= 2;
                },
                TimeSpan.FromSeconds(30)),
            "The throttle is stuck after the action threw");
    }
}
