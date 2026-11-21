using System.Reflection.Emit;

namespace Meziantou.Framework.WasmEngine;

/// <summary>
/// The inlining of the small functions of the module: a call of a small function without calls, loops and handlers,
/// whose instructions the compiler emits without the machine and without the data of the closure, is replaced by the
/// code of the function (a compiler of the callee emits it into the method of the caller, with its own locals).
/// </summary>
internal sealed partial class IlCompiler
{
    /// <summary>The maximum number of instructions of an inlined function.</summary>
    private const int MaxInlinedInstructions = 32;

    /// <summary>The maximum number of locals of the inlined functions of a method: the JIT of .NET tracks a limited number of locals.</summary>
    private const int MaxInlinedLocals = 256;

    // The state of a compiler that inlines a callee: the slot of the caller that receives the result, and the end of the
    // code of the callee
    private bool _isInline;
    private LocalBuilder? _inlineResult;
    private Label _inlineEnd;
    private int _inlinedLocals;
    private bool _callInlined;

    /// <summary>Whether the last call was inlined (it does not need to read the memories again), and resets it.</summary>
    private bool TakeCallInlined()
    {
        var inlined = _callInlined;
        _callInlined = false;
        return inlined;
    }

    /// <summary>Whether the compiler emits all the instructions of a function without the machine, the closure and the calls.</summary>
    private bool HasInlinableInstructions(CompiledCode code)
    {
        if (code.Code.Length > MaxInlinedInstructions || code.Handlers.Length > 0 || code.Heights.Length != code.Code.Length || code.Pops.Length != code.Code.Length)
            return false;

        foreach (var local in code.Locals)
        {
            if (!local.IsNumeric)
                return false;
        }

        foreach (ref readonly var ins in code.Code.AsSpan())
        {
            var op = ins.Op;
            if (UnaryOperations.ContainsKey(op) || BinaryOperations.ContainsKey(op) || Loads.ContainsKey(op) || Stores.ContainsKey(op))
                continue;

            switch (op)
            {
                case Op.Nop or Op.Drop or Op.Unreachable or Op.Select:
                case Op.I32Const or Op.I64Const or Op.F32Const or Op.F64Const:
                case Op.Jump or Op.BrCopy or Op.JumpIfZero or Op.BrIf:
                case Op.Return or Op.End:
                case Op.LocalGet or Op.LocalSet or Op.LocalTee or Op.MemorySize:
                    break;

                case Op.GlobalGet or Op.GlobalSet when _module.Globals[ins.I0].Type.IsNumeric:
                    break;

                default:
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Emits the code of a function of the module instead of its call, when it is small (see <see cref="HasInlinableInstructions"/>):
    /// its arguments are the operands of the caller from <paramref name="bottom"/>, and its result goes to the slot <paramref name="bottom"/>.
    /// </summary>
    private bool TryEmitInline(CompiledCode callee, int bottom, int results)
    {
        if (_isInline || _inlinedLocals >= MaxInlinedLocals || !HasInlinableInstructions(callee))
            return false;

        var inlined = new IlCompiler(callee, _module);
        if (!inlined._typed || !inlined.Analyze() || inlined._hasV128 || inlined._hasRefs || inlined._makesCalls || inlined._hasLoops)
            return false;

        _inlinedLocals += inlined._methodLocals + callee.Locals.Length;
        inlined._il = _il;
        inlined._isInline = true;
        inlined._inlineResult = results == 0 ? null : _stackA[bottom];
        inlined._inlineEnd = _il.DefineLabel();
        inlined.DeclareLocals();
        inlined.EmitInlinePrologue(_stackA, bottom);

        // The instructions were checked: a failure is an error of the compiler (the function is interpreted)
        if (!inlined.EmitBody())
            throw new InvalidOperationException("The inlined function " + callee.Name + " cannot be compiled");

        _il.MarkLabel(inlined._inlineEnd);
        return true;
    }

    /// <summary>The start of an inlined function: the objects of the instance (the caller has the same instance), the parameters, and the other locals (zero).</summary>
    private void EmitInlinePrologue(LocalBuilder[] callerStack, int bottom)
    {
        RegisterInstanceObjects();
        LoadInstanceObjects(_memories, InstanceMemories);
        LoadInstanceObjects(_globals, InstanceGlobals);
        LoadInstanceObjects(_tables, InstanceTables);
        EmitReloadMemories();

        // The locals of the method are zero when it starts only: the inlined code can run several times
        for (var i = 0; i < _code.Locals.Length; i++)
        {
            if (i < _code.ParameterCount)
            {
                _il.Emit(OpCodes.Ldloc, callerStack[bottom + i]);
            }
            else
            {
                _il.Emit(OpCodes.Ldc_I8, 0L);
            }

            _il.Emit(OpCodes.Stloc, _localA[i]);
        }

        EmitFloatParameters(_code.ParameterCount);
        for (var i = _code.ParameterCount; i < _code.Locals.Length; i++)
        {
            if (_localD[i] is { } d)
            {
                _il.Emit(OpCodes.Ldc_R8, 0d);
                _il.Emit(OpCodes.Stloc, d);
            }
            else if (_localF[i] is { } f)
            {
                _il.Emit(OpCodes.Ldc_R4, 0f);
                _il.Emit(OpCodes.Stloc, f);
            }
        }
    }

    /// <summary>The return of an inlined function: its result goes to the slot of the caller, and the code continues after the inlined code.</summary>
    private void EmitInlineReturn(in Instr ins, int h)
    {
        if (ins.I1 != 0 && _inlineResult is not null)
        {
            _il.Emit(OpCodes.Ldloc, _stackA[h - 1]);
            _il.Emit(OpCodes.Stloc, _inlineResult);
        }

        _il.Emit(OpCodes.Br, _inlineEnd);
    }
}
