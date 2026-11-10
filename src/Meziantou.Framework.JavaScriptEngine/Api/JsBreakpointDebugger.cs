namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A debugger that pauses the scripts at breakpoints, at the <c>debugger</c> statements and on request, and steps through
/// their statements. Set it in <see cref="JsEngineOptions.Debugger"/> before the code is compiled.
/// </summary>
/// <remarks>
/// <para>
/// When the script pauses, <see cref="OnPaused"/> (or the callback given to the constructor) is called on the thread of
/// the engine. The script waits until it returns: it can read the scopes, the call stack and evaluate code at the
/// statement (<see cref="JsDebugPause.Event"/>), then return how the script resumes. To stop the script, throw a
/// <see cref="JsTerminationException"/>.
/// </para>
/// <para>
/// The breakpoints can be added, removed, enabled and disabled from any thread, including while a script runs.
/// </para>
/// <para>
/// A step is based on the call depth of the statements: a step that leaves the code being run (e.g. the end of a script,
/// or an <c>await</c>) pauses at the next statement that runs, such as the statement of a promise job.
/// </para>
/// </remarks>
public class JsBreakpointDebugger : JsDebugger
{
    private readonly Func<JsDebugPause, JsStepAction>? _onPaused;
    private readonly Lock _lock = new();

    // Copied on write: the statements read the breakpoints without lock
    private volatile JsBreakpoint[] _breakpoints = [];
    private volatile bool _isPauseRequested;
    private JsStepAction _stepAction;
    private int _stepCallDepth;

    /// <summary>Initializes a new instance of the <see cref="JsBreakpointDebugger"/> class.</summary>
    /// <param name="onPaused">Called when a script pauses. It returns how the script resumes.</param>
    public JsBreakpointDebugger(Func<JsDebugPause, JsStepAction> onPaused)
    {
        ArgumentNullException.ThrowIfNull(onPaused);
        _onPaused = onPaused;
    }

    /// <summary>Initializes a new instance of the <see cref="JsBreakpointDebugger"/> class, for a class that overrides <see cref="OnPaused"/>.</summary>
    protected JsBreakpointDebugger()
    {
    }

    /// <summary>Gets the breakpoints, in the order they were added.</summary>
    public IReadOnlyList<JsBreakpoint> Breakpoints => _breakpoints;

    /// <summary>Gets or sets whether the <c>debugger</c> statements pause the scripts. The default is true.</summary>
    public bool PauseOnDebuggerStatements { get; set; } = true;

    /// <summary>Adds a breakpoint.</summary>
    /// <param name="url">The URL of the code (the name given to the script, the module, or the <c>//# sourceURL=</c> comment of eval code).</param>
    /// <param name="line">The line, 1-based.</param>
    /// <param name="column">The column of the statement, 1-based in UTF-16 code units, or null to pause at each statement that starts on the line.</param>
    /// <param name="condition">The code evaluated at the statement: the script pauses when its value is truthy. Null to always pause.</param>
    /// <returns>The breakpoint.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="line"/> or <paramref name="column"/> is less than 1.</exception>
    public JsBreakpoint AddBreakpoint(string? url, int line, int? column = null, string? condition = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(line, 1);
        if (column is { } value)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1, nameof(column));
        }

        var breakpoint = new JsBreakpoint(url, line, column, condition);
        lock (_lock)
        {
            _breakpoints = [.. _breakpoints, breakpoint];
        }

        return breakpoint;
    }

    /// <summary>Removes a breakpoint.</summary>
    /// <param name="breakpoint">The breakpoint.</param>
    /// <returns>True when the breakpoint was removed; false when it is not a breakpoint of the debugger.</returns>
    public bool RemoveBreakpoint(JsBreakpoint breakpoint)
    {
        ArgumentNullException.ThrowIfNull(breakpoint);
        lock (_lock)
        {
            var index = Array.IndexOf(_breakpoints, breakpoint);
            if (index < 0)
                return false;

            _breakpoints = [.. _breakpoints.AsSpan(0, index), .. _breakpoints.AsSpan(index + 1)];
            return true;
        }
    }

    /// <summary>Removes all the breakpoints.</summary>
    public void ClearBreakpoints()
    {
        lock (_lock)
        {
            _breakpoints = [];
        }
    }

    /// <summary>Pauses the script at the next statement that runs. It can be called from any thread.</summary>
    public void RequestPause() => _isPauseRequested = true;

    /// <summary>Called when a script pauses, on the thread of the engine. The script waits until it returns.</summary>
    /// <param name="pause">The statement where the script is paused, and why. It is valid until the method returns.</param>
    /// <returns>How the script resumes.</returns>
    protected virtual JsStepAction OnPaused(JsDebugPause pause) => _onPaused?.Invoke(pause) ?? JsStepAction.Continue;

    /// <inheritdoc/>
    protected internal override void OnStatement(JsDebugEvent debugEvent) => OnEvent(debugEvent, isDebuggerStatement: false);

    /// <inheritdoc/>
    protected internal override void OnDebuggerStatement(JsDebugEvent debugEvent) => OnEvent(debugEvent, isDebuggerStatement: true);

    private void OnEvent(JsDebugEvent debugEvent, bool isDebuggerStatement)
    {
        JsPauseReason? reason = null;
        JsBreakpoint? breakpoint = null;
        if (FindBreakpoint(debugEvent) is { } hit)
        {
            reason = JsPauseReason.Breakpoint;
            breakpoint = hit;
        }
        else if (isDebuggerStatement && PauseOnDebuggerStatements)
        {
            reason = JsPauseReason.DebuggerStatement;
        }
        else if (_isPauseRequested)
        {
            reason = JsPauseReason.PauseRequested;
        }
        else if (IsStepComplete(debugEvent.CallDepth))
        {
            reason = JsPauseReason.Step;
        }

        if (reason is null)
            return;

        _isPauseRequested = false;
        _stepAction = JsStepAction.Continue;
        breakpoint?.IncrementHitCount();
        var action = OnPaused(new JsDebugPause(debugEvent, reason.Value, breakpoint));
        _stepAction = action;
        _stepCallDepth = debugEvent.CallDepth;
    }

    private bool IsStepComplete(int callDepth)
    {
        return _stepAction switch
        {
            JsStepAction.StepInto => true,
            JsStepAction.StepOver => callDepth <= _stepCallDepth,
            JsStepAction.StepOut => callDepth < _stepCallDepth,
            _ => false,
        };
    }

    private JsBreakpoint? FindBreakpoint(JsDebugEvent debugEvent)
    {
        var breakpoints = _breakpoints;
        if (breakpoints.Length == 0)
            return null;

        var location = debugEvent.Location;
        foreach (var breakpoint in breakpoints)
        {
            if (!breakpoint.Matches(location))
                continue;

            if (breakpoint.Condition is not { } condition)
                return breakpoint;

            try
            {
                if (JsEngine.ToBoolean(debugEvent.Evaluate(condition)))
                    return breakpoint;
            }
            catch (JsException)
            {
                // A condition that throws (or has a syntax error) does not pause the script
            }
        }

        return null;
    }
}
