using System.Runtime.CompilerServices;

namespace Meziantou.Framework.WasmEngine;

/// <summary>The methods of the machine that the .NET code of the functions calls (see <see cref="IlCompiler"/>).</summary>
internal sealed unsafe partial class Machine
{
    /// <summary>Returns the stack, which has at least <paramref name="size"/> slots.</summary>
    /// <exception cref="WasmTrapException">The stack is exhausted.</exception>
    internal Slot[] JitStack(int size)
    {
        if (size > _highWater)
        {
            _highWater = size;
        }

        if (size > _stack.Length)
        {
            if (size > _maxSlots)
                throw Traps.StackOverflow();

            EnsureCapacity(size);
        }

        return _stack;
    }

    /// <summary>
    /// The deepest frame of .NET code that can start (see <see cref="CallCompiled"/>): the .NET code receives its depth as
    /// an argument and gives its depth + 1 to its callees, so that the count is not written to memory at each call.
    /// </summary>
    internal int _jitDepthLimit;

    /// <summary>
    /// The lowest address of the stack of .NET at which the small frames of .NET code start without checking the stack
    /// (see <see cref="JitCheckStack"/>): the stack has at least <see cref="StackSafetyMargin"/> bytes below it.
    /// </summary>
    internal nuint _jitStackBound;

    // The thread whose stack contains _jitStackBound
    private int _jitStackThread;

    /// <summary>
    /// The size of the stack of .NET that the code can use below <see cref="_jitStackBound"/>: the small frames are smaller
    /// than a few kilobytes, and <see cref="RuntimeHelpers.TryEnsureSufficientExecutionStack"/> ensures that at least 64
    /// kilobytes are available.
    /// </summary>
    private const int StackSafetyMargin = 32 * 1024;

    /// <summary>Checks the stack of .NET, and moves <see cref="_jitStackBound"/> below the current frame.</summary>
    /// <exception cref="WasmTrapException">The stack is exhausted.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void JitCheckStack()
    {
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
            throw Traps.StackOverflow();

        byte probe = 0;
        _jitStackBound = (nuint)(&probe) - StackSafetyMargin;
        _jitStackThread = Environment.CurrentManagedThreadId;
    }

    /// <summary>
    /// Checks the stack of .NET before the machine calls .NET code: the check of .NET runs when the frame is below
    /// <see cref="_jitStackBound"/>, or when the bound was computed on another thread (the machine can run on another
    /// thread than before).
    /// </summary>
    /// <exception cref="WasmTrapException">The stack is exhausted.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CheckStackFromMachine()
    {
        byte probe = 0;
        if ((nuint)(&probe) < _jitStackBound || _jitStackThread != Environment.CurrentManagedThreadId)
        {
            JitCheckStack();
        }
    }


    /// <summary>The number of loop iterations that count as one call for the OSR (see <see cref="TryOsr"/>).</summary>
    private const int OsrIterationsPerCall = 100;

    /// <summary>
    /// Continues the interpreted call of <paramref name="function"/> at the loop <paramref name="pc"/> in .NET code, once its
    /// loops ran many times (OSR): returns true when the compiled code ran the rest of the call, whose results are at
    /// <paramref name="fp"/>, or whose exception is <paramref name="exception"/> (the compiled code did not catch it).
    /// Returns false when the function cannot be compiled: the interpreter continues.
    /// </summary>
    private bool TryOsr(CodeFunction function, int fp, int pc, out ManagedException? exception)
    {
        exception = null;
        var code = function.Code;
        if (_compilationThreshold < 0 || code.CompilationFailed || !RuntimeFeature.IsDynamicCodeSupported)
            return false;

        var osr = Volatile.Read(ref code.Osr);
        if (osr is null)
        {
            // One thread compiles the method once
            if (Volatile.Read(ref code.OsrStarted) != 0 || Interlocked.Exchange(ref code.OsrStarted, 1) != 0)
                return false;

            try
            {
                osr = IlCompiler.CompileOsr(function, out var loops);
                code.OsrLoops = loops;
            }
#pragma warning disable CA1031 // A failure of the compiler must not fail the call: the function is interpreted
            catch (Exception ex) when (!JitOps.ThrowOnInvalidProgram && ex is not OutOfMemoryException)
#pragma warning restore CA1031
            {
                osr = null;
            }

            if (osr is null)
                return false;

            Volatile.Write(ref code.Osr, osr);
        }

        var entry = Array.IndexOf(code.OsrLoops, pc);
        if (entry < 0)
            return false;

        CheckStackFromMachine();
        var depth = _nativeDepth + 1;
        if (depth + _frameCount > _maxFrames)
            throw Traps.StackOverflow();

        var savedTop = _top;
        var savedLimit = _jitDepthLimit;
        _jitDepthLimit = _maxFrames - _frameCount;
        try
        {
            osr(this, function.Instance, fp, depth, entry);
            return true;
        }
        catch (InvalidProgramException) when (!JitOps.ThrowOnInvalidProgram)
        {
            // The .NET code is not valid: the exception is thrown before it runs, so the call continues in the interpreter
            code.Osr = null;
            return false;
        }
        catch (WasmThrowException ex) when (ex.Exception is ManagedException managed)
        {
            exception = managed;
            return true;
        }
        finally
        {
            _jitDepthLimit = savedLimit;
            _top = savedTop;
        }
    }

