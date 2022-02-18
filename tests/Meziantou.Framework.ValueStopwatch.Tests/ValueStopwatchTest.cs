using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public class ValueStopwatchTest
    {
        [Fact]
        public void IsActiveIsFalseForDefaultValueStopwatch()
        {
            default(ValueStopwatch).IsActive.Should().BeFalse();
        }

        [Fact]
        public void IsActiveIsTrueWhenValueStopwatchStartedWithStartNew()
        {
            ValueStopwatch.StartNew().IsActive.Should().BeTrue();
        }

        [Fact]
        public void GetElapsedTimeThrowsIfValueStopwatchIsDefaultValue()
        {
            var stopwatch = default(ValueStopwatch);
            new Func<object>(() => stopwatch.GetElapsedTime()).Should().ThrowExactly<InvalidOperationException>();
        }

        [Fact]
        public async Task GetElapsedTimeReturnsTimeElapsedSinceStart()
        {
            var stopwatch = ValueStopwatch.StartNew();
            await Task.Delay(200);
            var elapsed = stopwatch.GetElapsedTime();
            elapsed.Should().BeCloseTo(200.Milliseconds(), precision: TimeSpan.FromSeconds(3));
        }
    }
}
