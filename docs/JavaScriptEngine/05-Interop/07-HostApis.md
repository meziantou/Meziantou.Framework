---
title: Console, timers and other host APIs
description: Give the scripts the globals that browsers and Node.js provide, such as console, setTimeout and queueMicrotask.
---

A realm has the built-ins of ECMAScript only. `console`, `setTimeout`, `setInterval`, `queueMicrotask`, `fetch`,
`require`... are APIs of the *host*, the browser or Node.js, not of the language: a script that uses them throws a
`ReferenceError` until your application defines them. Define only what your scripts need: each global you add is a
way for the scripts to reach your application.

To run scripts in HTML documents, with the DOM, `console`, the timers and `fetch`, use `Meziantou.Framework.WebPlatform`
with `Meziantou.Framework.WebPlatform.JavaScript`: it provides the APIs of a browser on top of this engine.

## Provide a console

`console` is an object whose methods receive any values. This one writes them to a `TextWriter`:

<!-- ref:../../../samples/Meziantou.Framework.JavaScriptEngine.Samples/HostConsole.cs;region=JavaScriptEngine_HostConsole;format=md-fenced-code-block;dedent=true -->
```csharp
/// <summary>A <c>console</c> object whose methods write their arguments to a <see cref="TextWriter"/>.</summary>
public static class HostConsole
{
    /// <summary>Adds <c>console</c> to the global object of the realm. Call it inside a scope of the engine.</summary>
    public static void Install(JsRealm realm, TextWriter output, int maxMessageLength = 10_000)
    {
        var console = realm.CreateObject();
        foreach (var level in (string[])["log", "info", "warn", "error", "debug"])
        {
            console.SetProperty(level, realm.CreateFunction(level, 0, (_, arguments) =>
            {
                var message = new StringBuilder();
                foreach (var argument in arguments)
                {
                    if (message.Length > 0)
                    {
                        message.Append(' ');
                    }

                    // JsValue.ToString() runs no code of the script: an object is "[object Array]"...
                    // engine.ToString(argument) would call its toString method, which can throw or never return.
                    message.Append(argument.ToString());
                    if (message.Length > maxMessageLength)
                    {
                        message.Length = maxMessageLength;
                        message.Append('…');
                        break;
                    }
                }

                output.WriteLine($"[{level}] {message}");
                return JsValue.Undefined;
            }));
        }

        realm.GlobalObject.SetProperty("console", console);
    }
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_Console;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();
using var output = new StringWriter();
using (engine.Enter())
{
    HostConsole.Install(realm, output);
}

realm.Execute("console.log('total:', 42, [1, 2], { toString() { throw new Error('not called'); } }); console.error('failed');");
Assert.Equal("[log] total: 42 [object Array] [object Object]\n[error] failed\n", output.ToString().ReplaceLineEndings("\n"));
```
<!-- endref -->

- `argument.ToString()` formats a value without running code of the script: a string is its text, an object is
  `[object Array]`, `[object Object]`... `engine.ToString(argument)` calls the `toString` method of an object, as
  `String(value)` does: it runs code of the script, which can throw or loop. Prefer the first one for untrusted
  scripts, or read the properties you want to show yourself.
- Bound the size of the messages: a script can log in a loop. The [limits](../07-LimitsAndSandboxing.md) of the
  engine stop the loop, not the messages it has written.

## Provide timers and microtasks

The engine runs code only when your application calls it, on the thread that calls it: there is no background thread
and no timer. `queueMicrotask` is a job of the engine, which [RunJobs](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.RunJobs) runs with the jobs of the promises. A timer is a
callback that the host calls later: keep the callbacks and their due times, and call them from your own event loop,
each followed by [RunJobs](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.RunJobs):

