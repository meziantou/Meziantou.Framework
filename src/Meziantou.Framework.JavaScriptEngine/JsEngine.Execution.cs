using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
    private int _hostEntryDepth;

    // The termination of the running entry: it stays until the outermost entry returns, so that a host function that
    // catches the JsTerminationException of a nested call cannot let the script continue
    private (JsTerminationReason Reason, string Message)? _pendingTermination;

    // The scopes of EnterExecutionScope, the innermost last
    private readonly List<JsExecutionScope> _executionScopes = [];

    /// <summary>Gets whether a script is running (the execution context stack is not empty).</summary>
    public bool IsScriptRunning => _frames.Count > 0;

    /// <summary>
    /// Gets whether an entry into JavaScript is in progress: a script runs, or the host called the engine (<see cref="Call"/>,
    /// <see cref="JsRealm.Execute(string, string?)"/>, an execution scope...). The limits apply while it is true.
    /// </summary>
    internal bool IsExecuting => _frames.Count > 0 || _hostEntryDepth > 0;

    internal void PushFrame(Frame frame)
    {
        if (!IsExecuting)
        {
            StartExecution();
        }

        _frames.Add(frame);
    }

    internal void PopFrame()
    {
        _frames.RemoveAt(_frames.Count - 1);
        if (_frames.Count == 0 && _hostEntryDepth == 0)
        {
            EndExecution();
        }
    }

    /// <summary>
    /// Enters the engine for a host call that may run code (<see cref="Call"/>, <see cref="JsRealm.Execute(string, string?)"/>...):
    /// the call is an entry into JavaScript, whose limits apply to the built-ins it calls and to the parsing of the source,
    /// not only to the JavaScript frames. A call made while JavaScript runs (from a host function) is part of the running entry.
    /// </summary>
    /// <exception cref="JsTerminationException">The running entry was terminated, or the execution is canceled.</exception>
    internal HostEntryScope EnterHostEntry()
    {
        // Another thread must not change the state of the engine: the ownership is checked first
        var ownsEngine = AcquireOwnership();
        try
        {
            if (IsExecuting)
            {
                ThrowIfTerminationPending();
            }
            else
            {
                StartExecution();
            }
        }
        catch
        {
            if (ownsEngine)
            {
                ReleaseOwnership();
            }

            throw;
        }

        var previous = s_current;
        s_current = this;
        _hostEntryDepth++;
        return new HostEntryScope(this, previous, ownsEngine);
    }

    internal void ExitHostEntry(JsEngine? previous, bool ownsEngine)
    {
        _hostEntryDepth--;
        s_current = previous;
        if (!IsExecuting)
        {
            EndExecution();
        }

        if (ownsEngine)
        {
            ReleaseOwnership();
        }
    }

    /// <summary>The scope returned by <see cref="EnterHostEntry"/>.</summary>
    internal readonly ref struct HostEntryScope(JsEngine engine, JsEngine? previous, bool ownsEngine)
    {
        /// <summary>Gets the engine of the thread before the entry.</summary>
        public JsEngine? Previous => previous;

        /// <summary>Gets a value indicating whether the entry took the ownership of the engine.</summary>
        public bool OwnsEngine => ownsEngine;

        public void Dispose() => engine.ExitHostEntry(previous, ownsEngine);
    }

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

    /// <summary>
    /// Starts an entry into JavaScript: the limits of <see cref="JsEngineOptions"/> apply to each entry. An entry canceled
    /// before it starts does not run.
    /// </summary>
    private void StartExecution()
    {
        var options = Options;
        if (options.CancellationToken.IsCancellationRequested)
            throw new JsTerminationException(JsTerminationReason.Canceled, "The script was canceled");

        _pendingTermination = null;
        _executionStart = Stopwatch.GetTimestamp();
        _executionStartCounter = _interruptCounter;
        _executionStartAllocatedBytes = options.MemoryLimit is null ? 0 : GC.GetAllocatedBytesForCurrentThread();
        _nextInterruptCheck = GetNextInterruptCheck(options);
    }

    /// <summary>Ends the outermost entry: the next entry is not terminated by the termination of this one.</summary>
    private void EndExecution() => _pendingTermination = null;

    private long GetNextInterruptCheck(JsEngineOptions options)
    {
        var next = _interruptCounter + InterruptCheckInterval;
        if (options.MaxStatements is { } maxStatements)
        {
            next = Math.Min(next, GetStatementLimitCheck(_executionStartCounter, maxStatements));
        }

        foreach (var scope in _executionScopes)
        {
            if (scope.Limits.MaxStatements is { } scopeMaxStatements)
            {
                next = Math.Min(next, GetStatementLimitCheck(scope.StartCounter, scopeMaxStatements));
            }
        }

        return next;
    }

    // The statement limit is exact: the limits are checked when it is reached
    private long GetStatementLimitCheck(long startCounter, long maxStatements) => Math.Max(startCounter + Math.Max(maxStatements, 0) + 1, _interruptCounter + 1);

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
    /// (<see cref="JsEngineOptions.CancellationToken"/>), a limit of <see cref="JsEngineOptions"/> or of an execution scope
    /// (<see cref="EnterExecutionScope"/>) is exceeded, or the running entry was already terminated. The host calls it
    /// periodically while it runs code on behalf of the script (e.g. WebAssembly code); the engine checks the limits itself
    /// while JavaScript code and the built-in functions run.
    /// </summary>
    /// <exception cref="JsTerminationException">The script must stop.</exception>
    public void CheckInterrupt() => CheckLimits();

    /// <summary>The periodic check of the parser and the compiler: the limits apply when the source is compiled by an entry.</summary>
    internal void CheckCompilationInterrupt()
    {
        if (IsExecuting)
        {
            CheckLimits();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CheckLimits()
    {
        var options = Options;
        _nextInterruptCheck = GetNextInterruptCheck(options);
        ThrowIfTerminationPending();
        if (options.CancellationToken.IsCancellationRequested)
            ThrowTermination(JsTerminationReason.Canceled, "The script was canceled");

        if (!IsExecuting)
            return;

        if (options.Timeout is { } timeout && Stopwatch.GetElapsedTime(_executionStart) > timeout)
            ThrowTermination(JsTerminationReason.Timeout, "The script exceeded its time limit");

        if (options.MemoryLimit is { } memoryLimit && GC.GetAllocatedBytesForCurrentThread() - _executionStartAllocatedBytes > memoryLimit)
            ThrowTermination(JsTerminationReason.MemoryLimit, "The script exceeded its memory limit");

        if (options.MaxStatements is { } maxStatements && _interruptCounter - _executionStartCounter > maxStatements)
            ThrowTermination(JsTerminationReason.StatementLimit, "The script exceeded its statement limit");

        foreach (var scope in _executionScopes)
        {
            CheckScopeLimits(scope, additionalBytes: 0);
        }
    }

    private void CheckScopeLimits(JsExecutionScope scope, long additionalBytes)
    {
        var limits = scope.Limits;
        if (limits.CancellationToken.IsCancellationRequested)
            ThrowTermination(JsTerminationReason.Canceled, "The script was canceled");

        if (limits.Timeout is { } timeout && Stopwatch.GetElapsedTime(scope.StartTimestamp) > timeout)
            ThrowTermination(JsTerminationReason.Timeout, "The script exceeded its time limit");

        if (limits.MemoryLimit is { } memoryLimit && GC.GetAllocatedBytesForCurrentThread() - scope.StartAllocatedBytes + additionalBytes > memoryLimit)
            ThrowTermination(JsTerminationReason.MemoryLimit, "The script exceeded its memory limit");

        if (limits.MaxStatements is { } maxStatements && _interruptCounter - scope.StartCounter > maxStatements)
            ThrowTermination(JsTerminationReason.StatementLimit, "The script exceeded its statement limit");
    }

    /// <summary>
    /// Throws the termination of the running entry: it is recorded until the outermost entry returns, so that the code
    /// that continues after a host function caught the exception is terminated again (at its next interrupt point, its
    /// next call of a host function or its next entry).
    /// </summary>
    [DoesNotReturn]
    internal void ThrowTermination(JsTerminationReason reason, string message)
    {
        RecordTermination(reason, message);
        throw new JsTerminationException(reason, message);
    }

    private void RecordTermination(JsTerminationReason reason, string message)
    {
        if (IsExecuting && _pendingTermination is null)
        {
            _pendingTermination = (reason, message);

            // The next interrupt point checks the limits
            _nextInterruptCheck = _interruptCounter + 1;
        }
    }

    /// <summary>Records a termination that leaves a host call (e.g. thrown by a host function): an exception filter that never catches it.</summary>
    internal bool RecordTermination(JsTerminationException exception)
    {
        RecordTermination(exception.Reason, exception.Message);
        return false;
    }

    /// <summary>Throws the termination of the running entry again when a host function caught it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ThrowIfTerminationPending()
    {
        if (_pendingTermination is not null)
        {
            ThrowPendingTermination();
        }
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ThrowPendingTermination()
    {
        var (reason, message) = _pendingTermination!.Value;
        throw new JsTerminationException(reason, message);
    }

    /// <summary>
    /// Gets the time left before the running entry exceeds a timeout (of <see cref="JsEngineOptions.Timeout"/> or of an
    /// execution scope), or null when no timeout applies. The operations that block the thread (<c>Atomics.wait</c>) wait
    /// at most this long.
    /// </summary>
    internal TimeSpan? GetRemainingTime()
    {
        if (!IsExecuting)
            return null;

        TimeSpan? remaining = null;
        if (Options.Timeout is { } timeout)
        {
            remaining = timeout - Stopwatch.GetElapsedTime(_executionStart);
        }

        foreach (var scope in _executionScopes)
        {
            if (scope.Limits.Timeout is { } scopeTimeout)
            {
                var scopeRemaining = scopeTimeout - Stopwatch.GetElapsedTime(scope.StartTimestamp);
                if (remaining is null || scopeRemaining < remaining)
                {
                    remaining = scopeRemaining;
                }
            }
        }

        return remaining;
    }

    /// <summary>Gets whether an execution scope of the running entry has a cancellation token that can be canceled.</summary>
    internal bool HasCancelableExecutionScope
    {
        get
        {
            foreach (var scope in _executionScopes)
            {
                if (scope.Limits.CancellationToken.CanBeCanceled)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Starts an execution scope: the calls made until the returned scope is disposed are one entry into JavaScript, and
    /// <paramref name="limits"/> apply to them as a whole, in addition to the limits of <see cref="JsEngineOptions"/> (which
    /// apply to the scope as a whole as well: the calls in the scope do not restart them). It bounds a sequence of calls
    /// (e.g. a script and the jobs it enqueues: <see cref="RunJobs"/> runs each job in its own entry otherwise), or one call
    /// of an engine shared by several requests, without changing the options of the engine. The scope also makes the engine
    /// the engine of the current thread (<see cref="Enter"/>). A scope started while JavaScript runs (from a host function)
    /// adds its limits to the running entry until it is disposed.
    /// </summary>
    /// <param name="limits">The limits of the scope, or null for the limits of the options only.</param>
    /// <exception cref="JsTerminationException">The execution is canceled, or the running entry was terminated.</exception>
    public JsExecutionScope EnterExecutionScope(JsExecutionLimits? limits = null)
    {
        limits ??= JsExecutionLimits.None;
        if (limits.CancellationToken.IsCancellationRequested)
            throw new JsTerminationException(JsTerminationReason.Canceled, "The script was canceled");

        var entry = EnterHostEntry();
        var scope = new JsExecutionScope(this, entry.Previous, entry.OwnsEngine, limits, Stopwatch.GetTimestamp(), _interruptCounter, limits.MemoryLimit is null ? 0 : GC.GetAllocatedBytesForCurrentThread());
        _executionScopes.Add(scope);
        _nextInterruptCheck = GetNextInterruptCheck(Options);
        return scope;
    }

    internal void ExitExecutionScope(JsExecutionScope scope)
    {
        if (_executionScopes.Count == 0 || !ReferenceEquals(_executionScopes[^1], scope))
            throw new InvalidOperationException("The execution scopes must be exited in the reverse order they were entered");

        _executionScopes.RemoveAt(_executionScopes.Count - 1);
        ExitHostEntry(scope.PreviousEngine, scope.OwnsEngine);
    }

    /// <summary>
    /// Throws a <see cref="JsTerminationException"/> when allocating <paramref name="byteCount"/> more bytes would exceed
    /// <see cref="JsEngineOptions.MemoryLimit"/>. The limit is otherwise checked after the allocations, so a single large
    /// allocation (a string whose length the script chooses) must be checked before it is made.
    /// </summary>
    internal void EnsureCanAllocate(long byteCount)
    {
        if (!IsExecuting)
            return;

        if (Options.MemoryLimit is { } memoryLimit && GC.GetAllocatedBytesForCurrentThread() - _executionStartAllocatedBytes + byteCount > memoryLimit)
            ThrowTermination(JsTerminationReason.MemoryLimit, "The script exceeded its memory limit");

        foreach (var scope in _executionScopes)
        {
            if (scope.Limits.MemoryLimit is not null)
            {
                CheckScopeLimits(scope, byteCount);
            }
        }
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

    /// <summary>
    /// Gets whether the periodic checks of <see cref="CountCurrentWork"/> can stop the built-in function that runs on the
    /// current thread: a timeout, a memory limit or a cancellation token applies to it. A long operation without them can
    /// run at once, without the cost of being split into parts.
    /// </summary>
    internal static bool CurrentWorkCanBeInterrupted => s_current is { } engine && engine.WorkCanBeInterrupted();

    private bool WorkCanBeInterrupted()
    {
        var options = Options;
        if (options.CancellationToken.CanBeCanceled)
            return true;

        if (!IsExecuting)
            return false;

        if (options.Timeout is not null || options.MemoryLimit is not null)
            return true;

        foreach (var scope in _executionScopes)
        {
            var limits = scope.Limits;
            if (limits.CancellationToken.CanBeCanceled || limits.Timeout is not null || limits.MemoryLimit is not null)
                return true;
        }

        return false;
    }

    internal string? HostGetCodeForEval(JsValue argument) => HostHooks.GetCodeForEval(this, argument);

    internal void HostEnsureCanCompileStrings(JsRealm realm, IReadOnlyList<string> parameterStrings, string bodyString, string codeString, JsCompilationType compilationType, IReadOnlyList<JsValue> parameterArgs, JsValue bodyArg)
        => HostHooks.EnsureCanCompileStrings(this, realm, parameterStrings, bodyString, codeString, compilationType, parameterArgs, bodyArg);
}
