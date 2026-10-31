using System.Diagnostics;
using System.Runtime.CompilerServices;
using Meziantou.Framework.JavaScriptEngine.Interpreter;

namespace Meziantou.Framework.JavaScriptEngine;

// The execution context stack and the limits of the running scripts.
public sealed partial class JsEngine
{
    // The limits are checked every InterruptCheckInterval interrupt points (the backward jumps and the calls)
    private const long InterruptCheckInterval = 1024;

    // The limits are checked every WorkCheckInterval units of work of the built-in functions (CountWork)
    private const long WorkCheckInterval = 1 << 16;

    // The allocations checked before they are made (EnsureCurrentCanAllocate): the size of the large object heap
    internal const long LargeAllocationBytes = 85_000;

    private readonly List<Frame> _frames = [];

    // The script execution contexts pushed by the host (EnterActiveScriptOrModule), with the number of frames when they were pushed
    private readonly List<(int FrameCount, JsScriptOrModule? Script)> _scriptContexts = [];
    private long _interruptCounter;
    private long _nextInterruptCheck = InterruptCheckInterval;
    private long _executionStart;
    private long _executionStartCounter;
    private long _executionStartAllocatedBytes;
    private long _workCounter;
    private long _nextWorkCheck = WorkCheckInterval;

    /// <summary>Gets whether a script is running (the execution context stack is not empty).</summary>
    public bool IsScriptRunning => _frames.Count > 0;

    internal void PushFrame(Frame frame)
    {
        if (_frames.Count == 0)
        {
            StartExecution();
        }

        _frames.Add(frame);
    }

    internal void PopFrame() => _frames.RemoveAt(_frames.Count - 1);

    /// <summary>Gets the frame of the running code, if any.</summary>
    internal Frame? CurrentFrame => _frames.Count > 0 ? _frames[^1] : null;

    /// <summary>Gets the frames of the running code, the innermost last.</summary>
    internal IReadOnlyList<Frame> Frames => _frames;

    /// <summary>
    /// Pushes an execution context whose ScriptOrModule is <paramref name="scriptOrModule"/> until the returned scope is
    /// disposed: it is the active script or module (<see cref="GetActiveScriptOrModule"/>) of the code it runs, until code
    /// that has its own script or module runs (e.g. HostCallJobCallback restores the active script captured by
    /// HostMakeJobCallback, so that the code compiled by an <c>eval</c> called by a promise job resolves its imports against it).
    /// </summary>
    /// <param name="scriptOrModule">The script or module, or null for an execution context without script or module.</param>
    public JsScriptContextScope EnterActiveScriptOrModule(JsScriptOrModule? scriptOrModule)
    {
        _scriptContexts.Add((_frames.Count, scriptOrModule));
        return new JsScriptContextScope(this, _scriptContexts.Count);
    }

    internal void ExitActiveScriptOrModule(int count)
    {
        if (_scriptContexts.Count != count)
            throw new InvalidOperationException("The script contexts must be exited in the reverse order they were entered");

        _scriptContexts.RemoveAt(count - 1);
    }

    /// <summary>GetActiveScriptOrModule(): the script or module of the innermost frame or script context of the host that has one.</summary>
    internal Interpreter.IScriptOrModule? FindActiveScriptOrModule()
    {
        var contextIndex = _scriptContexts.Count - 1;
        for (var i = _frames.Count - 1; i >= 0; i--)
        {
            // The contexts pushed after the frame was pushed are above it
            for (; contextIndex >= 0 && _scriptContexts[contextIndex].FrameCount > i; contextIndex--)
            {
                if (_scriptContexts[contextIndex].Script is { } contextScript)
                    return contextScript;
            }

            if (_frames[i].Script is { } script)
                return script;
        }

        for (; contextIndex >= 0; contextIndex--)
        {
            if (_scriptContexts[contextIndex].Script is { } contextScript)
                return contextScript;
        }

        return null;
    }

    /// <summary>Starts an entry into JavaScript: the limits of <see cref="JsEngineOptions"/> apply to each entry.</summary>
    private void StartExecution()
    {
        var options = Options;
        _executionStart = Stopwatch.GetTimestamp();
        _executionStartCounter = _interruptCounter;
        _executionStartAllocatedBytes = options.MemoryLimit is null ? 0 : GC.GetAllocatedBytesForCurrentThread();
        _nextInterruptCheck = GetNextInterruptCheck(options);
    }

