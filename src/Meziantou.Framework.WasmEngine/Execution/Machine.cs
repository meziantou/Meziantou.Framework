using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.WasmEngine;

/// <summary>
/// The interpreter of a store. The values are on a stack of slots: a frame has its locals (the parameters first) and its
/// operands. The calls between interpreted functions do not use the .NET stack; the calls of host functions and of the
/// functions compiled to .NET code do.
/// </summary>
internal sealed unsafe partial class Machine
{
    private const int InitialStackSize = 1024;

    private readonly ManagedStore _store;
    private readonly WasmRuntime _runtime;
    private readonly int _maxSlots;
    private readonly int _maxFrames;
    private Slot[] _stack = new Slot[InitialStackSize];
    private Frame[] _frames = new Frame[64];
    private int _frameCount;
    // The number of frames of .NET code of the running calls, when the .NET code calls the machine (see JitCall): the .NET
    // code gives its depth as an argument to its callees
    private int _nativeDepth;
    private int _top;

    // The end of the slots used since the outermost call started: they are cleared when it returns, so that the frames
    // of the finished calls do not keep their references alive
    private int _highWater;
    // Set by the timer of the interruptions of the runtime (see RequestInterruptCheck); the compiled code reads it
    internal bool _interruptRequested;
    private readonly int _compilationThreshold;

    // The number of backward branches of the interpreted calls of a function after which a call continues in .NET code
    // (see TryOsr)
    private readonly int _osrThreshold;

    // The arguments and the results of the host functions being called (see CallHost)
    private WasmValue[] _values = new WasmValue[16];
    private int _valueCount;

    [StructLayout(LayoutKind.Auto)]
    private struct Frame
    {
        public CodeFunction Function;
        public int Pc;
        public int Fp;
    }

    public Machine(ManagedStore store)
    {
        _store = store;
        _runtime = store.Runtime;
        var stackSize = Math.Max(_runtime.Options.MaxStackSize, 16 * 1024);
        _maxSlots = Math.Max(stackSize / 8, 4096);
        _maxFrames = Math.Max(stackSize / 32, 512);
        _compilationThreshold = _runtime.Options.CompilationThreshold;
        _osrThreshold = _compilationThreshold < 0 ? int.MaxValue : _runtime.Options.OsrThreshold ?? Math.Max(_compilationThreshold, 1) * OsrIterationsPerCall;

        // Only the stores that can interrupt their code are asked to call their action
        if (store.Options.CheckInterrupt is not null)
        {
            _runtime.Interruptions.Register(this);
        }
    }

    public Slot[] Stack => _stack;

    /// <summary>The store whose code the machine runs.</summary>
    public ManagedStore Store => _store;

    /// <summary>The first slot that is not used by the running calls.</summary>
    public int Top
    {
        get => _top;
        set => _top = value;
    }

    /// <summary>Calls a function with the arguments of the host.</summary>
    /// <param name="function">The function.</param>
    /// <param name="arguments">The arguments.</param>
    /// <param name="results">Receives the results.</param>
    /// <param name="caller">The instance given to a host function as its caller (the instance of a start function), or null when the host calls the function.</param>
    public void Invoke(ManagedFunction function, ReadOnlySpan<WasmValue> arguments, Span<WasmValue> results, ManagedInstance? caller = null)
    {
        var type = function.DefinedType;
        var fp = _top;
        var size = Math.Max(type.Parameters.Length, type.Results.Length);
        EnsureCapacity(fp + size);
        for (var i = 0; i < arguments.Length; i++)
        {
            _stack[fp + i] = _store.ToSlot(arguments[i], type.Parameters[i]);
        }

        var savedTop = _top;
        _top = fp + type.Parameters.Length;
        try
        {
            Call(function, fp, caller);
            for (var i = 0; i < results.Length; i++)
            {
                results[i] = _stack[fp + i].ToValue(type.Results[i]);
            }
        }
        finally
        {
            _top = savedTop;
            var end = Math.Max(fp + size, Math.Min(_highWater, _stack.Length));
            Array.Clear(_stack, fp, end - fp);
            _highWater = fp;
        }
    }

    /// <summary>
    /// Calls a function whose arguments are at <paramref name="fp"/>: its results are written at <paramref name="fp"/>.
    /// <paramref name="caller"/> is the instance of the calling code, given to a host function (see <see cref="WasmCaller"/>).
    /// </summary>
    public void Call(ManagedFunction function, int fp, ManagedInstance? caller)
    {
        if (function is CodeFunction code)
        {
            var compiledCode = code.Code;
            if (compiledCode.Compiled is null && ShouldCompile(compiledCode))
            {
                TryCompile(code);
            }

            if (compiledCode.Compiled is { } compiled)
            {
                CallCompiled(code, compiled, fp);
            }
            else
            {
                Run(code, fp);
            }
        }
        else
        {
            CallHost((HostFunction)function, fp, caller);
        }
    }

    /// <summary>Counts the calls of an interpreted function: whether it must be compiled to .NET code.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ShouldCompile(CompiledCode code) => _compilationThreshold >= 0 && !code.CompilationFailed && ++code.CallCount > _compilationThreshold;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void TryCompile(CodeFunction function)
    {
        var code = function.Code;
        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            code.CompilationFailed = true;
            return;
        }

