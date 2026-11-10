namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>A breakpoint of a <see cref="JsBreakpointDebugger"/> (<see cref="JsBreakpointDebugger.AddBreakpoint"/>).</summary>
public sealed class JsBreakpoint
{
    private int _hitCount;

    internal JsBreakpoint(string? url, int line, int? column, string? condition)
    {
        Url = url;
        Line = line;
        Column = column;
        Condition = condition;
    }

    /// <summary>Gets the URL of the code (the name given to the script, the module or the <c>//# sourceURL=</c> comment of eval code).</summary>
    public string? Url { get; }

    /// <summary>Gets the line, 1-based.</summary>
    public int Line { get; }

    /// <summary>Gets the column of the statement, 1-based in UTF-16 code units, or null for each statement that starts on the line.</summary>
    public int? Column { get; }

    /// <summary>
    /// Gets the code evaluated at the statement to decide whether the script pauses (it pauses when the value is truthy), or
    /// null to always pause. A condition that throws does not pause the script.
    /// </summary>
    public string? Condition { get; }

    /// <summary>Gets or sets whether the breakpoint pauses the scripts. The default is true.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Gets the number of times the breakpoint paused a script.</summary>
    public int HitCount => Volatile.Read(ref _hitCount);

    internal void IncrementHitCount() => Interlocked.Increment(ref _hitCount);

    internal bool Matches(JsSourceLocation location)
    {
        return IsEnabled
            && location.Line == Line
            && (Column is null || location.Column == Column)
            && string.Equals(location.Url, Url, StringComparison.Ordinal);
    }
}
