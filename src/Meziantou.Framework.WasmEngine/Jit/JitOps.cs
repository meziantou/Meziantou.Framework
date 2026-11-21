using System.Numerics;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.WasmEngine;

/// <summary>
/// The operations that the .NET code of the functions calls (see <see cref="IlCompiler"/>). The values are the bits of
/// the slots (<see cref="Slot.A"/>), as in the interpreter. The methods of the numeric operations have the name of their
/// operation (see <see cref="Op"/>): the compiler finds them by name.
/// </summary>
internal static unsafe class JitOps
{
    /// <summary>Whether the code that is not valid throws (the tests), instead of being interpreted.</summary>
    public static bool ThrowOnInvalidProgram { get; set; }

    // ------------------------------------------------------------------------------------------------------------
    // Control
    // ------------------------------------------------------------------------------------------------------------

    public static Exception UnreachableTrap() => Traps.Unreachable();

    public static ManagedFunction CallRefTarget(object? reference) => (ManagedFunction?)reference ?? throw Traps.NullFunctionReference();

    /// <summary>The function of a table that an indirect call calls: the function of the expected type first, then the subtypes and the traps.</summary>
    /// <remarks>The compiler converts the index of a 32-bit table: <paramref name="address"/> is the index in the table.</remarks>
    public static ManagedFunction CallIndirectTarget(ManagedTable table, ulong address, DefinedType type)
    {
        // A sealed class is tested without the helper of .NET that walks the base classes
        if (address < table.Size && table.Elements[(int)address] is CodeFunction function && ReferenceEquals(function.DefinedType, type))
            return function;

        return Machine.GetIndirectFunction(table, (long)address, type);
    }

    public static Exception Throw(ManagedInstance instance, int tagIndex, Slot[] stack, int start)
    {
        var tag = instance.Tags[tagIndex];
        var count = tag.FunctionType.Parameters.Length;
        var payload = count == 0 ? [] : new Slot[count];
        Array.Copy(stack, start, payload, 0, count);
        return new WasmThrowException(new ManagedException(tag, payload));
    }

    public static Exception ThrowReference(object? reference) => new WasmThrowException((ManagedException?)reference ?? throw Traps.NullExceptionReference());

    /// <summary>The index of the catch clause that handles an exception thrown at <paramref name="pc"/>, in the order of the handlers and of their clauses, or -1.</summary>
    public static int FindClause(CompiledCode code, ManagedInstance instance, int pc, WasmThrowException exception) => exception.Exception is ManagedException managed ? Machine.FindClause(code, instance, pc, managed, out _) : -1;

    public static Slot[] Payload(WasmThrowException exception) => ((ManagedException)exception.Exception).Payload;

    public static object ExceptionObject(WasmThrowException exception) => exception.Exception;

    // ------------------------------------------------------------------------------------------------------------
    // References and tables
    // ------------------------------------------------------------------------------------------------------------

    public static void CheckNonNull(object? reference)
    {
        if (reference is null)
            throw Traps.NullReference();
    }

    public static long RefEq(object? left, object? right) => RuntimeTypes.RefEquals(left, right) ? 1 : 0;

    public static bool Matches(object? reference, ref Instr ins) => RuntimeTypes.Matches(reference, (WasmHeapKind)(ins.L1 >> 1), (DefinedType?)ins.O, (ins.L1 & 1) != 0);

    public static object? TableGet(ManagedTable table, long indexValue)
    {
        var index = table.Info.Is64 ? (ulong)indexValue : (uint)indexValue;
        if (index >= table.Size)
            throw Traps.OutOfBoundsTable();

        return table.Elements[(int)index];
    }

    public static void TableSet(ManagedTable table, long indexValue, object? value)
    {
        var index = table.Info.Is64 ? (ulong)indexValue : (uint)indexValue;
        if (index >= table.Size)
            throw Traps.OutOfBoundsTable();

        table.Elements[(int)index] = value;
    }

    // ------------------------------------------------------------------------------------------------------------
    // Memory
    // ------------------------------------------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte* Address(ManagedMemory memory, long address, long offset, int size) => memory.GetAddress(memory.Is64 ? (ulong)address : (uint)address, (ulong)offset, size);

