namespace Meziantou.Framework.JavaScriptEngine.Samples;

#region JavaScriptEngine_BreakpointDebugger
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
#endregion