        // One thread compiles the function, the others interpret it meanwhile (the read avoids an interlocked write per call)
        if (Volatile.Read(ref code.CompilationStarted) != 0 || Interlocked.Exchange(ref code.CompilationStarted, 1) != 0)
            return;

        JitFunction? compiled;
        try
        {
            compiled = IlCompiler.Compile(function);
        }
#pragma warning disable CA1031 // A failure of the compiler must not fail the call: the function is interpreted
        catch (Exception ex) when (!JitOps.ThrowOnInvalidProgram && ex is not OutOfMemoryException)
#pragma warning restore CA1031
        {
            // A function the compiler does not handle is interpreted
            compiled = null;
        }

        if (compiled is null)
        {
            code.CompilationFailed = true;
        }
        else
        {
            Volatile.Write(ref code.Compiled, compiled);
        }
    }

    private void CallCompiled(CodeFunction function, JitFunction compiled, int fp)
    {
        // The frames of the machine and of the host before this one can be large, and the machine can run on another
        // thread than before: the stack of .NET is checked when this frame is below the bound of the small frames, or on
        // another thread
        CheckStackFromMachine();
        var depth = _nativeDepth + 1;
        if (depth + _frameCount > _maxFrames)
            throw Traps.StackOverflow();

        // The interpreted frames do not change while the .NET code runs (the calls of the machine end their frames)
        var savedTop = _top;
        var savedLimit = _jitDepthLimit;
        _jitDepthLimit = _maxFrames - _frameCount;
        try
        {
            compiled(this, function.Instance, fp, depth);
            if (function.Code.PendingTyped is { } typed)
            {
                // The code ran: the JIT of .NET compiled it, so the compiled callers can call it directly
                Volatile.Write(ref function.Code.Typed, typed);
                function.Code.PendingTyped = null;
            }

            // The compiled callers that find the function at run time read the typed entry of the function
            if (function.Typed is null && function.Code.Typed is { } published)
            {
                Volatile.Write(ref function.Typed, published);
            }
        }
        catch (InvalidProgramException) when (!JitOps.ThrowOnInvalidProgram)
        {
            // The .NET code is not valid: the exception is thrown before it runs, so the function is interpreted instead
            function.Code.CompilationFailed = true;
            function.Code.Compiled = null;
            function.Code.PendingTyped = null;
            Run(function, fp);
        }
        finally
        {
            _jitDepthLimit = savedLimit;
            _top = savedTop;
        }
    }

    private void CallHost(HostFunction function, int fp, ManagedInstance? caller)
    {
        var type = function.DefinedType;
        var parameters = type.Parameters;
        var resultTypes = type.Results;

        // The arguments and the results are in a buffer of the machine, used as a stack (the callback can call WebAssembly
        // again): a larger buffer replaces it when it is full, the running callbacks keep the previous one
        var count = parameters.Length + resultTypes.Length;
        var start = _valueCount;
        if (start + count > _values.Length)
        {
            _values = new WasmValue[Math.Max(_values.Length * 2, start + count)];
        }

        var values = _values;
        var arguments = values.AsSpan(start, parameters.Length);
        var results = values.AsSpan(start + parameters.Length, resultTypes.Length);
        var parameterKinds = function.ParameterKinds;
        var resultKinds = function.ResultKinds;
        for (var i = 0; i < arguments.Length; i++)
        {
            if (parameterKinds is null)
            {
                arguments[i] = _stack[fp + i].ToValue(parameters[i]);
            }
            else
            {
                var kind = parameterKinds[i];
                var bits = _stack[fp + i].A;
                arguments[i] = WasmValue.FromNumberBits(kind, kind is WasmValueKind.I32 or WasmValueKind.F32 ? (int)bits : bits);
            }
        }

        for (var i = 0; i < results.Length; i++)
        {
            results[i] = resultKinds is null ? WasmValue.Default(resultTypes[i].ToPublic()) : WasmValue.FromNumberBits(resultKinds[i], 0);
        }

        var savedTop = _top;
        _top = fp + Math.Max(parameters.Length, resultTypes.Length);
        _valueCount = start + count;
        try
        {
            if (function.Callback is { } callback)
            {
                callback(arguments, results);
            }
            else
            {
                function.CallerCallback!(new WasmCaller(_store, caller), arguments, results);
            }

            _top = savedTop;
            EnsureCapacity(fp + results.Length);
            for (var i = 0; i < results.Length; i++)
            {
                // A number of the expected kind is the bits of the slot (the conversion reports the other values)
                _stack[fp + i] = resultKinds is not null && results[i].Kind == resultKinds[i] ? new Slot { A = results[i].Bits } : _store.ToSlot(results[i], resultTypes[i]);
            }
        }
        finally
        {
            _top = savedTop;
            _valueCount = start;
            values.AsSpan(start, count).Clear();
        }
    }

    /// <summary>Calls a function that is not interpreted by the running loop, and returns the WebAssembly exception it throws, or null.</summary>
    private ManagedException? CallExternal(ManagedFunction function, int fp, int top, ManagedInstance caller)
    {
        var savedTop = _top;
        _top = top;
        try
        {
            Call(function, fp, caller);
            return null;
        }
        catch (WasmThrowException ex) when (ex.Exception is ManagedException exception)
        {
            return exception;
        }
        finally
        {
            _top = savedTop;
        }
    }

    /// <summary>Evaluates a constant expression of an instance.</summary>
    public Slot Evaluate(CompiledCode code, ManagedInstance instance)
    {
        var function = new CodeFunction(_store, _runtime.Registry.GetFunctionType([], code.Results), instance, code);
        var fp = _top;
        EnsureCapacity(fp + code.FrameSize);
        Run(function, fp);
        var result = _stack[fp];
        _stack[fp] = default;
        return result;
    }

    private void EnsureCapacity(int size)
    {
        if (size > _stack.Length)
        {
            Array.Resize(ref _stack, Math.Max(size, Math.Min(_stack.Length * 2, Math.Max(_maxSlots, size))));
        }
    }

    /// <summary>Starts a frame: the locals that are not parameters are zeroed. Returns the first slot of the operands.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int EnterFrame(CompiledCode code, int fp)
    {
        var end = fp + code.FrameSize;
        if (end > _stack.Length)
        {
            if (end > _maxSlots)
                throw Traps.StackOverflow();

            EnsureCapacity(end);
        }

        if (end > _highWater)
        {
            _highWater = end;
        }

        var locals = code.Locals.Length;
        var parameters = code.ParameterCount;
        if (locals > parameters)
        {
            Array.Clear(_stack, fp + parameters, locals - parameters);
        }

        return fp + locals;
    }

    private void PushFrame(CodeFunction function, int pc, int fp)
    {
        if (_frameCount + _nativeDepth >= _maxFrames)
            throw Traps.StackOverflow();

        if (_frameCount == _frames.Length)
        {
            Array.Resize(ref _frames, _frames.Length * 2);
        }

        _frames[_frameCount++] = new Frame { Function = function, Pc = pc, Fp = fp };
    }

    /// <summary>Asks the running code to call <see cref="WasmStoreOptions.CheckInterrupt"/> (the timer of the interruptions of the runtime calls it at each interval).</summary>
    internal void RequestInterruptCheck() => Volatile.Write(ref _interruptRequested, value: true);

    /// <summary>
    /// Lets the host interrupt the running code (<see cref="WasmStoreOptions.CheckInterrupt"/>), when the timer of the
    /// interruptions asked for it: the running code reads a field of the machine (the read is volatile, so that the loops
    /// read it again).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void CheckInterrupt()
    {
        if (Volatile.Read(ref _interruptRequested))
        {
            CheckInterruptSlow();
        }
    }

    /// <summary>Calls <see cref="WasmStoreOptions.CheckInterrupt"/> (the compiled code calls it when <see cref="_interruptRequested"/> is set).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void CheckInterruptSlow()
    {
        Volatile.Write(ref _interruptRequested, value: false);
        if (_store.Options.CheckInterrupt is not { } checkInterrupt)
            return;

        try
        {
            checkInterrupt();
        }
        catch
        {
            _runtime.Telemetry.RecordInterruption();
            throw;
        }
    }

    /// <summary>Runs an interpreted function whose arguments are at <paramref name="fp"/>.</summary>
    public void Run(CodeFunction function, int fp)
    {
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
            throw Traps.StackOverflow();

        var entryFrames = _frameCount;
        var savedTop = _top;
        try
        {
            Execute(function, fp, entryFrames);
        }
        finally
        {
            _frameCount = entryFrames;
            _top = savedTop;
        }
    }

    private static ulong Address(ManagedMemory memory, long value) => memory.Is64 ? (ulong)value : (uint)value;

    private static ulong Address(ManagedTable table, long value) => table.Info.Is64 ? (ulong)value : (uint)value;

    /// <summary>
    /// Copies a slot: the reference is written only when one of the slots has one, because the write of a reference in an
    /// array calls the write barrier of the GC (most values are numbers).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CopySlot(ref Slot source, ref Slot destination)
    {
        destination.A = source.A;
        destination.B = source.B;
        var reference = source.R;
        if (reference is not null || destination.R is not null)
        {
            destination.R = reference;
        }
    }

    /// <summary>Writes a number to a slot (see <see cref="CopySlot"/>).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetNumber(ref Slot slot, long value)
    {
        slot.A = value;
        slot.B = 0;
        if (slot.R is not null)
        {
            slot.R = null;
        }
    }

    private static void CopySlots(Slot[] stack, int source, int destination, int count)
    {
        if (source != destination && count > 0)
        {
            Array.Copy(stack, source, stack, destination, count);
        }
    }
}

/// <summary>The .NET code of a function of an instance: its arguments are at <c>fp</c> on the stack of the machine, where it writes its results; <c>depth</c> is the number of frames of .NET code with this one.</summary>
internal delegate void JitFunction(Machine machine, ManagedInstance instance, int fp, int depth);
