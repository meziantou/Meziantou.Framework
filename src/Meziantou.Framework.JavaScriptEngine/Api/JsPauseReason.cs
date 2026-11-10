namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>Why a <see cref="JsBreakpointDebugger"/> paused a script.</summary>
public enum JsPauseReason
{
    /// <summary>The statement has a breakpoint (<see cref="JsDebugPause.Breakpoint"/>).</summary>
    Breakpoint,

    /// <summary>A <c>debugger</c> statement runs.</summary>
    DebuggerStatement,

    /// <summary>The script stops after a step (<see cref="JsStepAction"/>).</summary>
    Step,

    /// <summary>The host called <see cref="JsBreakpointDebugger.RequestPause"/>.</summary>
    PauseRequested,
}