    /// <summary>The trap of an access out of a memory (the inline accesses of the compiled code throw it).</summary>
    public static Exception OutOfBoundsMemoryTrap() => Traps.OutOfBoundsMemory();

    public static long MemorySize(ManagedMemory memory) => (long)memory.Size;

    public static long MemoryGrow(ManagedMemory memory, long delta)
    {
        var result = memory.TryGrow(memory.Is64 ? (ulong)delta : (uint)delta);
        return memory.Is64 ? result : (int)result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long I32Load(ManagedMemory memory, long address, long offset) => Unsafe.ReadUnaligned<int>(Address(memory, address, offset, 4));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long I64Load(ManagedMemory memory, long address, long offset) => Unsafe.ReadUnaligned<long>(Address(memory, address, offset, 8));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long F32Load(ManagedMemory memory, long address, long offset) => Unsafe.ReadUnaligned<int>(Address(memory, address, offset, 4));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long F64Load(ManagedMemory memory, long address, long offset) => Unsafe.ReadUnaligned<long>(Address(memory, address, offset, 8));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long I32Load8S(ManagedMemory memory, long address, long offset) => *(sbyte*)Address(memory, address, offset, 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long I32Load8U(ManagedMemory memory, long address, long offset) => *Address(memory, address, offset, 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long I32Load16S(ManagedMemory memory, long address, long offset) => Unsafe.ReadUnaligned<short>(Address(memory, address, offset, 2));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long I32Load16U(ManagedMemory memory, long address, long offset) => Unsafe.ReadUnaligned<ushort>(Address(memory, address, offset, 2));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long I64Load8S(ManagedMemory memory, long address, long offset) => *(sbyte*)Address(memory, address, offset, 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long I64Load8U(ManagedMemory memory, long address, long offset) => *Address(memory, address, offset, 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long I64Load16S(ManagedMemory memory, long address, long offset) => Unsafe.ReadUnaligned<short>(Address(memory, address, offset, 2));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long I64Load16U(ManagedMemory memory, long address, long offset) => Unsafe.ReadUnaligned<ushort>(Address(memory, address, offset, 2));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long I64Load32S(ManagedMemory memory, long address, long offset) => Unsafe.ReadUnaligned<int>(Address(memory, address, offset, 4));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long I64Load32U(ManagedMemory memory, long address, long offset) => Unsafe.ReadUnaligned<uint>(Address(memory, address, offset, 4));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void I32Store(ManagedMemory memory, long address, long offset, long value) => Unsafe.WriteUnaligned(Address(memory, address, offset, 4), (int)value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void I64Store(ManagedMemory memory, long address, long offset, long value) => Unsafe.WriteUnaligned(Address(memory, address, offset, 8), value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void F32Store(ManagedMemory memory, long address, long offset, long value) => Unsafe.WriteUnaligned(Address(memory, address, offset, 4), (int)value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void F64Store(ManagedMemory memory, long address, long offset, long value) => Unsafe.WriteUnaligned(Address(memory, address, offset, 8), value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void I32Store8(ManagedMemory memory, long address, long offset, long value) => *Address(memory, address, offset, 1) = (byte)value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void I32Store16(ManagedMemory memory, long address, long offset, long value) => Unsafe.WriteUnaligned(Address(memory, address, offset, 2), (short)value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void I64Store8(ManagedMemory memory, long address, long offset, long value) => *Address(memory, address, offset, 1) = (byte)value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void I64Store16(ManagedMemory memory, long address, long offset, long value) => Unsafe.WriteUnaligned(Address(memory, address, offset, 2), (short)value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void I64Store32(ManagedMemory memory, long address, long offset, long value) => Unsafe.WriteUnaligned(Address(memory, address, offset, 4), (int)value);

    // ------------------------------------------------------------------------------------------------------------
    // Numbers (the same operations as the interpreter)
    // ------------------------------------------------------------------------------------------------------------

    public static long I32Eqz(long a) => (int)a == 0 ? 1 : 0;

    public static long I32Eq(long a, long b) => (int)a == (int)b ? 1 : 0;

    public static long I32Ne(long a, long b) => (int)a != (int)b ? 1 : 0;

    public static long I32LtS(long a, long b) => (int)a < (int)b ? 1 : 0;

    public static long I32LtU(long a, long b) => (uint)a < (uint)b ? 1 : 0;

    public static long I32GtS(long a, long b) => (int)a > (int)b ? 1 : 0;

    public static long I32GtU(long a, long b) => (uint)a > (uint)b ? 1 : 0;

    public static long I32LeS(long a, long b) => (int)a <= (int)b ? 1 : 0;

    public static long I32LeU(long a, long b) => (uint)a <= (uint)b ? 1 : 0;

    public static long I32GeS(long a, long b) => (int)a >= (int)b ? 1 : 0;

    public static long I32GeU(long a, long b) => (uint)a >= (uint)b ? 1 : 0;

    public static long I64Eqz(long a) => a == 0 ? 1 : 0;

    public static long I64Eq(long a, long b) => a == b ? 1 : 0;

    public static long I64Ne(long a, long b) => a != b ? 1 : 0;

    public static long I64LtS(long a, long b) => a < b ? 1 : 0;

    public static long I64LtU(long a, long b) => (ulong)a < (ulong)b ? 1 : 0;

    public static long I64GtS(long a, long b) => a > b ? 1 : 0;

    public static long I64GtU(long a, long b) => (ulong)a > (ulong)b ? 1 : 0;

    public static long I64LeS(long a, long b) => a <= b ? 1 : 0;

    public static long I64LeU(long a, long b) => (ulong)a <= (ulong)b ? 1 : 0;

    public static long I64GeS(long a, long b) => a >= b ? 1 : 0;

    public static long I64GeU(long a, long b) => (ulong)a >= (ulong)b ? 1 : 0;

    public static long F32Eq(long a, long b) => Numerics.F32(a) == Numerics.F32(b) ? 1 : 0;

    public static long F32Ne(long a, long b) => Numerics.F32(a) != Numerics.F32(b) ? 1 : 0;

    public static long F32Lt(long a, long b) => Numerics.F32(a) < Numerics.F32(b) ? 1 : 0;

    public static long F32Gt(long a, long b) => Numerics.F32(a) > Numerics.F32(b) ? 1 : 0;

    public static long F32Le(long a, long b) => Numerics.F32(a) <= Numerics.F32(b) ? 1 : 0;

    public static long F32Ge(long a, long b) => Numerics.F32(a) >= Numerics.F32(b) ? 1 : 0;

    public static long F64Eq(long a, long b) => Numerics.F64(a) == Numerics.F64(b) ? 1 : 0;

    public static long F64Ne(long a, long b) => Numerics.F64(a) != Numerics.F64(b) ? 1 : 0;

    public static long F64Lt(long a, long b) => Numerics.F64(a) < Numerics.F64(b) ? 1 : 0;

    public static long F64Gt(long a, long b) => Numerics.F64(a) > Numerics.F64(b) ? 1 : 0;

    public static long F64Le(long a, long b) => Numerics.F64(a) <= Numerics.F64(b) ? 1 : 0;

    public static long F64Ge(long a, long b) => Numerics.F64(a) >= Numerics.F64(b) ? 1 : 0;

    public static long I32Clz(long a) => BitOperations.LeadingZeroCount((uint)a);

    public static long I32Ctz(long a) => (uint)a == 0 ? 32 : BitOperations.TrailingZeroCount((uint)a);

    public static long I32Popcnt(long a) => BitOperations.PopCount((uint)a);

    public static long I32Add(long a, long b) => (int)a + (int)b;

    public static long I32Sub(long a, long b) => (int)a - (int)b;

    public static long I32Mul(long a, long b) => (int)a * (int)b;

    public static long I32DivS(long a, long b) => Numerics.DivS((int)a, (int)b);

    public static long I32DivU(long a, long b) => (int)Numerics.DivU((uint)a, (uint)b);

    public static long I32RemS(long a, long b) => Numerics.RemS((int)a, (int)b);

    public static long I32RemU(long a, long b) => (int)Numerics.RemU((uint)a, (uint)b);

    public static long I32And(long a, long b) => (int)a & (int)b;

    public static long I32Or(long a, long b) => (int)a | (int)b;

    public static long I32Xor(long a, long b) => (int)a ^ (int)b;

    public static long I32Shl(long a, long b) => (int)a << ((int)b & 31);

    public static long I32ShrS(long a, long b) => (int)a >> ((int)b & 31);

    public static long I32ShrU(long a, long b) => (int)((uint)a >> ((int)b & 31));

    public static long I32Rotl(long a, long b) => Numerics.Rotl((int)a, (int)b);

    public static long I32Rotr(long a, long b) => Numerics.Rotr((int)a, (int)b);

    public static long I64Clz(long a) => BitOperations.LeadingZeroCount((ulong)a);

    public static long I64Ctz(long a) => a == 0 ? 64 : BitOperations.TrailingZeroCount(a);

    public static long I64Popcnt(long a) => BitOperations.PopCount((ulong)a);

    public static long I64Add(long a, long b) => a + b;

    public static long I64Sub(long a, long b) => a - b;

    public static long I64Mul(long a, long b) => a * b;

    public static long I64DivS(long a, long b) => Numerics.DivS(a, b);

    public static long I64DivU(long a, long b) => (long)Numerics.DivU((ulong)a, (ulong)b);

    public static long I64RemS(long a, long b) => Numerics.RemS(a, b);

    public static long I64RemU(long a, long b) => (long)Numerics.RemU((ulong)a, (ulong)b);

    public static long I64And(long a, long b) => a & b;

    public static long I64Or(long a, long b) => a | b;

    public static long I64Xor(long a, long b) => a ^ b;

    public static long I64Shl(long a, long b) => a << (int)(b & 63);

    public static long I64ShrS(long a, long b) => a >> (int)(b & 63);

    public static long I64ShrU(long a, long b) => (long)((ulong)a >> (int)(b & 63));

    public static long I64Rotl(long a, long b) => Numerics.Rotl(a, b);

    public static long I64Rotr(long a, long b) => Numerics.Rotr(a, b);

    public static long F32Abs(long a) => (int)((uint)a & 0x7FFF_FFFF);

    public static long F32Neg(long a) => (int)((uint)a ^ 0x8000_0000);

    public static long F32Ceil(long a) => Numerics.Bits(Numerics.Ceil(Numerics.F32(a)));

    public static long F32Floor(long a) => Numerics.Bits(Numerics.Floor(Numerics.F32(a)));

    public static long F32Trunc(long a) => Numerics.Bits(Numerics.Trunc(Numerics.F32(a)));

    public static long F32Nearest(long a) => Numerics.Bits(Numerics.Nearest(Numerics.F32(a)));

    public static long F32Sqrt(long a) => Numerics.Bits(MathF.Sqrt(Numerics.F32(a)));

    public static long F32Add(long a, long b) => Numerics.Bits(Numerics.F32(a) + Numerics.F32(b));

    public static long F32Sub(long a, long b) => Numerics.Bits(Numerics.F32(a) - Numerics.F32(b));

    public static long F32Mul(long a, long b) => Numerics.Bits(Numerics.F32(a) * Numerics.F32(b));

    public static long F32Div(long a, long b) => Numerics.Bits(Numerics.F32(a) / Numerics.F32(b));

    public static long F32Min(long a, long b) => Numerics.Bits(Numerics.Min(Numerics.F32(a), Numerics.F32(b)));

    public static long F32Max(long a, long b) => Numerics.Bits(Numerics.Max(Numerics.F32(a), Numerics.F32(b)));

    public static long F32Copysign(long a, long b) => (int)(((uint)a & 0x7FFF_FFFF) | ((uint)b & 0x8000_0000));

    public static long F64Abs(long a) => a & 0x7FFF_FFFF_FFFF_FFFF;

    public static long F64Neg(long a) => a ^ unchecked((long)0x8000_0000_0000_0000);

    public static long F64Ceil(long a) => Numerics.Bits(Numerics.Ceil(Numerics.F64(a)));

    public static long F64Floor(long a) => Numerics.Bits(Numerics.Floor(Numerics.F64(a)));

    public static long F64Trunc(long a) => Numerics.Bits(Numerics.Trunc(Numerics.F64(a)));

    public static long F64Nearest(long a) => Numerics.Bits(Numerics.Nearest(Numerics.F64(a)));

    public static long F64Sqrt(long a) => Numerics.Bits(Math.Sqrt(Numerics.F64(a)));

    public static long F64Add(long a, long b) => Numerics.Bits(Numerics.F64(a) + Numerics.F64(b));

    public static long F64Sub(long a, long b) => Numerics.Bits(Numerics.F64(a) - Numerics.F64(b));

    public static long F64Mul(long a, long b) => Numerics.Bits(Numerics.F64(a) * Numerics.F64(b));

    public static long F64Div(long a, long b) => Numerics.Bits(Numerics.F64(a) / Numerics.F64(b));

    public static long F64Min(long a, long b) => Numerics.Bits(Numerics.Min(Numerics.F64(a), Numerics.F64(b)));

    public static long F64Max(long a, long b) => Numerics.Bits(Numerics.Max(Numerics.F64(a), Numerics.F64(b)));

    public static long F64Copysign(long a, long b) => (a & 0x7FFF_FFFF_FFFF_FFFF) | (b & unchecked((long)0x8000_0000_0000_0000));

    public static long I32WrapI64(long a) => (int)a;

    public static long I32TruncF32S(long a) => Numerics.TruncI32(Numerics.F32(a));

    public static long I32TruncF32U(long a) => (int)Numerics.TruncU32(Numerics.F32(a));

    public static long I32TruncF64S(long a) => Numerics.TruncI32(Numerics.F64(a));

    public static long I32TruncF64U(long a) => (int)Numerics.TruncU32(Numerics.F64(a));

    public static long I64ExtendI32S(long a) => (int)a;

    public static long I64ExtendI32U(long a) => (uint)a;

    public static long I64TruncF32S(long a) => Numerics.TruncI64(Numerics.F32(a));

    public static long I64TruncF32U(long a) => (long)Numerics.TruncU64(Numerics.F32(a));

    public static long I64TruncF64S(long a) => Numerics.TruncI64(Numerics.F64(a));

    public static long I64TruncF64U(long a) => (long)Numerics.TruncU64(Numerics.F64(a));

    public static long F32ConvertI32S(long a) => Numerics.Bits((float)(int)a);

    public static long F32ConvertI32U(long a) => Numerics.Bits((float)(uint)a);

    public static long F32ConvertI64S(long a) => Numerics.Bits(Numerics.ConvertI64ToF32(a));

    public static long F32ConvertI64U(long a) => Numerics.Bits(Numerics.ConvertU64ToF32((ulong)a));

    public static long F32DemoteF64(long a) => Numerics.Bits(Numerics.Demote(Numerics.F64(a)));

    public static long F64ConvertI32S(long a) => Numerics.Bits((double)(int)a);

    public static long F64ConvertI32U(long a) => Numerics.Bits((double)(uint)a);

    public static long F64ConvertI64S(long a) => Numerics.Bits((double)a);

    public static long F64ConvertI64U(long a) => Numerics.Bits(Numerics.ConvertU64ToF64((ulong)a));

    public static long F64PromoteF32(long a) => Numerics.Bits(Numerics.Promote(Numerics.F32(a)));

    public static long I32ReinterpretF32(long a) => (int)a;

    public static long F32ReinterpretI32(long a) => (int)a;

    public static long I64ReinterpretF64(long a) => a;

    public static long F64ReinterpretI64(long a) => a;

    public static long I32Extend8S(long a) => (sbyte)a;

    public static long I32Extend16S(long a) => (short)a;

    public static long I64Extend8S(long a) => (sbyte)a;

    public static long I64Extend16S(long a) => (short)a;

    public static long I64Extend32S(long a) => (int)a;

    public static long I32TruncSatF32S(long a) => Numerics.TruncSatI32(Numerics.F32(a));

    public static long I32TruncSatF32U(long a) => (int)Numerics.TruncSatU32(Numerics.F32(a));

    public static long I32TruncSatF64S(long a) => Numerics.TruncSatI32(Numerics.F64(a));

    public static long I32TruncSatF64U(long a) => (int)Numerics.TruncSatU32(Numerics.F64(a));

    public static long I64TruncSatF32S(long a) => Numerics.TruncSatI64(Numerics.F32(a));

    public static long I64TruncSatF32U(long a) => (long)Numerics.TruncSatU64(Numerics.F32(a));

    public static long I64TruncSatF64S(long a) => Numerics.TruncSatI64(Numerics.F64(a));

    public static long I64TruncSatF64U(long a) => (long)Numerics.TruncSatU64(Numerics.F64(a));
}
