using System.Diagnostics;
using Meziantou.Framework.JavaScriptEngine.Interpreter;

namespace Meziantou.Framework.JavaScriptEngine;

// The events of the debugger of the engine (JsEngineOptions.Debugger).
public sealed partial class JsEngine
{
    private bool _isInDebuggerCallback;

    /// <summary>Calls the debugger for the instruction at the position of <paramref name="frame"/> (a statement, or a debugger statement).</summary>
    internal void RaiseDebugEvent(Frame frame, bool isDebuggerStatement)
    {
        // The code run by the callbacks raises no event
        if (Options.Debugger is not { } debugger || _isInDebuggerCallback)
            return;

        _isInDebuggerCallback = true;

        // The callback (e.g. a debugger paused by the user, or the code it evaluates) does not count in the limits of the
        // script: it has its own limits, like a new entry into JavaScript
        var options = Options;
        var executionStart = _executionStart;
        var executionStartCounter = _executionStartCounter;
        var executionStartAllocatedBytes = _executionStartAllocatedBytes;
        var interruptCounter = _interruptCounter;
        StartExecution();
        var callbackStart = _executionStart;
        var callbackAllocatedBytes = _executionStartAllocatedBytes;
        try
        {
            var debugEvent = new JsDebugEvent(this, frame.Realm, frame.Code, frame.Pc, _frames.Count, frame.Function?.InitialName);
            if (isDebuggerStatement)
            {
                debugger.OnDebuggerStatement(debugEvent);
            }
            else
            {
                debugger.OnStatement(debugEvent);
            }
        }
        finally
        {
            _isInDebuggerCallback = false;
            _executionStart = executionStart + (Stopwatch.GetTimestamp() - callbackStart);
            _executionStartCounter = executionStartCounter + (_interruptCounter - interruptCounter);
            _executionStartAllocatedBytes = options.MemoryLimit is null ? executionStartAllocatedBytes : executionStartAllocatedBytes + (GC.GetAllocatedBytesForCurrentThread() - callbackAllocatedBytes);
            _nextInterruptCheck = GetNextInterruptCheck(options);
        }
    }
}