<!-- ref:../../../samples/Meziantou.Framework.JavaScriptEngine.Samples/HostEventLoop.cs;region=JavaScriptEngine_HostEventLoop;format=md-fenced-code-block;dedent=true -->
```csharp
/// <summary>
/// An event loop that gives <c>setTimeout</c>, <c>clearTimeout</c> and <c>queueMicrotask</c> to the scripts, and runs the
/// timers and the jobs on the thread that calls <see cref="RunAsync"/>.
/// </summary>
public sealed class HostEventLoop(JsEngine engine, TimeProvider timeProvider)
{
    private readonly PriorityQueue<Timer, (DateTimeOffset DueTime, long Order)> _timers = new();
    private readonly HashSet<int> _activeTimers = [];
    private int _lastTimerId;
    private long _order;

    /// <summary>Adds the functions to the global object of the realm. Call it inside a scope of the engine.</summary>
    public void Install(JsRealm realm)
    {
        var global = realm.GlobalObject;
        global.SetProperty("setTimeout", realm.CreateFunction("setTimeout", 2, (_, arguments) =>
        {
            if (arguments.Length is 0 || !arguments[0].IsCallable)
                throw realm.CreateException(JsErrorType.TypeError, "The callback must be a function");

            var delay = arguments.Length > 1 ? engine.ToNumber(arguments[1]) : 0;
            var id = ++_lastTimerId;
            var dueTime = timeProvider.GetUtcNow() + TimeSpan.FromMilliseconds(double.IsFinite(delay) ? Math.Max(0, delay) : 0);
            _activeTimers.Add(id);
            _timers.Enqueue(new Timer(id, arguments[0], arguments.Length > 2 ? arguments[2..].ToArray() : []), (dueTime, _order++));
            return id;
        }));

        global.SetProperty("clearTimeout", realm.CreateFunction("clearTimeout", 1, (_, arguments) =>
        {
            if (arguments.Length > 0 && arguments[0].IsNumber)
            {
                _activeTimers.Remove((int)arguments[0].AsNumber());
            }

            return JsValue.Undefined;
        }));

        // A microtask is a job of the engine: it runs with the jobs of the promises, before the next timer
        global.SetProperty("queueMicrotask", realm.CreateFunction("queueMicrotask", 1, (_, arguments) =>
        {
            if (arguments.Length is 0 || !arguments[0].IsCallable)
                throw realm.CreateException(JsErrorType.TypeError, "The callback must be a function");

            var callback = arguments[0];
            engine.EnqueueJob(() => engine.Call(callback, JsValue.Undefined));
            return JsValue.Undefined;
        }));
    }

    /// <summary>Runs the jobs, then each timer when it is due, followed by the jobs it starts, until no timer is left.</summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        engine.RunJobs();
        while (_timers.TryPeek(out _, out var priority))
        {
            var delay = priority.DueTime - timeProvider.GetUtcNow();
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, timeProvider, cancellationToken);
            }

            var timer = _timers.Dequeue();
            if (_activeTimers.Remove(timer.Id))
            {
                engine.Call(timer.Callback, JsValue.Undefined, timer.Arguments);
                engine.RunJobs();
            }
        }
    }

    private sealed record Timer(int Id, JsValue Callback, JsValue[] Arguments);
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_EventLoop;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();
var eventLoop = new HostEventLoop(engine, TimeProvider.System);
using (engine.Enter())
{
    eventLoop.Install(realm);
}

realm.Execute("""
    globalThis.log = [];
    setTimeout(name => log.push(name), 20, 'timer 20 ms');
    const canceled = setTimeout(() => log.push('canceled'), 10);
    setTimeout(() => {
      log.push('timer 0 ms');
      Promise.resolve().then(() => log.push('promise of the timer'));
    }, 0);
    queueMicrotask(() => log.push('microtask'));
    clearTimeout(canceled);
    log.push('script');
    """);

// The jobs run after the script, and after each timer
await eventLoop.RunAsync();
Assert.Equal("script,microtask,timer 0 ms,promise of the timer,timer 20 ms", realm.Execute("log.join()").AsString());
```
<!-- endref -->

- The order is the order of a browser: the script, then its microtasks and promise jobs, then each timer when it is
  due, followed by the jobs it starts.
- The loop awaits the delays, but it calls the engine from one place: after an `await`, it can continue on another
  thread, which is allowed because no other thread uses the engine at the same time. Never call the engine from a
  `System.Threading.Timer` callback: it would run concurrently with the loop.
- The [TimeProvider](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.TimeProvider) of the loop can be the one of the options of the engine, so that `Date.now()` and the timers
  use the same clock.
- The loop ends when no timer is left. A script that sets a timer from each timer never ends: cancel the loop, and set
  the limits of the engine, for untrusted scripts.

To await a .NET `Task` from a script, settle a promise from the same loop: see
[Async code and promises](../06-AsyncAndPromises.md#await-a-net-task-from-a-script).

## Other APIs

- `structuredClone`, `atob`, `btoa`, `TextEncoder`, `TextDecoder`, `URL`: implement the ones you need in .NET with
  [Functions](04-Functions.md), or in JavaScript with a script that you run first in each realm (a polyfill). A
  [prepared script](../04-RunningCode/02-Scripts.md#reuse-a-compiled-script) avoids parsing it for each realm.
- `require`: see [CommonJS modules](../04-RunningCode/06-CommonJsModules.md).
- `fetch` and the file system: expose the operations that the scripts need, not a general access. Check every URL or
  path they give, as you would check the input of a user.
