---
title: Debugging and inspecting scripts
description: Stop at breakpoints, step through the statements, read the variables, evaluate code in a paused script and measure the code coverage.
---

## Pause at breakpoints and step

A [JsBreakpointDebugger](cref:Meziantou.Framework.JavaScriptEngine.JsBreakpointDebugger) set in [JsEngineOptions.Debugger](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.Debugger) pauses the scripts at its breakpoints and at the `debugger`
statements. The callback given to its constructor is called while the script is paused: it reads the variables,
evaluates code at the paused statement, then returns how the script resumes.

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_Debugger;format=md-fenced-code-block;dedent=true -->
```csharp
var pauses = new List<string>();
var debugger = new JsBreakpointDebugger(pause =>
{
    // The script waits here: inspect its state, then return how it resumes
    var local = pause.Event.GetScopes()[0];
    pauses.Add($"{pause.Reason} {pause.Event.FunctionName}@{pause.Location.Line}: {string.Join(", ", local.Variables)}");
    if (pause.Reason is JsPauseReason.Step)
    {
        // Evaluate code in the paused function: it reads and changes its variables
        Assert.Equal(20, pause.Event.Evaluate("amount").AsNumber());
        pause.Event.Evaluate("amount = 25");
        return JsStepAction.Continue;
    }

    return JsStepAction.StepOver;
});

// Pause at line 3 when quantity > 1
debugger.AddBreakpoint("cart.js", line: 3, condition: "quantity > 1");

using var engine = new JsEngine(new JsEngineOptions { Debugger = debugger });
var realm = engine.CreateRealm();
realm.Execute("""
    var total = 0;
    function add(price, quantity) {
      let amount = price * quantity;
      total += amount;
    }
    add(10, 2);
    add(3, 1);
    """, "cart.js");

Assert.Equal(
    [
        "Breakpoint add@3: price = 10, quantity = 2, arguments = [object Arguments], amount (uninitialized)",
        "Step add@4: price = 10, quantity = 2, arguments = [object Arguments], amount = 20",
    ],
    pauses);
Assert.Equal(28, realm.Execute("total").AsNumber());
```
<!-- endref -->

The [JsDebugPause](cref:Meziantou.Framework.JavaScriptEngine.JsDebugPause) given to the callback has the [Reason](cref:Meziantou.Framework.JavaScriptEngine.JsDebugPause.Reason) of the pause (a breakpoint, a `debugger` statement, a
step or a pause request), the [Breakpoint](cref:Meziantou.Framework.JavaScriptEngine.JsDebugPause.Breakpoint) that was hit, and the paused statement in [Event](cref:Meziantou.Framework.JavaScriptEngine.JsDebugPause.Event).
To subclass the debugger instead of giving a callback, override [OnPaused](cref:Meziantou.Framework.JavaScriptEngine.JsBreakpointDebugger.OnPaused).

### Breakpoints

[AddBreakpoint](cref:Meziantou.Framework.JavaScriptEngine.JsBreakpointDebugger.AddBreakpoint) takes the URL of the code (the name given to the script or the module, or the
`//# sourceURL=` comment of eval code) and a 1-based line:

- Without a column, the script pauses before each statement that starts on the line. Give the column of a statement to
  pause at this statement only.
- A condition is code evaluated at the statement: the script pauses when its value is truthy. A condition that throws
  does not pause the script.
- [IsEnabled](cref:Meziantou.Framework.JavaScriptEngine.JsBreakpoint.IsEnabled) turns a breakpoint off without removing it, and [HitCount](cref:Meziantou.Framework.JavaScriptEngine.JsBreakpoint.HitCount) counts the pauses.
- [RemoveBreakpoint](cref:Meziantou.Framework.JavaScriptEngine.JsBreakpointDebugger.RemoveBreakpoint) and [ClearBreakpoints](cref:Meziantou.Framework.JavaScriptEngine.JsBreakpointDebugger.ClearBreakpoints) remove them. The breakpoints can be changed from any
  thread, while a script runs too.
