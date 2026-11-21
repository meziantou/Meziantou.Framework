using System.Reflection;
using System.Reflection.Emit;

namespace Meziantou.Framework.WasmEngine;

/// <summary>
/// The floating-point values: the <c>f32</c> and <c>f64</c> locals are <see cref="float"/> and <see cref="double"/>
/// locals of the method, and the operands that the floating-point instructions produce stay <see cref="float"/> and
/// <see cref="double"/> values until another instruction reads them, so that the JIT of .NET keeps them in the
/// floating-point registers. The other instructions, the branches and their targets see the bits of the values
/// (<see cref="Slot.A"/>): the compiler writes the bits of the floating-point operands before them.
/// </summary>
internal sealed partial class IlCompiler
{
    private static readonly MethodInfo DoubleToBits = GetMethod(typeof(BitConverter), nameof(BitConverter.DoubleToInt64Bits));
    private static readonly MethodInfo BitsToDouble = GetMethod(typeof(BitConverter), nameof(BitConverter.Int64BitsToDouble));
    private static readonly MethodInfo SingleToBits = GetMethod(typeof(BitConverter), nameof(BitConverter.SingleToInt32Bits));
    private static readonly MethodInfo BitsToSingle = GetMethod(typeof(BitConverter), nameof(BitConverter.Int32BitsToSingle));
    private static readonly MethodInfo DoubleSqrt = GetMethod(typeof(Math), nameof(Math.Sqrt));
    private static readonly MethodInfo SingleSqrt = GetMethod(typeof(MathF), nameof(MathF.Sqrt));
    private static readonly MethodInfo ConvertU64ToF64 = GetMethod(typeof(Numerics), nameof(Numerics.ConvertU64ToF64));
    private static readonly MethodInfo ConvertU64ToF32 = GetMethod(typeof(Numerics), nameof(Numerics.ConvertU64ToF32));
    private static readonly MethodInfo Promote = GetMethod(typeof(Numerics), nameof(Numerics.Promote));
    private static readonly MethodInfo Demote = GetMethod(typeof(Numerics), nameof(Numerics.Demote));

    /// <summary>Where the value of an operand is: its bits (<see cref="_stackA"/>), or a floating-point local.</summary>
    private enum ValueKind : byte
    {
        Bits,
        F64,
        F32,
    }

    private ValueKind[] _kinds = [];
    private LocalBuilder?[] _stackD = [];
    private LocalBuilder?[] _stackF = [];
    private LocalBuilder?[] _localD = [];
    private LocalBuilder?[] _localF = [];

    private void DeclareFloatLocals()
    {
        var locals = _code.Locals;
        _localD = new LocalBuilder?[locals.Length];
        _localF = new LocalBuilder?[locals.Length];
        for (var i = 0; i < locals.Length; i++)
        {
            if (locals[i].Code == ValueCode.F64)
            {
                _localD[i] = _il.DeclareLocal(typeof(double));
            }
            else if (locals[i].Code == ValueCode.F32)
            {
                _localF[i] = _il.DeclareLocal(typeof(float));
            }
        }

        _kinds = new ValueKind[_code.MaxStackHeight];
        _stackD = new LocalBuilder?[_code.MaxStackHeight];
        _stackF = new LocalBuilder?[_code.MaxStackHeight];
    }

