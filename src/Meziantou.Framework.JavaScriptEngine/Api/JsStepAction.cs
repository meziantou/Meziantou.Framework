namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>How a script paused by a <see cref="JsBreakpointDebugger"/> resumes.</summary>
public enum JsStepAction
{
    /// <summary>Runs until the next breakpoint, <c>debugger</c> statement or <see cref="JsBreakpointDebugger.RequestPause"/>.</summary>
    Continue,

    /// <summary>Pauses at the next statement, in the functions it calls too.</summary>
    StepInto,

    /// <summary>Pauses at the next statement of the function, or of its caller once it returns: the functions it calls run without pausing.</summary>
    StepOver,

    /// <summary>Pauses at the next statement of the caller, once the function returns.</summary>
    StepOut,
}
