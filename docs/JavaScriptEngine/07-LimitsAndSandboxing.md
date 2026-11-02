---
title: Limits and sandboxing
description: Bound the time, the work, the memory and the recursion of the scripts, cancel them, forbid eval, and decide what untrusted code can reach.
---

## Limit the execution of the scripts

[JsEngineOptions](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions) sets the limits of each entry into JavaScript: a script run by [Execute](cref:Meziantou.Framework.JavaScriptEngine.JsRealm.Execute), a [Call](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.Call) or a [Construct](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.Construct)
from .NET, a job run by [RunJobs](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.RunJobs). A script that exceeds a limit is stopped with a [JsTerminationException](cref:Meziantou.Framework.JavaScriptEngine.JsTerminationException):

<!-- ref:../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_Limits;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine(new JsEngineOptions
{
    Timeout = TimeSpan.FromSeconds(2),
    MaxStatements = 1_000_000,
    MemoryLimit = 64 * 1024 * 1024,
    RecursionLimit = 500,
    RegExpTimeout = TimeSpan.FromMilliseconds(100),
});
var realm = engine.CreateRealm();

// A limit stops the script: the script cannot catch the termination, and its finally blocks do not run
var exception = Assert.Throws<JsTerminationException>(() => realm.Execute("try { for (;;) {} } finally { globalThis.cleanedUp = true; }"));
Assert.Equal(JsTerminationReason.StatementLimit, exception.Reason);
Assert.Equal("undefined", realm.Execute("typeof cleanedUp").AsString());

// Each entry into JavaScript gets the full limits again
Assert.Equal(4, realm.Execute("2 + 2").AsNumber());

// A too deep recursion is a RangeError, which the scripts can catch
Assert.Equal("RangeError", realm.Execute("function f() { return 1 + f(); } try { f(); } catch (e) { e.name }").AsString());
```
<!-- endref -->

| Option | Limits | When exceeded |
| --- | --- | --- |
| `Timeout` | The duration of an entry. | [JsTerminationException](cref:Meziantou.Framework.JavaScriptEngine.JsTerminationException), [Reason](cref:Meziantou.Framework.JavaScriptEngine.JsTerminationException.Reason) is `Timeout` |
| [MaxStatements](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.MaxStatements) | The work of an entry: the engine counts the iterations of the loops and the calls. | [JsTerminationException](cref:Meziantou.Framework.JavaScriptEngine.JsTerminationException), [Reason](cref:Meziantou.Framework.JavaScriptEngine.JsTerminationException.Reason) is [StatementLimit](cref:Meziantou.Framework.JavaScriptEngine.JsTerminationReason.StatementLimit) |
| `MemoryLimit` | The bytes allocated by the thread during an entry (`GC.GetAllocatedBytesForCurrentThread`). | [JsTerminationException](cref:Meziantou.Framework.JavaScriptEngine.JsTerminationException), [Reason](cref:Meziantou.Framework.JavaScriptEngine.JsTerminationException.Reason) is `MemoryLimit` |
| [RegExpTimeout](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.RegExpTimeout) | The duration of one regular expression match. | [JsTerminationException](cref:Meziantou.Framework.JavaScriptEngine.JsTerminationException), [Reason](cref:Meziantou.Framework.JavaScriptEngine.JsTerminationException.Reason) is `Timeout` |
| [RecursionLimit](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.RecursionLimit) | The depth of the calls, 10,000 by default. | A `RangeError`, which the script can catch |

- The limits are off by default, except [RecursionLimit](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.RecursionLimit).
- A [JsTerminationException](cref:Meziantou.Framework.JavaScriptEngine.JsTerminationException) cannot be caught by JavaScript code: neither the `catch` nor the `finally` blocks of the
  script run. The realm can still run code afterwards, but the state that the stopped script left may be incomplete:
  discard the realm when the script is not trusted. The generators, async functions and modules that it stopped are
  completed: the promises of the async functions and of the modules are rejected with an error such as
  `Error: The execution was terminated: The script exceeded its time limit`.
- `MemoryLimit` counts what the thread allocates, not the memory that the script keeps: a script that allocates and
  drops many objects reaches it too, and so does the allocation of your .NET functions. The built-ins check it before
  they create a large string or array, and the regular expressions check it before they grow their backtracking stack.
- The regular expressions use a backtracking matcher: some patterns, such as `/^(a+)+$/`, take an exponential time on
  some strings. The matcher checks the limits and the cancellation while it backtracks and while it scans the string
  for the position where a match can start, so `Timeout`, [RegExpTimeout](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.RegExpTimeout) and the [CancellationToken](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.CancellationToken) stop a long
  match, even one that never backtracks.
- The interpreter does not use the .NET stack for JavaScript calls, so a deep recursion of JavaScript code never
  overflows the stack of the thread; a recursion through .NET functions throws a `RangeError` before it does.

### What the limits cover

An entry includes everything the engine does for it, not only the JavaScript code:

- The parsing and the compilation of the source of [Execute](cref:Meziantou.Framework.JavaScriptEngine.JsRealm.Execute), [ParseModule](cref:Meziantou.Framework.JavaScriptEngine.JsRealm.ParseModule), `eval` and `new Function`: the parser and the
  compiler check the limits periodically, so a huge source is stopped by `Timeout`, `MemoryLimit` or the cancellation
  like a long loop. [JsPreparedScript.Parse](cref:Meziantou.Framework.JavaScriptEngine.JsPreparedScript.Parse) runs outside any entry: parse it in an execution scope (below)
  to bound it. Bound the length of the source yourself when you do not want to parse a huge one at all.
- The built-ins called by .NET: `engine.Call(repeat, "abcd", 1 << 26)` is stopped by `MemoryLimit` as
  `'abcd'.repeat(1 << 26)` is in a script.
- The long operations of the built-ins (sorting a large typed array, normalizing or splitting a long string, joining a
  large array...), which check the limits while they run.
- `Atomics.wait`: a blocking wait is bounded by the time left to the entry, and stops when the execution is canceled.
  Set [CanBlock](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.CanBlock) to `false` to forbid the blocking waits: `Atomics.wait` then throws a `TypeError`, as on the
  main thread of a browser.

The straight-line code between two loops or calls is not counted by `MaxStatements`: it is bounded by the length of the
source, whose parsing is checked.

### Nested calls

A .NET function called by a script, which calls JavaScript again (`engine.Call`), is part of the same entry: the limits
are not restarted. When the nested call is terminated, the termination stays until the outermost entry returns: a .NET
function that catches the [JsTerminationException](cref:Meziantou.Framework.JavaScriptEngine.JsTerminationException) cannot let the script continue, the engine throws it again
when the function returns, and at the next call into JavaScript.

### Jobs

[RunJobs](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.RunJobs) runs each job in its own entry: the limits apply to each job, not to the whole drain. Promise jobs that keep
enqueuing jobs (`function f() { Promise.resolve().then(f); }`) keep [RunJobs](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.RunJobs) running forever. Run the jobs in an
execution scope to bound the drain as a whole. Once the execution is canceled, [RunJobs](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.RunJobs) throws before it runs the
next job, which stays in the queue.

## Limit a request

[EnterExecutionScope](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.EnterExecutionScope) makes the calls made until the scope is disposed one entry into JavaScript. The
[JsExecutionLimits](cref:Meziantou.Framework.JavaScriptEngine.JsExecutionLimits) of the scope apply to them as a whole, in addition to the limits of the options, which apply to the
scope as a whole too. It gives a request its own cancellation token and budget without changing the options of a
shared engine, and bounds a script together with the jobs it enqueues:

<!-- ref:../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_ExecutionScope;format=md-fenced-code-block;dedent=true -->
```csharp
// One engine serves several requests: its options apply to every entry
using var engine = new JsEngine(new JsEngineOptions { MaxStatements = 1_000_000 });
var realm = engine.CreateRealm();