    /// <summary>Converts the first <paramref name="count"/> floating-point locals, read as bits (<see cref="_localA"/>), to their locals.</summary>
    private void EmitFloatParameters(int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (_localD[i] is { } d)
            {
                _il.Emit(OpCodes.Ldloc, _localA[i]);
                _il.Emit(OpCodes.Call, BitsToDouble);
                _il.Emit(OpCodes.Stloc, d);
            }
            else if (_localF[i] is { } f)
            {
                _il.Emit(OpCodes.Ldloc, _localA[i]);
                _il.Emit(OpCodes.Conv_I4);
                _il.Emit(OpCodes.Call, BitsToSingle);
                _il.Emit(OpCodes.Stloc, f);
            }
        }
    }

    private LocalBuilder StackD(int h) => _stackD[h] ??= _il.DeclareLocal(typeof(double));

    private LocalBuilder StackF(int h) => _stackF[h] ??= _il.DeclareLocal(typeof(float));

    /// <summary>Writes the bits of the floating-point operands of the slots [<paramref name="from"/>, <paramref name="to"/>).</summary>
    private void MaterializeRange(int from, int to)
    {
        for (var h = Math.Max(from, 0); h < to && h < _kinds.Length; h++)
        {
            switch (_kinds[h])
            {
                case ValueKind.F64:
                    _il.Emit(OpCodes.Ldloc, _stackD[h]!);
                    _il.Emit(OpCodes.Call, DoubleToBits);
                    _il.Emit(OpCodes.Stloc, _stackA[h]);
                    break;

                case ValueKind.F32:
                    // The bits of an f32 value are an unsigned 32-bit number (see Numerics.Bits)
                    _il.Emit(OpCodes.Ldloc, _stackF[h]!);
                    _il.Emit(OpCodes.Call, SingleToBits);
                    _il.Emit(OpCodes.Conv_U8);
                    _il.Emit(OpCodes.Stloc, _stackA[h]);
                    break;
            }

            _kinds[h] = ValueKind.Bits;
        }
    }

    private void MaterializeAll(int height) => MaterializeRange(0, height);

    /// <summary>Marks the slots [<paramref name="from"/>, <paramref name="to"/>) as bits (the instruction writes them).</summary>
    private void SetBits(int from, int to)
    {
        for (var h = Math.Max(from, 0); h < to && h < _kinds.Length; h++)
        {
            _kinds[h] = ValueKind.Bits;
        }
    }

    /// <summary>Makes the operand of the slot <paramref name="h"/> a <see cref="double"/> local, and returns it.</summary>
    private LocalBuilder ToDouble(int h)
    {
        var d = StackD(h);
        if (_kinds[h] != ValueKind.F64)
        {
            _il.Emit(OpCodes.Ldloc, _stackA[h]);
            _il.Emit(OpCodes.Call, BitsToDouble);
            _il.Emit(OpCodes.Stloc, d);
            _kinds[h] = ValueKind.F64;
        }

        return d;
    }

    /// <summary>Makes the operand of the slot <paramref name="h"/> a <see cref="float"/> local, and returns it.</summary>
    private LocalBuilder ToSingle(int h)
    {
        var f = StackF(h);
        if (_kinds[h] != ValueKind.F32)
        {
            _il.Emit(OpCodes.Ldloc, _stackA[h]);
            _il.Emit(OpCodes.Conv_I4);
            _il.Emit(OpCodes.Call, BitsToSingle);
            _il.Emit(OpCodes.Stloc, f);
            _kinds[h] = ValueKind.F32;
        }

        return f;
    }

    /// <summary>Whether an instruction reads the operands below the ones it pops, or leaves the function (the branches, the calls, the exceptions): the compiler writes the bits of all the operands before it.</summary>
    private static bool ReadsAllOperands(ushort op) => op is Op.Jump or Op.BrCopy or Op.JumpIfZero or Op.BrIf or Op.BrTable or Op.BrOnNull or Op.BrOnNonNull or Op.BrOnCast or Op.BrOnCastFail
        or Op.Return or Op.End or Op.Unreachable or Op.Throw or Op.ThrowRef
        or Op.Call or Op.CallIndirect or Op.CallRef or Op.ReturnCall or Op.ReturnCallIndirect or Op.ReturnCallRef;

    /// <summary>Emits a floating-point instruction with <see cref="double"/> and <see cref="float"/> values, or returns false (the instruction reads and writes bits).</summary>