    /// <summary>The exception of a frame deeper than <see cref="_jitDepthLimit"/>.</summary>
    internal static Exception JitStackOverflow() => Traps.StackOverflow();

    /// <summary>Calls a function whose arguments are at <paramref name="fp"/>, the slots from <paramref name="top"/> being free, from a frame of .NET code at <paramref name="depth"/>.</summary>
    internal void JitCall(ManagedFunction callee, int fp, int top, int depth)
    {
        var savedTop = _top;
        var savedDepth = _nativeDepth;
        _top = top;
        _nativeDepth = depth;
        try
        {
            Call(callee, fp);
        }
        finally
        {
            _top = savedTop;
            _nativeDepth = savedDepth;
        }
    }

    /// <summary>Runs an instruction that the .NET code does not implement: its operands are below <paramref name="sp"/>.</summary>
    internal void JitExecute(ref Instr ins, ManagedInstance instance, int sp)
    {
        var stack = _stack;
        switch (ins.Op)
        {
            case Op.RefTest:
            case Op.RefTestNull:
            {
                ref var slot = ref stack[sp - 1];
                slot.A = RuntimeTypes.Matches(slot.R, (WasmHeapKind)ins.I0, (DefinedType?)ins.O, ins.I1 != 0) ? 1 : 0;
                slot.R = null;
                break;
            }

            case Op.RefCast:
            case Op.RefCastNull:
                if (!RuntimeTypes.Matches(stack[sp - 1].R, (WasmHeapKind)ins.I0, (DefinedType?)ins.O, ins.I1 != 0))
                    throw Traps.CastFailure();

                break;

            case Op.RefI31:
            {
                ref var slot = ref stack[sp - 1];
                slot.R = new WasmI31Reference((int)(slot.A << 33 >> 33));
                break;
            }

            case Op.I31GetS:
            case Op.I31GetU:
            {
                ref var slot = ref stack[sp - 1];
                var value = ((WasmI31Reference?)slot.R ?? throw Traps.NullI31Reference()).Value;
                slot.A = ins.Op == Op.I31GetS ? value : value & 0x7FFF_FFFF;
                slot.R = null;
                break;
            }

            case Op.I64Add128:
            {
                var (low, high) = Numerics.Add128(stack[sp - 4].A, stack[sp - 3].A, stack[sp - 2].A, stack[sp - 1].A);
                stack[sp - 4].A = low;
                stack[sp - 3].A = high;
                break;
            }

            case Op.I64Sub128:
            {
                var (low, high) = Numerics.Sub128(stack[sp - 4].A, stack[sp - 3].A, stack[sp - 2].A, stack[sp - 1].A);
                stack[sp - 4].A = low;
                stack[sp - 3].A = high;
                break;
            }

            case Op.I64MulWideS:
            {
                var (low, high) = Numerics.MulWideS(stack[sp - 2].A, stack[sp - 1].A);
                stack[sp - 2].A = low;
                stack[sp - 1].A = high;
                break;
            }

            case Op.I64MulWideU:
            {
                var (low, high) = Numerics.MulWideU(stack[sp - 2].A, stack[sp - 1].A);
                stack[sp - 2].A = low;
                stack[sp - 1].A = high;
                break;
            }

            default:
                ExecuteExtended(in ins, instance, stack, ref sp);
                break;
        }
    }
}
