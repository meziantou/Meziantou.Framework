namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>A script paused by a <see cref="JsBreakpointDebugger"/>.</summary>
public readonly struct JsDebugPause
{
    internal JsDebugPause(JsDebugEvent debugEvent, JsPauseReason reason, JsBreakpoint? breakpoint)
    {
        Event = debugEvent;
        Reason = reason;
        Breakpoint = breakpoint;
    }

    /// <summary>Gets the statement where the script is paused: its location, its scopes, its call stack, and the evaluation of code at its position.</summary>
    public JsDebugEvent Event { get; }

    /// <summary>Gets why the script is paused.</summary>
    public JsPauseReason Reason { get; }

    /// <summary>Gets the breakpoint of the statement, when <see cref="Reason"/> is <see cref="JsPauseReason.Breakpoint"/>.</summary>
    public JsBreakpoint? Breakpoint { get; }

    /// <summary>Gets the position of the statement.</summary>
    public JsSourceLocation Location => Event.Location;
}
