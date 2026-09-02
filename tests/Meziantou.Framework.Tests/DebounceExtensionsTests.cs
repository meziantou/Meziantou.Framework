using Microsoft.Extensions.Time.Testing;

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

    [Fact]
    public void Debounce_ReusesASingleTimerAcrossCalls()
    {
        var fakeTimeProvider = new FakeTimeProvider();
        var timeProvider = new CountingTimeProvider(fakeTimeProvider);
        var callCount = 0;
        var debounced = ((Action)(() => Interlocked.Increment(ref callCount))).Debounce(TimeSpan.FromSeconds(1), timeProvider);

        for (var i = 0; i < 10_000; i++)
        {
            debounced();
        }

        // One reused timer, not one pending delay per call
        Assert.Equal(1, timeProvider.TimersCreated);

        fakeTimeProvider.Advance(TimeSpan.FromSeconds(2));
        SpinWait.SpinUntil(() => Volatile.Read(ref callCount) > 0, TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref callCount));
    }

    [Fact]
    public void Debounce_InvokesWithTheArgumentsOfTheLastCall()
    {
        var timeProvider = new FakeTimeProvider();
        var observed = new List<(int First, int Second)>();
        var debounced = ((Action<int, int>)((first, second) => observed.Add((first, second))))
            .Debounce(TimeSpan.FromSeconds(1), timeProvider);

        debounced(1, 1);
        debounced(2, 2);
        debounced(3, 3);

        timeProvider.Advance(TimeSpan.FromSeconds(2));
        SpinWait.SpinUntil(() => observed.Count > 0, TimeSpan.FromSeconds(5));

        Assert.Equal([(3, 3)], observed);
    }

    [Fact]
    public void Debounce_CanFireAgainAfterAnInvocation()
    {
        var timeProvider = new FakeTimeProvider();
        var callCount = 0;
        var debounced = ((Action)(() => Interlocked.Increment(ref callCount))).Debounce(TimeSpan.FromSeconds(1), timeProvider);

        debounced();
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        SpinWait.SpinUntil(() => Volatile.Read(ref callCount) >= 1, TimeSpan.FromSeconds(5));

        debounced();
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        SpinWait.SpinUntil(() => Volatile.Read(ref callCount) >= 2, TimeSpan.FromSeconds(5));

        Assert.Equal(2, Volatile.Read(ref callCount));
    }

    private sealed class CountingTimeProvider(TimeProvider inner) : TimeProvider
    {
        private int _timersCreated;

        public int TimersCreated => Volatile.Read(ref _timersCreated);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref _timersCreated);
            return inner.CreateTimer(callback, state, dueTime, period);
        }

        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();
        public override long GetTimestamp() => inner.GetTimestamp();
        public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;
        public override long TimestampFrequency => inner.TimestampFrequency;
    }
}