// A request gets its own token and budget, shared by the script and by the jobs it enqueues
using var requestAborted = new CancellationTokenSource();
var exception = Assert.Throws<JsTerminationException>(() =>
{
    using var scope = engine.EnterExecutionScope(new JsExecutionLimits
    {
        CancellationToken = requestAborted.Token,
        Timeout = TimeSpan.FromSeconds(5),
        MaxStatements = 10_000,
    });

    realm.Execute("function tick() { Promise.resolve().then(tick); } tick();");
    engine.RunJobs(); // the jobs keep enqueuing jobs: the limits of the scope stop the drain
});
Assert.Equal(JsTerminationReason.StatementLimit, exception.Reason);

// The next request starts with a new budget
Assert.Equal(2, realm.Execute("1 + 1").AsNumber());
```
<!-- endref -->

- A scope whose token is already canceled does not start: [EnterExecutionScope](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.EnterExecutionScope) throws a [JsTerminationException](cref:Meziantou.Framework.JavaScriptEngine.JsTerminationException).
- The token of the scope can be canceled from any thread. The engine and the .NET functions that call
  `engine.CheckInterrupt()` check it with the limits.
- The scopes nest: an inner scope adds its limits until it is disposed. Dispose them in the reverse order, on the thread
  that entered them.
- A scope also makes the engine the engine of the thread, as [Enter](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.Enter) does.

## Change the limits of an engine

The engine copies its options when it is created: an instance of [JsEngineOptions](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions) can configure several engines,
and changing it afterwards does not change them. `engine.Options` is the copy of the engine:

- The limits ([Timeout](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.Timeout), [MaxStatements](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.MaxStatements), [MemoryLimit](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.MemoryLimit), [RecursionLimit](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.RecursionLimit), [RegExpTimeout](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.RegExpTimeout) and
  [CancellationToken](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.CancellationToken)) can be changed on `engine.Options`, between two entries or from a .NET function: the change
  applies from the next check of the limits.
- The [Debugger](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.Debugger) can be changed on `engine.Options` too: the code compiled
  afterwards uses the new debugger (see [Debugging](04-RunningCode/05-Debugging.md)).
- The other options ([Host](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.Host), [ModuleLoader](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.ModuleLoader), [TimeProvider](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.TimeProvider), [TimeZone](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.TimeZone), [DefaultLocale](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.DefaultLocale)...) configure the
  engine for its whole life: setting them on `engine.Options` throws an `InvalidOperationException`.

## Cancel a script

The [CancellationToken](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.CancellationToken) of the options stops the running script when it is canceled, from any thread, for instance
when the user cancels the operation or the request ends. A .NET function that works for a long time calls
`engine.CheckInterrupt()` to check the limits and the cancellation while it runs:

<!-- ref:../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_Cancellation;format=md-fenced-code-block;dedent=true -->
```csharp
using var cancellation = new CancellationTokenSource();
using var engine = new JsEngine(new JsEngineOptions { CancellationToken = cancellation.Token });
var realm = engine.CreateRealm();
using (engine.Enter())
{
    realm.GlobalObject.SetProperty("processItem", realm.CreateFunction("processItem", 1, (_, arguments) =>
    {
        if (engine.ToNumber(arguments[0]) >= 3)
        {
            cancellation.Cancel(); // e.g. the user clicked Cancel, from another thread
        }

        // A .NET function that works for a long time checks the limits and the cancellation itself
        engine.CheckInterrupt();
        return JsValue.Undefined;
    }));
}