#pragma warning disable MA0051 // Method is too long: one case per instruction
    private bool TryEmitFloat(in Instr ins, int h)
    {
        switch (ins.Op)
        {
            // The JIT of .NET keeps the constants as double values: a NaN constant stays bits, so that a signaling NaN keeps its
            // payload
            case Op.F64Const when !double.IsNaN(BitConverter.Int64BitsToDouble(ins.L0)):
                _il.Emit(OpCodes.Ldc_R8, BitConverter.Int64BitsToDouble(ins.L0));
                _il.Emit(OpCodes.Stloc, StackD(h));
                _kinds[h] = ValueKind.F64;
                return true;

            case Op.F32Const when !float.IsNaN(BitConverter.Int32BitsToSingle((int)ins.L0)):
                _il.Emit(OpCodes.Ldc_R4, BitConverter.Int32BitsToSingle((int)ins.L0));
                _il.Emit(OpCodes.Stloc, StackF(h));
                _kinds[h] = ValueKind.F32;
                return true;

            case Op.LocalGet when _localD[ins.I0] is { } d:
                _il.Emit(OpCodes.Ldloc, d);
                _il.Emit(OpCodes.Stloc, StackD(h));
                _kinds[h] = ValueKind.F64;
                return true;

            case Op.LocalGet when _localF[ins.I0] is { } f:
                _il.Emit(OpCodes.Ldloc, f);
                _il.Emit(OpCodes.Stloc, StackF(h));
                _kinds[h] = ValueKind.F32;
                return true;

            case Op.LocalSet or Op.LocalTee when _localD[ins.I0] is { } d:
                _il.Emit(OpCodes.Ldloc, ToDouble(h - 1));
                _il.Emit(OpCodes.Stloc, d);
                return true;

            case Op.LocalSet or Op.LocalTee when _localF[ins.I0] is { } f:
                _il.Emit(OpCodes.Ldloc, ToSingle(h - 1));
                _il.Emit(OpCodes.Stloc, f);
                return true;

            case Op.F64Add:
                return EmitDoubleBinary(h, OpCodes.Add);
            case Op.F64Sub:
                return EmitDoubleBinary(h, OpCodes.Sub);
            case Op.F64Mul:
                return EmitDoubleBinary(h, OpCodes.Mul);
            case Op.F64Div:
                return EmitDoubleBinary(h, OpCodes.Div);
            case Op.F32Add:
                return EmitSingleBinary(h, OpCodes.Add);
            case Op.F32Sub:
                return EmitSingleBinary(h, OpCodes.Sub);
            case Op.F32Mul:
                return EmitSingleBinary(h, OpCodes.Mul);
            case Op.F32Div:
                return EmitSingleBinary(h, OpCodes.Div);

            case Op.F64Sqrt:
            {
                var d = ToDouble(h - 1);
                _il.Emit(OpCodes.Ldloc, d);
                _il.Emit(OpCodes.Call, DoubleSqrt);
                _il.Emit(OpCodes.Stloc, d);
                return true;
            }

            case Op.F32Sqrt:
            {
                var f = ToSingle(h - 1);
                _il.Emit(OpCodes.Ldloc, f);
                _il.Emit(OpCodes.Call, SingleSqrt);
                _il.Emit(OpCodes.Stloc, f);
                return true;
            }

            case Op.F64Eq or Op.F64Ne or Op.F64Lt or Op.F64Gt or Op.F64Le or Op.F64Ge:
                _il.Emit(OpCodes.Ldloc, ToDouble(h - 2));
                _il.Emit(OpCodes.Ldloc, ToDouble(h - 1));
                EmitComparison(ins.Op);
                _il.Emit(OpCodes.Stloc, _stackA[h - 2]);
                _kinds[h - 2] = ValueKind.Bits;
                return true;

            case Op.F32Eq or Op.F32Ne or Op.F32Lt or Op.F32Gt or Op.F32Le or Op.F32Ge:
                _il.Emit(OpCodes.Ldloc, ToSingle(h - 2));
                _il.Emit(OpCodes.Ldloc, ToSingle(h - 1));
                EmitComparison(ins.Op);
                _il.Emit(OpCodes.Stloc, _stackA[h - 2]);
                _kinds[h - 2] = ValueKind.Bits;
                return true;

            // The conversions of integers: the operand is the bits of an integer
            case Op.F64ConvertI32S:
                return EmitIntegerToFloat(h, ValueKind.F64, OpCodes.Conv_I4, OpCodes.Conv_R8);
            case Op.F64ConvertI32U:
                return EmitIntegerToFloat(h, ValueKind.F64, OpCodes.Conv_U4, OpCodes.Conv_R_Un, OpCodes.Conv_R8);
            case Op.F64ConvertI64S:
                return EmitIntegerToFloat(h, ValueKind.F64, OpCodes.Conv_R8);
            case Op.F32ConvertI32S:
                return EmitIntegerToFloat(h, ValueKind.F32, OpCodes.Conv_I4, OpCodes.Conv_R4);
            case Op.F32ConvertI32U:
                return EmitIntegerToFloat(h, ValueKind.F32, OpCodes.Conv_U4, OpCodes.Conv_R_Un, OpCodes.Conv_R4);
            case Op.F32ConvertI64S:
                return EmitIntegerToFloat(h, ValueKind.F32, OpCodes.Conv_R4);

            case Op.F64ConvertI64U:
                MaterializeRange(h - 1, h);
                _il.Emit(OpCodes.Ldloc, _stackA[h - 1]);
                _il.Emit(OpCodes.Call, ConvertU64ToF64);
                _il.Emit(OpCodes.Stloc, StackD(h - 1));
                _kinds[h - 1] = ValueKind.F64;
                return true;

            case Op.F32ConvertI64U:
                MaterializeRange(h - 1, h);
                _il.Emit(OpCodes.Ldloc, _stackA[h - 1]);
                _il.Emit(OpCodes.Call, ConvertU64ToF32);
                _il.Emit(OpCodes.Stloc, StackF(h - 1));
                _kinds[h - 1] = ValueKind.F32;
                return true;

            case Op.F64PromoteF32:
                _il.Emit(OpCodes.Ldloc, ToSingle(h - 1));
                _il.Emit(OpCodes.Call, Promote);
                _il.Emit(OpCodes.Stloc, StackD(h - 1));
                _kinds[h - 1] = ValueKind.F64;
                return true;

            case Op.F32DemoteF64:
                _il.Emit(OpCodes.Ldloc, ToDouble(h - 1));
                _il.Emit(OpCodes.Call, Demote);
                _il.Emit(OpCodes.Stloc, StackF(h - 1));
                _kinds[h - 1] = ValueKind.F32;
                return true;

            case Op.Drop:
                _kinds[h - 1] = ValueKind.Bits;
                return true;

            case Op.F64Load or Op.F32Load or Op.F64Store or Op.F32Store when _inlineMemories.TryGetValue(ins.I0, out var memory):
                EmitInlineFloatAccess(in ins, h, memory);
                return true;

            default:
                return false;
        }
    }
