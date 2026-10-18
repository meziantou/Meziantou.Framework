using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Tests.Scripting;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#timers, animation frames and High Resolution Time, with a fake clock.</summary>
public sealed class TimerTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly List<string> _log = [];

    public void Dispose() => _contexts.Dispose();

    private Window CreateWindow(Scripting.RecordingScriptEngine? engine = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, ScriptEngine = engine, UnhandledScriptError = (_, error) => _log.Add("error " + error.Message) });
        context.Navigate("https://example.com/", _ => "<body>");
        RunUntilIdle();
        return context.Window;
    }

    private void RunUntilIdle() => _contexts.EventLoop.RunUntilIdle();

    private void Advance(int milliseconds)
    {
        _clock.Advance(TimeSpan.FromMilliseconds(milliseconds));
        RunUntilIdle();
    }

    [Fact]
    public void SetTimeout_RunsTheCallbackOnceTheDelayElapsed()
    {
        var window = CreateWindow();
        var id = window.SetTimeout(() => _log.Add("a"), 100);
        Assert.True(id > 0);
        Advance(99);
        Assert.Empty(_log);

        Advance(1);
        Assert.Equal(["a"], _log);
        Advance(1000);
        Assert.Equal(["a"], _log);
    }

    [Fact]
    public void Timers_WithTheSameDelayRunInOrder()
    {
        var window = CreateWindow();
        window.SetTimeout(() => _log.Add("b"), 10);
        window.SetTimeout(() => _log.Add("a"), 5);
        window.SetTimeout(() => _log.Add("c"), 10);
        Advance(10);
        Assert.Equal(["a", "b", "c"], _log);
    }

    [Fact]
    public void ClearTimeout_CancelsTheTimer()
    {
        var window = CreateWindow();
        var id = window.SetTimeout(() => _log.Add("a"), 10);
        window.ClearTimeout(id);
        Advance(10);
        Assert.Empty(_log);
    }

    [Fact]
    public void SetInterval_RepeatsUntilCleared()
    {
        var window = CreateWindow();
        var id = 0;
        id = window.SetInterval(() =>
        {
            _log.Add("tick");
            if (_log.Count == 3)
            {
                window.ClearInterval(id);
            }
        }, 10);

        for (var i = 0; i < 10; i++)
        {
            Advance(10);
        }

        Assert.Equal(["tick", "tick", "tick"], _log);
    }

    [Fact]
    public void FunctionHandler_GetsTheArguments()
    {
        var window = CreateWindow();
        window.SetTimeout(new FunctionCallback(args =>
        {
            _log.Add(string.Join(",", args));
            return null;
        }), 0, 1, "x");
        Advance(0);
        Assert.Equal(["1,x"], _log);
    }

    [Fact]
    public void NestedTimers_AreClampedTo4Milliseconds()
    {
        var window = CreateWindow();
        var times = new List<double>();
        void Nest()
        {
            times.Add(window.Performance.Now());
            if (times.Count < 8)
            {
                window.SetTimeout(Nest, 0);
            }
        }

        window.SetTimeout(Nest, 0);
        for (var i = 0; i < 40; i++)
        {
            Advance(1);
        }

        var delays = times.Zip(times.Skip(1), (a, b) => b - a).ToArray();
        Assert.Equal(7, delays.Length);
        Assert.All(delays.Take(4), d => Assert.True(d < 4, $"{d}"));
        Assert.All(delays.Skip(5), d => Assert.Equal(4, d));
    }

    [Fact]
    public void ExceptionInACallback_IsReported()
    {
        var window = CreateWindow();
        window.SetTimeout(() => throw new InvalidOperationException("boom"), 0);
        window.SetTimeout(() => _log.Add("after"), 0);
        Advance(0);
        Assert.Equal(["error boom", "after"], _log);
    }

    [Fact]
    public void StringHandler_RunsWithTheScriptEngine()
    {
        var engine = new Scripting.RecordingScriptEngine();
        engine.Behaviors["run()"] = script =>
        {
            _log.Add("run " + script.Url);
            return null;
        };
        var window = CreateWindow(engine);
        window.SetTimeout("run()", 5);
        Advance(5);
        Assert.Equal(["run https://example.com/"], _log);
    }

    [Fact]
    public void StringHandler_DoesNothingWithoutScriptEngine()
    {
        var window = CreateWindow();
        window.SetTimeout("run()", 0);
        Advance(0);
        Assert.Empty(_log);
    }

    [Fact]
    public void RequestAnimationFrame_RunsTheCallbacksAtTheNextFrame()
    {
        var window = CreateWindow();
        Advance(5);
        var times = new List<double>();
        window.RequestAnimationFrame(times.Add);
        var cancelled = window.RequestAnimationFrame(_ => _log.Add("cancelled"));
        window.RequestAnimationFrame(times.Add);
        window.CancelAnimationFrame(cancelled);
        Assert.Empty(times);

        Advance(17);
        Assert.Equal(2, times.Count);
        Assert.Equal(times[0], times[1]);
        Assert.Empty(_log);

        // The frames run at a fixed rate
        window.RequestAnimationFrame(times.Add);
        Advance(17);
        Assert.Equal(3, times.Count);
        Assert.InRange(times[2] - times[0], 16, 17.5);
    }

    [Fact]
    public void Performance_UsesTheClock()
    {
        var window = CreateWindow();
        var start = window.Performance.Now();
        _clock.Advance(TimeSpan.FromMilliseconds(12.34));
        Assert.Equal(12.3, window.Performance.Now() - start, 3);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), window.Performance.TimeOrigin);
    }

    [Fact]
    public void AbortSignalTimeout_UsesTheClock()
    {
        var signal = AbortSignal.Timeout(CreateWindow(), 50);
        Advance(49);
        Assert.False(signal.Aborted);
        Advance(1);
        Assert.True(signal.Aborted);
    }

    [Fact]
    public async Task WaitForNextTimerAsync_WaitsUntilTheClockIsAdvanced()
    {
        var window = CreateWindow();
        window.SetTimeout(() => _log.Add("a"), 100);
        var wait = _contexts.EventLoop.WaitForNextTimerAsync(TestContext.Current.CancellationToken);
        Assert.False(wait.IsCompleted);

        _clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.True(await wait);
        RunUntilIdle();
        Assert.Equal(["a"], _log);
        Assert.False(await _contexts.EventLoop.WaitForNextTimerAsync(TestContext.Current.CancellationToken));
    }
}
