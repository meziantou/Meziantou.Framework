using System.Diagnostics;

namespace Meziantou.Framework.Tests;

public class ValueStopwatchTest
{
    [Fact]
    public void IsActiveIsFalseForDefaultValueStopwatch()
    {
        Assert.False(default(ValueStopwatch).IsActive);
    }

    [Fact]
    public void IsActiveIsTrueWhenValueStopwatchStartedWithStartNew()
    {
        Assert.True(ValueStopwatch.StartNew().IsActive);
    }

    [Fact]
    public void GetElapsedTimeThrowsIfValueStopwatchIsDefaultValue()
    {
        var stopwatch = default(ValueStopwatch);
        Assert.Throws<InvalidOperationException>(() => stopwatch.GetElapsedTime());
    }

    [Fact]
    public void GetElapsedTimeFromTimestampsConvertsTicksUsingStopwatchFrequency()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), ValueStopwatch.GetElapsedTime(0, Stopwatch.Frequency));
        Assert.Equal(TimeSpan.FromSeconds(0.5), ValueStopwatch.GetElapsedTime(0, Stopwatch.Frequency / 2));
        Assert.Equal(TimeSpan.FromSeconds(2), ValueStopwatch.GetElapsedTime(Stopwatch.Frequency, Stopwatch.Frequency * 3));
    }

    [Fact]
    public void GetElapsedTimeFromIdenticalTimestampsIsZero()
    {
        var timestamp = ValueStopwatch.GetTimestamp();
        Assert.Equal(TimeSpan.Zero, ValueStopwatch.GetElapsedTime(timestamp, timestamp));
    }

    [Fact]
    public async Task GetElapsedTimeReturnsTimeElapsedSinceStart()
    {
        var stopwatch = ValueStopwatch.StartNew();
        await Task.Delay(300);
        var elapsed = stopwatch.GetElapsedTime();
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(200), $"{elapsed}"); // Allow some margin
    }
}
