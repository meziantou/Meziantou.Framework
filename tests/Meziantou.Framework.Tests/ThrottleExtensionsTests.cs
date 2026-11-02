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
        using var invoked = new ManualResetEventSlim(initialState: false);
        var observed = new List<(int First, int Second)>();
        var throttled = ((Action<int, int>)((first, second) =>
        {
            observed.Add((first, second));
            invoked.Set();
        })).Throttle(TimeSpan.FromSeconds(1), timeProvider);

        // Every call passes a matching pair, so the invocation must observe a matching pair
        for (var i = 0; i < 100; i++)
        {
            throttled(i, i);
        }

        timeProvider.Advance(TimeSpan.FromSeconds(2));

        // Advancing the time only queues the invocation to the thread pool. Every test of the process runs
        // in parallel, and the sibling process running the other target framework competes for the same CPU,
        // so the work item can sit in the queue for a long time: waiting for it with a short spin turned a
        // slow agent into a failure. The budget below is not an assertion about the scheduling latency, it
        // only exists so an invocation that never happens fails the test instead of hanging the run.
        Assert.True(invoked.Wait(TimeSpan.FromSeconds(60)), "The throttled action was not invoked");

        // Setting the event happens after the list is updated, so waiting for it publishes the writes
        Assert.Single(observed);
        Assert.Equal(observed[0].First, observed[0].Second);
    }

    [Fact]
    public void Throttle_KeepsWorkingAfterTheActionThrows()
    {
        var timeProvider = new FakeTimeProvider();
        var callCount = 0;
        var errors = new List<Exception>();
        var throttled = ((Action)(() =>
        {
            callCount++;
            throw new InvalidOperationException("boom");
        })).Throttle(TimeSpan.FromSeconds(1), timeProvider, errors.Add);

        // FakeTimeProvider invokes the timer callback synchronously from Advance
        throttled();
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, callCount);

        throttled();
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, callCount);

        Assert.HasCount(2, errors);
        Assert.All(errors, error => Assert.Equal("boom", error.Message));
    }

    [Fact]
    public void Throttle_WithoutErrorHandler_ExceptionIsUnhandled()
    {
        var timeProvider = new FakeTimeProvider();
        var throttled = ((Action)(() => throw new InvalidOperationException("boom"))).Throttle(TimeSpan.FromSeconds(1), timeProvider);

        throttled();

        // The exception propagates to the timer callback: with FakeTimeProvider, to Advance
        Assert.Equal("boom", Assert.Throws<InvalidOperationException>(() => timeProvider.Advance(TimeSpan.FromSeconds(1))).Message);
    }

    [Fact]
    public void Throttle_ReusesASingleTimer()
    {
        var fakeTimeProvider = new FakeTimeProvider();
        var timeProvider = new CountingTimeProvider(fakeTimeProvider);
        var callCount = 0;
        var throttled = ((Action)(() => callCount++)).Throttle(TimeSpan.FromSeconds(1), timeProvider);

        for (var i = 0; i < 100; i++)
        {
            throttled();
            throttled();
            fakeTimeProvider.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(100, callCount);
        Assert.Equal(1, timeProvider.TimersCreated);
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

    [Fact]
    public void Documentation_Throttle_OnError()
    {
        #region ThrottleExtensions_OnError
        var timeProvider = new FakeTimeProvider();
        var errors = new List<Exception>();
        var refresh = ThrottleExtensions.Throttle(
            () => throw new InvalidOperationException("The server is unavailable"),
            TimeSpan.FromSeconds(1),
            timeProvider,
            onError: errors.Add); // For instance, log the exception

        refresh();
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("The server is unavailable", Assert.Single(errors).Message);
        #endregion
    }

    [Fact]
    public async Task Documentation_Throttle()
    {
        #region ThrottleExtensions_Throttle
        var timeProvider = new FakeTimeProvider();
        var saved = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var save = ThrottleExtensions.Throttle<int>(version => saved.TrySetResult(version), TimeSpan.FromSeconds(1), timeProvider);

        // The first call schedules the action; the next calls only update the arguments
        save(1);
        save(2);
        save(3);

        // The action runs once the interval has elapsed, with the last arguments
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(3, await saved.Task);
        #endregion
    }
}