- Set [PauseOnDebuggerStatements](cref:Meziantou.Framework.JavaScriptEngine.JsBreakpointDebugger.PauseOnDebuggerStatements) to false to ignore the `debugger` statements.
- [RequestPause](cref:Meziantou.Framework.JavaScriptEngine.JsBreakpointDebugger.RequestPause), called from any thread, pauses the script at the next statement that runs, for instance
  to break into a script that runs too long.

### Steps

The callback returns a [JsStepAction](cref:Meziantou.Framework.JavaScriptEngine.JsStepAction):

- [Continue](cref:Meziantou.Framework.JavaScriptEngine.JsStepAction.Continue) runs until the next breakpoint.
- [StepInto](cref:Meziantou.Framework.JavaScriptEngine.JsStepAction.StepInto) pauses at the next statement, in the functions it calls too.
- [StepOver](cref:Meziantou.Framework.JavaScriptEngine.JsStepAction.StepOver) pauses at the next statement of the function, or of its caller once it returns.
- [StepOut](cref:Meziantou.Framework.JavaScriptEngine.JsStepAction.StepOut) pauses at the next statement of the caller.

The steps compare the call depths of the statements. A step that leaves the code being run, at the end of a script or
at an `await`, pauses at the next statement that runs: the next script, or a promise job run by `engine.RunJobs()`.

## Inspect a paused script

The statement of a pause ([JsDebugEvent](cref:Meziantou.Framework.JavaScriptEngine.JsDebugEvent)) gives the state of the script:

- [GetScopes](cref:Meziantou.Framework.JavaScriptEngine.JsDebugEvent.GetScopes) returns the scopes of the statement, the innermost first. A [JsDebugScope](cref:Meziantou.Framework.JavaScriptEngine.JsDebugScope) has a
  [Kind](cref:Meziantou.Framework.JavaScriptEngine.JsDebugScope.Kind): the blocks and the `catch` clauses, the function of the statement (`Local`: its parameters, its
  vars, its top-level `let` and `const`, and its `arguments` object), the enclosing functions (`Closure`), the module,
  the `let`, `const` and `class` declarations of the scripts (`Script`), then the global object (`Global`). The scopes
  without variable are omitted.
- The [Variables](cref:Meziantou.Framework.JavaScriptEngine.JsDebugScope.Variables) of a scope have a name and a value. A `let`, `const` or `class` declaration that has not run
  yet is not initialized ([IsInitialized](cref:Meziantou.Framework.JavaScriptEngine.JsDebugVariable.IsInitialized) is false). The global scope and the `with` statements list no
  variable: their [BindingObject](cref:Meziantou.Framework.JavaScriptEngine.JsDebugScope.BindingObject) is the object that has them.
- [Evaluate](cref:Meziantou.Framework.JavaScriptEngine.JsDebugEvent.Evaluate) runs code at the statement, like a direct `eval` written there: the code reads and changes the
  variables of the function, and uses its `this`, `new.target`, `super` and the private names of its class. It returns
  the value of the code, and throws a [JsException](cref:Meziantou.Framework.JavaScriptEngine.JsException) when the code throws or has a syntax error. The code is
  evaluated even when the host forbids the compilation of strings: the debugger runs it, not the script.
- [GetCallStack](cref:Meziantou.Framework.JavaScriptEngine.JsDebugEvent.GetCallStack) returns the JavaScript frames, the statement first, then its callers. Each [JsDebugFrame](cref:Meziantou.Framework.JavaScriptEngine.JsDebugFrame) has
  its [Location](cref:Meziantou.Framework.JavaScriptEngine.JsDebugFrame.Location), its [FunctionName](cref:Meziantou.Framework.JavaScriptEngine.JsDebugFrame.FunctionName), its scopes, and evaluates code at its position: the variables of a caller
  can be read and changed too.

The event, its frames and its scopes are valid until the callback returns: using them after throws an
`InvalidOperationException`. The values read from them can be kept.

## Pause a script

The callbacks are called on the thread of the engine, and the script waits until they return: a debugger pauses the
script by waiting in the callback, for instance until the user resumes it from another thread.

- The callback can use the engine: read the properties of objects, call functions, run code in the realm of the event.
  The code it runs raises no event.
- The time and the allocations of the callback, and the code it runs, do not count in the limits of the script. A
  pause never exceeds the [Timeout](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.Timeout). The [CancellationToken](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.CancellationToken) still stops the script once the callback returns.
