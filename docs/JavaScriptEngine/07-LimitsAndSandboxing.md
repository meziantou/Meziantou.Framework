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
  discard the realm when the script is not trusted.
- `MemoryLimit` counts what the thread allocates, not the memory that the script keeps: a script that allocates and
  drops many objects reaches it too, and so does the allocation of your .NET functions. The built-ins check it before
  they create a large string or array.
- The interpreter does not use the .NET stack for JavaScript calls, so a deep recursion of JavaScript code never
  overflows the stack of the thread; a recursion through .NET functions throws a `RangeError` before it does.

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

The engine checks the token periodically while JavaScript code runs. The token is the one of the engine for its whole
life: once it is canceled, the engine stops every script at its next check, so create a new engine for the next work.

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
