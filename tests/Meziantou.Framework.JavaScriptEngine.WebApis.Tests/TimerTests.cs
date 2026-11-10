namespace Meziantou.Framework.JavaScriptEngine.WebApis.Tests;

public sealed class TimerTests
{
    [Fact]
    public void SetTimeout_RunsInTheOrderOfTheDelays()
    {
        var timeProvider = new ManualTimeProvider();
        using var context = new WebApisTestContext(timeProvider: timeProvider);
        context.Run("""
            const log = [];
            setTimeout(() => log.push('b'), 20);
            setTimeout((x, y) => log.push(x + y), 10, 'a', '1');
            setTimeout(() => log.push('a2'), 10);
            setTimeout(function () { log.push(this === globalThis); }, 0);
            """);
        Assert.Equal("true", context.Run("log.join()"));
        Assert.Equal(3, context.Host.ActiveTimerCount);

        timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        Assert.Equal("true,a1,a2", context.Run("log.join()"));

        timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        Assert.Equal("true,a1,a2,b", context.Run("log.join()"));
        Assert.Equal(0, context.Host.ActiveTimerCount);
    }

    [Fact]
    public void SetTimeout_ReturnsPositiveIncreasingIds()
    {
        using var context = new WebApisTestContext();
        Assert.Equal("1,2,3", context.Run("[setTimeout(() => {}), setInterval(() => {}), setTimeout(() => {})].join()"));
        context.Run("clearTimeout(1); clearInterval(2); clearTimeout(3)");
        Assert.Equal(0, context.Host.ActiveTimerCount);
    }

    [Fact]
    public void SetTimeout_TimeoutConversion()
    {
        var timeProvider = new ManualTimeProvider();
        using var context = new WebApisTestContext(timeProvider: timeProvider);
        context.Run("""
            const log = [];
            setTimeout(() => log.push('string'), '10');
            setTimeout(() => log.push('negative'), -5);
            setTimeout(() => log.push('nan'), NaN);
            setTimeout(() => log.push('wrapped'), 2 ** 32 + 10);
            """);
        Assert.Equal("negative,nan", context.Run("log.join()"));
        timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        Assert.Equal("negative,nan,string,wrapped", context.Run("log.join()"));
    }

    [Fact]
    public void ClearTimeout()
    {
        var timeProvider = new ManualTimeProvider();
        using var context = new WebApisTestContext(timeProvider: timeProvider);
        context.Run("""
            const log = [];
            const id = setTimeout(() => log.push('cleared'), 10);
            setTimeout(() => log.push('kept'), 10);
            clearTimeout(id);
            clearTimeout(12345);
            clearTimeout();
            clearTimeout('not a number');
            """);
        timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        Assert.Equal("kept", context.Run("log.join()"));
    }

    [Fact]
    public void SetInterval_RunsUntilCleared()
    {
        var timeProvider = new ManualTimeProvider();
        using var context = new WebApisTestContext(timeProvider: timeProvider);
        context.Run("""
            let count = 0;
            const id = setInterval(() => { if (++count === 3) clearInterval(id); }, 10);
            """);
        for (var i = 0; i < 5; i++)
        {
            timeProvider.Advance(TimeSpan.FromMilliseconds(10));
            context.Engine.RunJobs();
        }

        Assert.Equal("3", context.Run("count"));
        Assert.Equal(0, context.Host.ActiveTimerCount);
    }

    [Fact]
    public void SetTimeout_NestingLevelClampsTheTimeout()
    {
        // https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#timers: a timer set by a timer nested more than 5
        // levels deep waits at least 4 ms
        var timeProvider = new ManualTimeProvider();
        using var context = new WebApisTestContext(timeProvider: timeProvider);
        context.Run("""
            let depth = 0;
            (function nest() { setTimeout(() => { depth++; nest(); }, 0); })();
            """);
        Assert.Equal("6", context.Run("depth"));

        timeProvider.Advance(TimeSpan.FromMilliseconds(3));
        Assert.Equal("6", context.Run("depth"));
        timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal("7", context.Run("depth"));
    }

    [Fact]
    public void SetTimeout_InvalidHandler()
    {
        using var context = new WebApisTestContext();
        Assert.Equal("TypeError", context.GetErrorName("setTimeout('code', 0)"));
        Assert.Equal("TypeError", context.GetErrorName("setInterval({}, 0)"));
        Assert.Equal("TypeError", context.GetErrorName("setTimeout()"));
        Assert.Equal(0, context.Host.ActiveTimerCount);
    }

    [Fact]
    public void SetInterval_ExceptionIsReportedAndTheIntervalContinues()
    {
        var timeProvider = new ManualTimeProvider();
        using var context = new WebApisTestContext(timeProvider: timeProvider);
        context.Run("let count = 0; setInterval(() => { count++; throw new Error('boom'); }, 10);");
        timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        Assert.Throws<JsException>(context.Engine.RunJobs);
        Assert.Equal(1, context.Host.ActiveTimerCount);

        timeProvider.Advance(TimeSpan.FromMilliseconds(10));
        Assert.Throws<JsException>(context.Engine.RunJobs);
        Assert.Equal("2", context.Run("count"));
    }

    [Fact]
    public async Task RunUntilIdleAsync_RunsTheTimersAndTheJobs()
    {
        using var context = new WebApisTestContext();
        context.Realm.Execute("""
            globalThis.log = [];
            setTimeout(() => {
              log.push('timeout');
              Promise.resolve().then(() => log.push('promise'));
              setTimeout(() => log.push('nested'), 5);
            }, 5);
            let count = 0;
            const id = setInterval(() => { if (++count === 3) clearInterval(id); }, 1);
            """);

        await context.Host.RunUntilIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(0, context.Host.ActiveTimerCount);
        Assert.Equal("timeout,promise,nested", context.Run("log.join()"));
        Assert.Equal("3", context.Run("count"));
    }

    [Fact]
    public async Task RunUntilIdleAsync_Cancellation()
    {
        using var context = new WebApisTestContext();
        context.Realm.Execute("setInterval(() => {}, 1)");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.Host.RunUntilIdleAsync(cancellation.Token));
        Assert.Equal(1, context.Host.ActiveTimerCount);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, -1)]
    [InlineData(1.9, 1)]
    [InlineData(-1.9, -1)]
    [InlineData(2147483648, -2147483648)]
    [InlineData(4294967306, 10)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    public void ToWebIdlLong(double value, int expected)
    {
        Assert.Equal(expected, JsWebApiHost.ToWebIdlLong(value));
    }
}