    private long GetNextInterruptCheck(JsEngineOptions options)
    {
        var next = _interruptCounter + InterruptCheckInterval;
        if (options.MaxStatements is { } maxStatements)
        {
            // The statement limit is exact: the limits are checked when it is reached
            var limit = _executionStartCounter + Math.Max(maxStatements, 0) + 1;
            if (limit < next)
            {
                next = Math.Max(limit, _interruptCounter + 1);
            }
        }

        return next;
    }

    /// <summary>Counts an interrupt point (a backward jump or a call) and checks the limits of the running script periodically.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void CountInterruptPoint()
    {
        if (++_interruptCounter < _nextInterruptCheck)
            return;

        CheckLimits();
    }

    /// <summary>
    /// Throws a <see cref="JsTerminationException"/> when the running script must stop: the execution is canceled
    /// (<see cref="JsEngineOptions.CancellationToken"/>) or a limit of <see cref="JsEngineOptions"/> is exceeded. The host
    /// calls it periodically while it runs code on behalf of the script (e.g. WebAssembly code); the engine checks the
    /// limits itself while JavaScript code runs.
    /// </summary>
    /// <exception cref="JsTerminationException">The script must stop.</exception>
    public void CheckInterrupt() => CheckLimits();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CheckLimits()
    {
        var options = Options;
        _nextInterruptCheck = GetNextInterruptCheck(options);
        if (options.CancellationToken.IsCancellationRequested)
            throw new JsTerminationException(JsTerminationReason.Canceled, "The script was canceled");

        if (!IsScriptRunning)
            return;

        if (options.Timeout is { } timeout && Stopwatch.GetElapsedTime(_executionStart) > timeout)
            throw new JsTerminationException(JsTerminationReason.Timeout, "The script exceeded its time limit");

        if (options.MemoryLimit is { } memoryLimit && GC.GetAllocatedBytesForCurrentThread() - _executionStartAllocatedBytes > memoryLimit)
            throw new JsTerminationException(JsTerminationReason.MemoryLimit, "The script exceeded its memory limit");

        if (options.MaxStatements is { } maxStatements && _interruptCounter - _executionStartCounter > maxStatements)
            throw new JsTerminationException(JsTerminationReason.StatementLimit, "The script exceeded its statement limit");
    }

    /// <summary>
    /// Throws a <see cref="JsTerminationException"/> when allocating <paramref name="byteCount"/> more bytes would exceed
    /// <see cref="JsEngineOptions.MemoryLimit"/>. The limit is otherwise checked after the allocations, so a single large
    /// allocation (a string whose length the script chooses) must be checked before it is made.
    /// </summary>
    internal void EnsureCanAllocate(long byteCount)
    {
        if (Options.MemoryLimit is { } memoryLimit && IsScriptRunning && GC.GetAllocatedBytesForCurrentThread() - _executionStartAllocatedBytes + byteCount > memoryLimit)
            throw new JsTerminationException(JsTerminationReason.MemoryLimit, "The script exceeded its memory limit");
    }

    /// <summary>
    /// <see cref="EnsureCanAllocate"/> for the engine of the current thread, when the allocation is large (smaller ones are
    /// covered by the periodic checks).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void EnsureCurrentCanAllocate(long byteCount)
    {
        if (byteCount >= LargeAllocationBytes)
        {
            s_current?.EnsureCanAllocate(byteCount);
        }
    }

    /// <summary>
    /// Counts the work of a built-in function (an element visited, a character written) and checks the limits
    /// periodically. A built-in function has no interrupt point: without it, one call could build a result of
    /// gigabytes, or run for minutes.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void CountWork(long units)
    {
        _workCounter += units;
        if (_workCounter >= _nextWorkCheck)
        {
            CheckWorkLimits();
        }
    }

    /// <summary><see cref="CountWork"/> for the engine of the current thread.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void CountCurrentWork(long units) => s_current?.CountWork(units);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CheckWorkLimits()
    {
        _nextWorkCheck = _workCounter + WorkCheckInterval;
        CheckLimits();
    }

    internal string? HostGetCodeForEval(JsValue argument) => HostHooks.GetCodeForEval(this, argument);

    internal void HostEnsureCanCompileStrings(JsRealm realm, IReadOnlyList<string> parameterStrings, string bodyString, string codeString, JsCompilationType compilationType, IReadOnlyList<JsValue> parameterArgs, JsValue bodyArg)
        => HostHooks.EnsureCanCompileStrings(this, realm, parameterStrings, bodyString, codeString, compilationType, parameterArgs, bodyArg);
}
