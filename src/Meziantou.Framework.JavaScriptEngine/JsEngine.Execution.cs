using System.Diagnostics;
using Meziantou.Framework.JavaScriptEngine.Interpreter;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The execution context stack and the limits of the running scripts.</summary>
public sealed partial class JsEngine
{
    private readonly List<Frame> _frames = [];
    private long _interruptCounter;
    private long _executionStart;

    /// <summary>Gets whether a script is running (the execution context stack is not empty).</summary>
    public bool IsScriptRunning => _frames.Count > 0;

    internal void PushFrame(Frame frame)
    {
        if (_frames.Count == 0)
        {
            _executionStart = Stopwatch.GetTimestamp();
        }

        _frames.Add(frame);
    }

    internal void PopFrame() => _frames.RemoveAt(_frames.Count - 1);

    /// <summary>Gets the frame of the running code, if any.</summary>
    internal Frame? CurrentFrame => _frames.Count > 0 ? _frames[^1] : null;

    /// <summary>Gets the frames of the running code, the innermost last.</summary>
    internal IReadOnlyList<Frame> Frames => _frames;

    /// <summary>Checks the limits of the running script (cancellation, timeout): called at the backward jumps and the calls.</summary>
    internal void CheckInterrupt()
    {
        if ((++_interruptCounter & 0x3FF) != 0)
            return;

        var options = Options;
        if (options.CancellationToken.IsCancellationRequested)
            throw new JsTerminationException(JsTerminationReason.Canceled, "The script was canceled");

        if (options.Timeout is { } timeout && Stopwatch.GetElapsedTime(_executionStart) > timeout)
            throw new JsTerminationException(JsTerminationReason.Timeout, "The script exceeded its time limit");
    }

    internal string? HostGetCodeForEval(JsValue argument) => HostHooks.GetCodeForEval(this, argument);

    internal void HostEnsureCanCompileStrings(JsRealm realm, IReadOnlyList<string> parameterStrings, string bodyString, string codeString, JsCompilationType compilationType, IReadOnlyList<JsValue> parameterArgs, JsValue bodyArg)
        => HostHooks.EnsureCanCompileStrings(this, realm, parameterStrings, bodyString, codeString, compilationType, parameterArgs, bodyArg);
}