- An exception thrown by the callback is thrown at the statement. To stop the script, throw a
  [JsTerminationException](cref:Meziantou.Framework.JavaScriptEngine.JsTerminationException): the script cannot catch it.

## Write your own debugger

[JsBreakpointDebugger](cref:Meziantou.Framework.JavaScriptEngine.JsBreakpointDebugger) is built on [JsDebugger](cref:Meziantou.Framework.JavaScriptEngine.JsDebugger), which is called before each statement of the scripts runs,
and when a `debugger` statement runs. Derive from it to observe the execution, for instance to record the lines that run
(the code coverage):

<!-- ref:../../../samples/Meziantou.Framework.JavaScriptEngine.Samples/CoverageDebugger.cs;region=JavaScriptEngine_CoverageDebugger;format=md-fenced-code-block;dedent=true -->
```csharp
/// <summary>Records the lines that run: the code coverage of the scripts.</summary>
public sealed class CoverageDebugger : JsDebugger
{
    public HashSet<(string? Url, int Line)> CoveredLines { get; } = [];

    protected override void OnStatement(JsDebugEvent debugEvent)
    {
        var location = debugEvent.Location;
        CoveredLines.Add((location.Url, location.Line));
    }
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_CoverageDebuggerUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var debugger = new CoverageDebugger();
using (var engine = new JsEngine(new JsEngineOptions { Debugger = debugger }))
{
    engine.CreateRealm().Execute("""
        function check(value) {
          if (value < 0) {
            return 'negative';
          }
          return 'positive';
        }
        check(1);
        """, "check.js");
}

// Line 3 did not run
Assert.Contains(("check.js", 5), debugger.CoveredLines);
Assert.DoesNotContain(("check.js", 3), debugger.CoveredLines);
```
<!-- endref -->

- [OnStatement](cref:Meziantou.Framework.JavaScriptEngine.JsDebugger.OnStatement) is called before each statement; [OnDebuggerStatement](cref:Meziantou.Framework.JavaScriptEngine.JsDebugger.OnDebuggerStatement) for each `debugger` statement. Blocks,
  labels and function declarations have no event of their own, nor do the expression bodies of arrow functions.
- The [JsDebugEvent](cref:Meziantou.Framework.JavaScriptEngine.JsDebugEvent) gives the [Location](cref:Meziantou.Framework.JavaScriptEngine.JsDebugEvent.Location) of the statement, the [FunctionName](cref:Meziantou.Framework.JavaScriptEngine.JsDebugEvent.FunctionName), the [Realm](cref:Meziantou.Framework.JavaScriptEngine.JsDebugEvent.Realm), the
  [CallDepth](cref:Meziantou.Framework.JavaScriptEngine.JsDebugEvent.CallDepth), and the scopes, the call stack and the evaluation of code described above.
  [CaptureStackTrace](cref:Meziantou.Framework.JavaScriptEngine.JsDebugEvent.CaptureStackTrace) returns the call stack as text.
- To step, compare the call depths: *step into* stops at the next statement, *step over* at the next statement whose
  depth is less than or equal to the current one, *step out* at the next statement whose depth is smaller.

## Generators and async functions

The statements of a generator or an async function raise their events when they run. After a `yield` or an `await`,
they come from the code that resumes the function, such as `engine.RunJobs()` for an `await`, with the call depth of
that code. The call stack of these statements starts at the function: its frames are the ones of the code that resumed
it.

## Performance

The code is compiled for the debugger when the engine has one. The engine adds an instruction at the start of each
statement, stores every variable where the debugger can find it by name (as if each function contained a direct
`eval`), and compiles the nested functions with their enclosing code instead of on their first call. This code runs
slower and uses more memory. The scripts of an engine without debugger are compiled without any of this and run at full
speed, so set the debugger before the code is compiled. A [JsPreparedScript](cref:Meziantou.Framework.JavaScriptEngine.JsPreparedScript) is compiled without it: an engine that
has a debugger compiles it again when it runs it.

There is no debugging protocol: Chrome DevTools and Visual Studio Code cannot attach to the engine. Build the user
interface of your debugger on this API.