var exception = Assert.Throws<JsTerminationException>(() => realm.Execute("for (let i = 0; ; i++) processItem(i);"));
Assert.Equal(JsTerminationReason.Canceled, exception.Reason);
```
<!-- endref -->

The engine checks the token periodically while JavaScript code runs. Once it is canceled, the engine stops every
script at its next check and does not start new entries: set a new token in `engine.Options` before the next work, or
create a new engine. To cancel one request of an engine that serves several of them, give the request its own token
with an execution scope ([Limit a request](#limit-a-request)).

## Forbid eval

`eval`, `new Function(...)` and the constructors of the generator and async functions compile code given as a string.
To forbid them, throw from [JsHost.EnsureCanCompileStrings](cref:Meziantou.Framework.JavaScriptEngine.JsHost.EnsureCanCompileStrings):

<!-- ref:../../samples/Meziantou.Framework.JavaScriptEngine.Samples/NoStringCompilationHost.cs;region=JavaScriptEngine_NoStringCompilationHost;format=md-fenced-code-block;dedent=true -->
```csharp
/// <summary>Forbids <c>eval</c>, <c>new Function(...)</c> and the other ways to run code given as a string.</summary>
public sealed class NoStringCompilationHost : JsHost
{
    public override void EnsureCanCompileStrings(JsEngine engine, JsRealm realm, IReadOnlyList<string> parameterStrings, string bodyString, string codeString, JsCompilationType compilationType, IReadOnlyList<JsValue> parameterArgs, JsValue bodyArg)
    {
        throw realm.CreateException(JsErrorType.EvalError, "Code generation from strings is disabled");
    }
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_NoStringCompilation;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine(new JsEngineOptions { Host = new NoStringCompilationHost() });
var realm = engine.CreateRealm();

Assert.Equal("EvalError,EvalError", realm.Execute("""
    const errors = [];
    try { eval('1 + 1'); } catch (e) { errors.push(e.name); }
    try { new Function('return 1'); } catch (e) { errors.push(e.name); }
    errors.join()
    """).AsString());
```
<!-- endref -->

The method receives the code and how it is compiled ([JsCompilationType](cref:Meziantou.Framework.JavaScriptEngine.JsCompilationType)): it can also allow some of them, as a
content security policy does.

## Run untrusted code

A realm has only the built-ins of ECMAScript. A script has no access to the files, the network, the environment
variables, the processes or the .NET types: it reaches your application only through the objects and functions you
add. To run code you do not trust:

- Set [Timeout](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.Timeout), [MaxStatements](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.MaxStatements) and [MemoryLimit](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.MemoryLimit), and a [CancellationToken](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.CancellationToken) tied to the request.
- Expose the smallest API you can, and validate every argument of your .NET functions as you would validate the input
  of a web API.
- Give each user or tenant its own realm, or its own engine, so that a script cannot see or change what another one
  left in the global object or the built-ins.
- Removing a global property, such as `eval`, does not remove the feature: the scripts can reach the constructors
  through the prototypes (`(function () {}).constructor`). Use the host methods instead, as above.
- Catch [JsTerminationException](cref:Meziantou.Framework.JavaScriptEngine.JsTerminationException) and [JsException](cref:Meziantou.Framework.JavaScriptEngine.JsException) around the calls into JavaScript, and let the other exceptions,
  which come from your .NET code, go to your error handling.
