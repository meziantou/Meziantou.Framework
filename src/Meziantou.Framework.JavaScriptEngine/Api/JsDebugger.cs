using Meziantou.Framework.JavaScriptEngine.Compiler;
using Meziantou.Framework.JavaScriptEngine.Interpreter;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// Receives the execution events of the scripts of an engine, to build a debugger, a code coverage tool or an execution
/// inspector. Set it in <see cref="JsEngineOptions.Debugger"/> before the code is compiled.
/// </summary>
/// <remarks>
/// <para>
/// The code compiled by an engine that has a debugger (the scripts, the modules, <c>eval</c> and <c>new Function</c>)
/// raises an event before each statement. The code of an engine without debugger has no instruction for it, so the
/// debugging costs nothing when it is not used. A <see cref="JsPreparedScript"/> is compiled again when an engine that
/// has a debugger runs it.
/// </para>
/// <para>
/// The events are raised on the thread of the engine, while the script waits: a debugger pauses the script by not
/// returning (e.g. it waits for the user to resume). The callbacks can use the engine, for instance to read the
/// properties of an object, call a function or evaluate code: the code they run raises no event. The time and the
/// allocations of the callbacks do not count in <see cref="JsEngineOptions.Timeout"/> and <see cref="JsEngineOptions.MemoryLimit"/>;
/// <see cref="JsEngineOptions.CancellationToken"/> still stops the script once the callback returns. An exception thrown
/// by a callback is thrown at the statement: throw a <see cref="JsTerminationException"/> to stop the script.
/// </para>
/// <para>
/// The statements of a generator or an async function raise their events when the function runs: after a <c>yield</c>
/// or an <c>await</c>, the events come from the code that resumes it (e.g. <see cref="JsEngine.RunJobs"/>), with the
/// call depth of that code.
/// </para>
/// </remarks>
public abstract class JsDebugger
{
    /// <summary>Called before a statement runs.</summary>
    /// <param name="debugEvent">The statement. It is valid until the method returns.</param>
    protected internal virtual void OnStatement(JsDebugEvent debugEvent)
    {
    }

    /// <summary>Called when a <c>debugger</c> statement runs.</summary>
    /// <param name="debugEvent">The statement. It is valid until the method returns.</param>
    protected internal virtual void OnDebuggerStatement(JsDebugEvent debugEvent)
    {
    }
}

/// <summary>A statement that is about to run, given to the methods of <see cref="JsDebugger"/>.</summary>
public readonly struct JsDebugEvent
{
    private readonly Frame _frame;
    private readonly FunctionTemplate _code;
    private readonly int _pc;
    private readonly long _version;

    internal JsDebugEvent(JsEngine engine, Frame frame, long version, int callDepth)
    {
        Engine = engine;
        Realm = frame.Realm;
        _frame = frame;
        _code = frame.Code;
        _pc = frame.Pc;
        _version = version;
        CallDepth = callDepth;
        FunctionName = frame.Function?.InitialName is { Length: > 0 } name ? name : null;
    }

    /// <summary>Gets the engine that runs the statement.</summary>
    public JsEngine Engine { get; }

    /// <summary>Gets the realm of the code of the statement.</summary>
    public JsRealm Realm { get; }

    /// <summary>
    /// Gets the number of JavaScript frames on the stack, the frame of the statement included (1 for the top-level code
    /// of a script). A debugger compares it to step over (the next statement with a depth less than or equal) or out of
    /// a function (a smaller depth).
    /// </summary>
    public int CallDepth { get; }

    /// <summary>Gets the name of the function that contains the statement, or null for the top-level code and the anonymous functions.</summary>
    public string? FunctionName { get; }

    /// <summary>Gets the position of the start of the statement.</summary>
    public JsSourceLocation Location => JsDebugFrame.GetLocation(_code, _pc);

    /// <summary>The URL of the code of the statement, without computing its line and column.</summary>
    internal string? Url => _code.Source.Url;

    /// <summary>Captures the stack trace of the running code, in the format of the <c>stack</c> property of the errors (without the first line).</summary>
    public string CaptureStackTrace() => Engine.CaptureStackTrace();

    /// <summary>Gets the JavaScript frames of the stack, the frame of the statement first, then its callers.</summary>
    /// <returns>The frames. They can be used until the callback that received the event returns.</returns>
    /// <exception cref="InvalidOperationException">The callback that received the event returned.</exception>
    public IReadOnlyList<JsDebugFrame> GetCallStack()
    {
        EnsureActive();
        var frames = Engine.Frames;
        var index = frames.Count - 1;

        // The callback may run code: the frames it pushed are above the frame of the event
        while (index >= 0 && !ReferenceEquals(frames[index], _frame))
        {
            index--;
        }

        var result = new JsDebugFrame[index + 1];
        for (var i = index; i >= 0; i--)
        {
            result[index - i] = new JsDebugFrame(Engine, frames[i], _version, isTop: i == index);
        }

        return result;
    }

    /// <summary>Gets the scopes of the statement, the innermost first: its variables, and the ones of the enclosing blocks, functions and global scope.</summary>
    /// <exception cref="InvalidOperationException">The callback that received the event returned.</exception>
    public IReadOnlyList<JsDebugScope> GetScopes() => GetFrame().GetScopes();

    /// <summary>Evaluates code at the position of the statement, like a direct <c>eval</c>: it reads and changes the variables of the statement.</summary>
    /// <param name="code">The code, e.g. an expression.</param>
    /// <returns>The completion value of the code.</returns>
    /// <exception cref="InvalidOperationException">The callback that received the event returned.</exception>
    /// <exception cref="JsException">The code throws, or it has a syntax error.</exception>
    public JsValue Evaluate(string code) => GetFrame().Evaluate(code);

    private JsDebugFrame GetFrame()
    {
        EnsureActive();
        return new JsDebugFrame(Engine, _frame, _version, isTop: true);
    }

    private void EnsureActive()
    {
        if (Engine is null || !Engine.IsDebugEventActive(_version))
            throw new InvalidOperationException("The debug event can only be used while the callback that received it runs");
    }
}