#pragma warning restore MA0051

    private bool EmitDoubleBinary(int h, OpCode operation)
    {
        var left = ToDouble(h - 2);
        _il.Emit(OpCodes.Ldloc, left);
        _il.Emit(OpCodes.Ldloc, ToDouble(h - 1));
        _il.Emit(operation);
        _il.Emit(OpCodes.Stloc, left);
        return true;
    }

    private bool EmitSingleBinary(int h, OpCode operation)
    {
        var left = ToSingle(h - 2);
        _il.Emit(OpCodes.Ldloc, left);
        _il.Emit(OpCodes.Ldloc, ToSingle(h - 1));
        _il.Emit(operation);
        _il.Emit(OpCodes.Stloc, left);
        return true;
    }

    /// <summary>
    /// Compares the two floating-point values of the stack of the IL, and pushes 1 or 0 as a 64-bit number: eq, ne, lt, gt,
    /// le, ge (the default case). A NaN is unordered: only ne is true (le is "not greater or unordered").
    /// </summary>
    private void EmitComparison(ushort op)
    {
        switch (op)
        {
            case Op.F64Eq or Op.F32Eq:
                _il.Emit(OpCodes.Ceq);
                break;
            case Op.F64Ne or Op.F32Ne:
                _il.Emit(OpCodes.Ceq);
                _il.Emit(OpCodes.Ldc_I4_0);
                _il.Emit(OpCodes.Ceq);
                break;
            case Op.F64Lt or Op.F32Lt:
                _il.Emit(OpCodes.Clt);
                break;
            case Op.F64Gt or Op.F32Gt:
                _il.Emit(OpCodes.Cgt);
                break;
            case Op.F64Le or Op.F32Le:
                _il.Emit(OpCodes.Cgt_Un);
                _il.Emit(OpCodes.Ldc_I4_0);
                _il.Emit(OpCodes.Ceq);
                break;
            default:
                _il.Emit(OpCodes.Clt_Un);
                _il.Emit(OpCodes.Ldc_I4_0);
                _il.Emit(OpCodes.Ceq);
                break;
        }

        _il.Emit(OpCodes.Conv_I8);
    }

    /// <summary>Converts the integer of the slot h - 1 (its bits) with the conversions of IL (the ones that C# emits for the casts of <see cref="JitOps"/>).</summary>
    private bool EmitIntegerToFloat(int h, ValueKind kind, params OpCode[] conversions)
    {
        MaterializeRange(h - 1, h);
        _il.Emit(OpCodes.Ldloc, _stackA[h - 1]);
        foreach (var conversion in conversions)
        {
            _il.Emit(conversion);
        }

        _il.Emit(OpCodes.Stloc, kind == ValueKind.F64 ? StackD(h - 1) : StackF(h - 1));
        _kinds[h - 1] = kind;
        return true;
    }

    /// <summary>A load or a store of a floating-point value of a memory whose accesses are inline (see <see cref="EmitInlineAccess"/>).</summary>
    private void EmitInlineFloatAccess(in Instr ins, int h, (LocalBuilder Base, LocalBuilder Length) memory)
    {
        var isLoad = ins.Op is Op.F64Load or Op.F32Load;
        var isDouble = ins.Op is Op.F64Load or Op.F64Store;
        var addressSlot = isLoad ? h - 1 : h - 2;
        MaterializeRange(addressSlot, addressSlot + 1);
        EmitInlineAddress(in ins, _stackA[addressSlot], memory, isDouble ? 8 : 4);
        if (isLoad)
        {
            _il.Emit(OpCodes.Unaligned, (byte)1);
            _il.Emit(isDouble ? OpCodes.Ldind_R8 : OpCodes.Ldind_R4);
            _il.Emit(OpCodes.Stloc, isDouble ? StackD(h - 1) : StackF(h - 1));
            _kinds[h - 1] = isDouble ? ValueKind.F64 : ValueKind.F32;
        }
        else
        {
            // The address is on the stack of the IL: the conversion of the value to a floating-point local does not use it
            _il.Emit(OpCodes.Ldloc, isDouble ? ToDouble(h - 1) : ToSingle(h - 1));
            _il.Emit(OpCodes.Unaligned, (byte)1);
            _il.Emit(isDouble ? OpCodes.Stind_R8 : OpCodes.Stind_R4);
            _kinds[h - 1] = ValueKind.Bits;
            _kinds[h - 2] = ValueKind.Bits;
        }
    }
}
