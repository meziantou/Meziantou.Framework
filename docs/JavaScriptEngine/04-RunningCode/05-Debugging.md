---
title: Debugging and inspecting scripts
description: Stop at breakpoints, step through the statements, measure the code coverage and inspect a running script.
---

## Receive the statements that run

A [JsDebugger](cref:Meziantou.Framework.JavaScriptEngine.JsDebugger) set in [JsEngineOptions.Debugger](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.Debugger) is called before each statement of the scripts runs, and when a
`debugger` statement runs. This debugger records the lines that run (the code coverage) and stops at breakpoints:

<!-- ref:../../../samples/Meziantou.Framework.JavaScriptEngine.Samples/BreakpointDebugger.cs;region=JavaScriptEngine_BreakpointDebugger;format=md-fenced-code-block;dedent=true -->
```csharp
/// <summary>
/// Records the lines that run (the code coverage) and calls <paramref name="onBreakpoint"/> when a statement of a line
/// that has a breakpoint runs, or a <c>debugger</c> statement. The script waits until the callback returns.
/// </summary>
public sealed class BreakpointDebugger(Action<JsDebugEvent> onBreakpoint) : JsDebugger
{
    public HashSet<(string? Url, int Line)> Breakpoints { get; } = [];

    public HashSet<(string? Url, int Line)> CoveredLines { get; } = [];

    protected override void OnStatement(JsDebugEvent debugEvent)
    {
        var location = debugEvent.Location;
        CoveredLines.Add((location.Url, location.Line));
        if (Breakpoints.Contains((location.Url, location.Line)))
        {
            onBreakpoint(debugEvent);
        }
    }

    protected override void OnDebuggerStatement(JsDebugEvent debugEvent) => onBreakpoint(debugEvent);
}
```
<!-- endref -->

Set it in the options of the engine. The script waits while the callback runs, which can read the state of the script:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_Debugger;format=md-fenced-code-block;dedent=true -->
```csharp
var pauses = new List<string>();
JsEngine? engine = null;
var debugger = new BreakpointDebugger(debugEvent =>
{
    // The script waits here: inspect its state, then return to resume it
    var total = debugEvent.Realm.Execute("total").AsNumber();
    pauses.Add($"{debugEvent.FunctionName}@{debugEvent.Location.Line} depth={debugEvent.CallDepth} total={total}");
});
debugger.Breakpoints.Add(("cart.js", 4));

using (engine = new JsEngine(new JsEngineOptions { Debugger = debugger }))
{
    var realm = engine.CreateRealm();
    realm.Execute("""
        var total = 0;
        function add(price, quantity) {
          if (quantity > 0) {
            total += price * quantity;
          }
        }
        add(10, 2);
        add(5, 0);
        add(3, 1);
        debugger;
        """, "cart.js");
}

Assert.Equal(["add@4 depth=2 total=0", "add@4 depth=2 total=20", "@10 depth=1 total=23"], pauses);

// Line 4 did not run for add(5, 0), the coverage has it once
Assert.Contains(("cart.js", 4), debugger.CoveredLines);
Assert.DoesNotContain(("cart.js", 2), debugger.CoveredLines);
```
<!-- endref -->

- [OnStatement](cref:Meziantou.Framework.JavaScriptEngine.JsDebugger.OnStatement) is called before each statement; [OnDebuggerStatement](cref:Meziantou.Framework.JavaScriptEngine.JsDebugger.OnDebuggerStatement) for each `debugger` statement. Blocks,
  labels and function declarations have no event of their own, nor do the expression bodies of arrow functions.
- The [JsDebugEvent](cref:Meziantou.Framework.JavaScriptEngine.JsDebugEvent) gives the [Location](cref:Meziantou.Framework.JavaScriptEngine.JsDebugEvent.Location) of the statement, the [FunctionName](cref:Meziantou.Framework.JavaScriptEngine.JsDebugEvent.FunctionName), the [Realm](cref:Meziantou.Framework.JavaScriptEngine.JsDebugEvent.Realm) and the
  [CallDepth](cref:Meziantou.Framework.JavaScriptEngine.JsDebugEvent.CallDepth). [CaptureStackTrace](cref:Meziantou.Framework.JavaScriptEngine.JsDebugEvent.CaptureStackTrace) returns the call stack. The event is valid until the callback returns.
- To step, compare the call depths: *step into* stops at the next statement, *step over* at the next statement whose
  depth is less than or equal to the current one, *step out* at the next statement whose depth is smaller.

## Pause a script

The events are raised on the thread of the engine, and the script waits until the callback returns: a debugger pauses
the script by waiting in the callback, for instance until the user resumes it from another thread.

- The callback can use the engine: read the properties of objects, call functions, run code in the realm of the event.
  The code it runs raises no event.
- The time and the allocations of the callback, and the code it runs, do not count in the limits of the script. A
  pause never exceeds the [Timeout](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.Timeout). The [CancellationToken](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.CancellationToken) still stops the script once the callback returns.
- An exception thrown by the callback is thrown at the statement. To stop the script, throw a
  [JsTerminationException](cref:Meziantou.Framework.JavaScriptEngine.JsTerminationException): the script cannot catch it.

## Generators and async functions

The statements of a generator or an async function raise their events when they run. After a `yield` or an `await`,
they come from the code that resumes the function, such as `engine.RunJobs()` for an `await`, with the call depth of
that code.

## Performance

The code is compiled for the debugger: the engine adds an instruction at the start of each statement only when it has
a debugger. The scripts of an engine without debugger do not have this instruction and run at full speed, so set the
debugger before the code is compiled. A [JsPreparedScript](cref:Meziantou.Framework.JavaScriptEngine.JsPreparedScript) is compiled without it: an engine that has a debugger
compiles it again when it runs it.
