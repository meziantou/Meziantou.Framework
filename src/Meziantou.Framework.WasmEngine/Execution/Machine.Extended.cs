using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.WasmEngine;

internal sealed unsafe partial class Machine
{
    private const int MaxArrayLength = 1 << 28;

    private static readonly byte[] SimdShapes = CreateSimdShapes();

    private static byte[] CreateSimdShapes()
    {
        var shapes = new byte[0x114];
        for (var i = 0; i < shapes.Length; i++)
        {
            shapes[i] = (byte)FunctionCompiler.GetSimdShapeCode((uint)i);
        }

        return shapes;
    }

#pragma warning disable MA0051 // Method is too long
    /// <summary>Runs the instructions that are not in the main loop: GC objects, bulk memory, tables, vectors and atomics.</summary>
    private void ExecuteExtended(in Instr ins, ManagedInstance instance, Slot[] stack, ref int sp)
    {
        switch (ins.Op)
        {
            // GC objects
            case Op.StructNew:
            {
                var type = (DefinedType)ins.O!;
                var count = type.Fields.Length;
                var fields = new Slot[count];
                Array.Copy(stack, sp - count, fields, 0, count);
                sp -= count;
                stack[sp++] = new Slot { R = new GcStruct(type, fields) };
                break;
            }

            case Op.StructNewDefault:
            {
                var type = (DefinedType)ins.O!;
                stack[sp++] = new Slot { R = new GcStruct(type, new Slot[type.Fields.Length]) };
                break;
            }

            case Op.StructGet:
            case Op.StructGetS:
            case Op.StructGetU:
            {
                ref var slot = ref stack[sp - 1];
                var structure = (GcStruct?)slot.R ?? throw Traps.NullStructureReference();
                var value = structure.Fields[ins.I0];
                if (ins.Op != Op.StructGet)
                {
                    var code = structure.DefinedType.Fields[ins.I0].Type.Code;
                    value.A = (ins.Op == Op.StructGetS, code) switch
                    {
                        (true, ValueCode.I8) => (sbyte)value.A,
                        (true, _) => (short)value.A,
                        (false, ValueCode.I8) => (byte)value.A,
                        _ => (ushort)value.A,
                    };
                }

                slot = value;
                break;
            }

            case Op.StructSet:
            {
                sp -= 2;
                var structure = (GcStruct?)stack[sp].R ?? throw Traps.NullStructureReference();
                structure.Fields[ins.I0] = stack[sp + 1];
                break;
            }

            case Op.ArrayNew:
            {
                sp--;
                var length = CheckArrayLength((uint)stack[sp].A);
                var array = new GcArray((DefinedType)ins.O!, length);
                array.Fill(0, length, stack[sp - 1]);
                stack[sp - 1] = new Slot { R = array };
                break;
            }

            case Op.ArrayNewDefault:
            {
                ref var slot = ref stack[sp - 1];
                slot = new Slot { R = new GcArray((DefinedType)ins.O!, CheckArrayLength((uint)slot.A)) };
                break;
            }

            case Op.ArrayNewFixed:
            {
                var count = ins.I0;
                var array = new GcArray((DefinedType)ins.O!, count);
                for (var i = 0; i < count; i++)
                {
                    array.Set(i, stack[sp - count + i]);
                }

                sp -= count;
                stack[sp++] = new Slot { R = array };
                break;
            }

            case Op.ArrayNewData:
            {
                sp--;
                var count = (uint)stack[sp].A;
                var offset = (uint)stack[sp - 1].A;
                var type = (DefinedType)ins.O!;
                var data = instance.Data[ins.I0];
                var size = (ulong)type.Element.Type.StorageSize;
                if (offset + (count * size) > (ulong)data.Length)
                    throw Traps.OutOfBoundsMemory();

                var array = new GcArray(type, CheckArrayLength(count));
                array.InitializeFromBytes(0, data.AsSpan((int)offset), (int)count);
                stack[sp - 1] = new Slot { R = array };
                break;
            }

            case Op.ArrayNewElem:
            {
                sp--;
                var count = (uint)stack[sp].A;
                var offset = (uint)stack[sp - 1].A;
                var elements = instance.Elements[ins.I0];
                if ((ulong)offset + count > (ulong)elements.Length)
                    throw Traps.OutOfBoundsTable();

                var array = new GcArray((DefinedType)ins.O!, CheckArrayLength(count));
                for (var i = 0; i < count; i++)
                {
                    array.Set(i, new Slot { R = elements[offset + i] });
                }

                stack[sp - 1] = new Slot { R = array };
                break;
            }

            case Op.ArrayGet:
            case Op.ArrayGetS:
            case Op.ArrayGetU:
            {
                sp--;
                var index = (uint)stack[sp].A;
                var array = (GcArray?)stack[sp - 1].R ?? throw Traps.NullArrayReference();
                if (index >= (uint)array.Length)
                    throw Traps.OutOfBoundsArray();

                stack[sp - 1] = array.Get((int)index, ins.Op == Op.ArrayGetS);
                break;
            }

            case Op.ArraySet:
            {
                sp -= 3;
                var array = (GcArray?)stack[sp].R ?? throw Traps.NullArrayReference();
                var index = (uint)stack[sp + 1].A;
                if (index >= (uint)array.Length)
                    throw Traps.OutOfBoundsArray();

                array.Set((int)index, stack[sp + 2]);
                break;
            }

            case Op.ArrayLen:
            {
                ref var slot = ref stack[sp - 1];
                var array = (GcArray?)slot.R ?? throw Traps.NullArrayReference();
                slot = new Slot { A = array.Length };
                break;
            }

            case Op.ArrayFill:
            {
                sp -= 4;
                var array = (GcArray?)stack[sp].R ?? throw Traps.NullArrayReference();
                var offset = (uint)stack[sp + 1].A;
                var count = (uint)stack[sp + 3].A;
                if ((ulong)offset + count > (ulong)array.Length)
                    throw Traps.OutOfBoundsArray();

                array.Fill((int)offset, (int)count, stack[sp + 2]);
                break;
            }

            case Op.ArrayCopy:
            {
                sp -= 5;
                var destination = (GcArray?)stack[sp].R ?? throw Traps.NullArrayReference();
                var destinationOffset = (uint)stack[sp + 1].A;
                var source = (GcArray?)stack[sp + 2].R ?? throw Traps.NullArrayReference();
                var sourceOffset = (uint)stack[sp + 3].A;
                var count = (uint)stack[sp + 4].A;
                if ((ulong)destinationOffset + count > (ulong)destination.Length || (ulong)sourceOffset + count > (ulong)source.Length)
                    throw Traps.OutOfBoundsArray();

                destination.CopyFrom(source, (int)sourceOffset, (int)destinationOffset, (int)count);
                break;
            }

            case Op.ArrayInitData:
            {
                sp -= 4;
                var array = (GcArray?)stack[sp].R ?? throw Traps.NullArrayReference();
                var destinationOffset = (uint)stack[sp + 1].A;
                var sourceOffset = (uint)stack[sp + 2].A;
                var count = (uint)stack[sp + 3].A;
                if ((ulong)destinationOffset + count > (ulong)array.Length)
                    throw Traps.OutOfBoundsArray();

                var data = instance.Data[ins.I0];
                var size = (ulong)array.DefinedType.Element.Type.StorageSize;
                if (sourceOffset + (count * size) > (ulong)data.Length)
                    throw Traps.OutOfBoundsMemory();

                array.InitializeFromBytes((int)destinationOffset, data.AsSpan((int)sourceOffset), (int)count);
                break;
            }

            case Op.ArrayInitElem:
            {
                sp -= 4;
                var array = (GcArray?)stack[sp].R ?? throw Traps.NullArrayReference();
                var destinationOffset = (uint)stack[sp + 1].A;
                var sourceOffset = (uint)stack[sp + 2].A;
                var count = (uint)stack[sp + 3].A;
                if ((ulong)destinationOffset + count > (ulong)array.Length)
                    throw Traps.OutOfBoundsArray();

                var elements = instance.Elements[ins.I0];
                if ((ulong)sourceOffset + count > (ulong)elements.Length)
                    throw Traps.OutOfBoundsTable();

                for (var i = 0; i < count; i++)
                {
                    array.Set((int)(destinationOffset + i), new Slot { R = elements[sourceOffset + i] });
                }

                break;
            }

            // Bulk memory and tables
            case Op.MemoryInit:
            {
                sp -= 3;
                var memory = instance.Memories[ins.I0];
                var destination = Address(memory, stack[sp].A);
                var source = (uint)stack[sp + 1].A;
                var count = (uint)stack[sp + 2].A;
                var data = instance.Data[ins.I1];
                if ((ulong)source + count > (ulong)data.Length)
                    throw Traps.OutOfBoundsMemory();

                var pointer = memory.GetRange(destination, count);
                data.AsSpan((int)source, (int)count).CopyTo(new Span<byte>(pointer, (int)count));
                break;
            }

            case Op.DataDrop:
                instance.Data[ins.I0] = [];
                break;

            case Op.MemoryCopy:
            {
                sp -= 3;
                var destinationMemory = instance.Memories[ins.I0];
                var sourceMemory = instance.Memories[ins.I1];
                var destination = Address(destinationMemory, stack[sp].A);
                var source = Address(sourceMemory, stack[sp + 1].A);
                var count = destinationMemory.Is64 && sourceMemory.Is64 ? (ulong)stack[sp + 2].A : (uint)stack[sp + 2].A;
                var sourcePointer = sourceMemory.GetRange(source, count);
                var destinationPointer = destinationMemory.GetRange(destination, count);
                Buffer.MemoryCopy(sourcePointer, destinationPointer, (long)count, (long)count);
                break;
            }

            case Op.MemoryFill:
            {
                sp -= 3;
                var memory = instance.Memories[ins.I0];
                var destination = Address(memory, stack[sp].A);
                var value = (byte)stack[sp + 1].A;
                var count = Address(memory, stack[sp + 2].A);
                var pointer = memory.GetRange(destination, count);
                new Span<byte>(pointer, (int)count).Fill(value);
                break;
            }

            case Op.TableInit:
            {
                sp -= 3;
                var table = instance.Tables[ins.I0];
                var destination = Address(table, stack[sp].A);
                var source = (uint)stack[sp + 1].A;
                var count = (uint)stack[sp + 2].A;
                var elements = instance.Elements[ins.I1];
                if ((ulong)source + count > (ulong)elements.Length || destination > table.Size || count > table.Size - destination)
                    throw Traps.OutOfBoundsTable();

                Array.Copy(elements, source, table.Elements, (long)destination, count);
                break;
            }

            case Op.ElemDrop:
                instance.Elements[ins.I0] = [];
                break;

            case Op.TableCopy:
            {
                sp -= 3;
                var destinationTable = instance.Tables[ins.I0];
                var sourceTable = instance.Tables[ins.I1];
                var destination = Address(destinationTable, stack[sp].A);
                var source = Address(sourceTable, stack[sp + 1].A);
                var count = destinationTable.Info.Is64 && sourceTable.Info.Is64 ? (ulong)stack[sp + 2].A : (uint)stack[sp + 2].A;
                if (source > sourceTable.Size || count > sourceTable.Size - source || destination > destinationTable.Size || count > destinationTable.Size - destination)
                    throw Traps.OutOfBoundsTable();

                Array.Copy(sourceTable.Elements, (long)source, destinationTable.Elements, (long)destination, (long)count);
                break;
            }

            case Op.TableGrow:
            {
                sp--;
                var table = instance.Tables[ins.I0];
                var delta = Address(table, stack[sp].A);
                var result = table.TryGrow(delta, stack[sp - 1].R);
                stack[sp - 1] = new Slot { A = table.Info.Is64 ? result : (int)result };
                break;
            }

            case Op.TableSize:
                stack[sp++] = new Slot { A = (long)instance.Tables[ins.I0].Size };
                break;

            case Op.TableFill:
            {
                sp -= 3;
                var table = instance.Tables[ins.I0];
                var destination = Address(table, stack[sp].A);
                var value = stack[sp + 1].R;
                var count = Address(table, stack[sp + 2].A);
                if (destination > table.Size || count > table.Size - destination)
                    throw Traps.OutOfBoundsTable();

                Array.Fill(table.Elements, value, (int)destination, (int)count);
                break;
            }

            // Atomics
            case Op.MemoryAtomicNotify:
            {
                sp--;
                var memory = instance.Memories[ins.I0];
                ref var slot = ref stack[sp - 1];
                var pointer = memory.GetAtomicAddress(Address(memory, slot.A), (ulong)ins.L0, 4);
                slot.A = memory.Info.Shared ? AtomicWaiters.Notify((nint)pointer, (uint)stack[sp].A) : 0;
                break;
            }

            case Op.MemoryAtomicWait32:
            case Op.MemoryAtomicWait64:
            {
                sp -= 2;
                var memory = instance.Memories[ins.I0];
                ref var slot = ref stack[sp - 1];
                var size = ins.Op == Op.MemoryAtomicWait32 ? 4 : 8;
                var pointer = memory.GetAtomicAddress(Address(memory, slot.A), (ulong)ins.L0, size);
                if (!memory.Info.Shared)
                    throw Traps.ExpectedSharedMemory();

                var timeout = stack[sp + 1].A;
                slot.A = (int)AtomicWaiters.Wait(pointer, stack[sp].A, size, timeout < 0 ? null : TimeSpan.FromTicks(timeout / 100), signal: null);
                break;
            }

            case Op.AtomicFence:
                Interlocked.MemoryBarrier();
                break;

            case >= Op.I32AtomicLoad and <= Op.I64AtomicLoad32U:
            {
                var memory = instance.Memories[ins.I0];
                ref var slot = ref stack[sp - 1];
                var (size, _) = GetAtomicAccess(ins.Op - Op.I32AtomicLoad);
                var pointer = memory.GetAtomicAddress(Address(memory, slot.A), (ulong)ins.L0, size);
                slot.A = size switch
                {
                    1 => Volatile.Read(ref *pointer),
                    2 => Volatile.Read(ref *(ushort*)pointer),
                    4 => ins.Op == Op.I32AtomicLoad ? Volatile.Read(ref *(int*)pointer) : Volatile.Read(ref *(uint*)pointer),
                    _ => Volatile.Read(ref *(long*)pointer),
                };
                break;
            }

            case >= Op.I32AtomicStore and <= Op.I64AtomicStore32:
            {
                sp -= 2;
                var memory = instance.Memories[ins.I0];
                var (size, _) = GetAtomicAccess(ins.Op - Op.I32AtomicStore);
                var pointer = memory.GetAtomicAddress(Address(memory, stack[sp].A), (ulong)ins.L0, size);
                var value = stack[sp + 1].A;
                switch (size)
                {
                    case 1:
                        Volatile.Write(ref *pointer, (byte)value);
                        break;
                    case 2:
                        Volatile.Write(ref *(ushort*)pointer, (ushort)value);
                        break;
                    case 4:
                        Volatile.Write(ref *(int*)pointer, (int)value);
                        break;
                    default:
                        Volatile.Write(ref *(long*)pointer, value);
                        break;
                }

                break;
            }

            case >= Op.AtomicRmwFirst and <= Op.AtomicRmwLast:
            {
                var index = ins.Op - Op.AtomicRmwFirst;
                var operation = index / 7;
                var (size, is64) = GetAtomicAccess(index % 7);
                var operandCount = operation == 6 ? 3 : 2;
                sp -= operandCount - 1;
                var memory = instance.Memories[ins.I0];
                ref var slot = ref stack[sp - 1];
                var pointer = memory.GetAtomicAddress(Address(memory, slot.A), (ulong)ins.L0, size);
                var old = AtomicReadModifyWrite(pointer, size, operation, stack[sp].A, operandCount == 3 ? stack[sp + 1].A : 0);
                slot.A = is64 || size < 4 ? old : (int)old;
                break;
            }

            default:
                if (Op.IsSimd(ins.Op))
                {
                    ExecuteSimd(in ins, instance, stack, ref sp);
                    break;
                }

                throw new InvalidOperationException("Unknown instruction " + OpNames.GetName(ins.Op));
        }
    }
#pragma warning restore MA0051

    private static int CheckArrayLength(uint length)
    {
        if (length > MaxArrayLength)
            throw Traps.ArrayTooLarge();

        return (int)length;
    }

    /// <summary>The size in bytes and whether the value is an i64, of an atomic access: i32, i64, i32 8, i32 16, i64 8, i64 16, i64 32.</summary>
    private static (int Size, bool Is64) GetAtomicAccess(int index) => index switch
    {
        0 => (4, false),
        1 => (8, true),
        2 => (1, false),
        3 => (2, false),
        4 => (1, true),
        5 => (2, true),
        _ => (4, true),
    };

    /// <summary>An atomic read-modify-write (add, sub, and, or, xor, xchg, cmpxchg): returns the previous value, zero-extended.</summary>
    private static long AtomicReadModifyWrite(byte* pointer, int size, int operation, long operand, long replacement)
    {
        static long Apply(int operation, long old, long operand, long replacement, long mask)
        {
            return operation switch
            {
                0 => old + operand,
                1 => old - operand,
                2 => old & operand,
                3 => old | operand,
                4 => old ^ operand,
                5 => operand,
                _ => old == (operand & mask) ? replacement : old,
            };
        }

        switch (size)
        {
            case 1:
            {
                ref var target = ref *pointer;
                while (true)
                {
                    var old = Volatile.Read(ref target);
                    var value = (byte)Apply(operation, old, operand, replacement, 0xFF);
                    if (Interlocked.CompareExchange(ref target, value, old) == old)
                        return old;
                }
            }

            case 2:
            {
                ref var target = ref *(ushort*)pointer;
                while (true)
                {
                    var old = Volatile.Read(ref target);
                    var value = (ushort)Apply(operation, old, operand, replacement, 0xFFFF);
                    if (Interlocked.CompareExchange(ref target, value, old) == old)
                        return old;
                }
            }

            case 4:
            {
                ref var target = ref *(uint*)pointer;
                while (true)
                {
                    var old = Volatile.Read(ref target);
                    var value = (uint)Apply(operation, old, operand, replacement, 0xFFFF_FFFF);
                    if (Interlocked.CompareExchange(ref target, value, old) == old)
                        return old;
                }
            }

            default:
            {
                ref var target = ref *(long*)pointer;
                while (true)
                {
                    var old = Volatile.Read(ref target);
                    var value = Apply(operation, old, operand, replacement, -1);
                    if (Interlocked.CompareExchange(ref target, value, old) == old)
                        return old;
                }
            }
        }
    }

    private static void ExecuteSimd(in Instr ins, ManagedInstance instance, Slot[] stack, ref int sp)
    {
        var opcode = ins.Op - Op.SimdPrefix;
        switch (opcode)
        {
            case <= 0x0A or 0x5C or 0x5D: // loads
            {
                var memory = instance.Memories[ins.I0];
                ref var slot = ref stack[sp - 1];
                var size = opcode switch { 0x00 => 16, <= 0x06 => 8, 0x07 => 1, 0x08 => 2, 0x09 or 0x5C => 4, _ => 8 };
                var pointer = memory.GetAddress(Address(memory, slot.A), (ulong)ins.L0, size);
                Vector128<byte> value;
                switch (opcode)
                {
                    case 0x00:
                        value = Unsafe.ReadUnaligned<Vector128<byte>>(pointer);
                        break;
                    case 0x01:
                        value = Vector128.WidenLower(Vector128.CreateScalar(Unsafe.ReadUnaligned<long>(pointer)).AsSByte()).AsByte();
                        break;
                    case 0x02:
                        value = Vector128.WidenLower(Vector128.CreateScalar(Unsafe.ReadUnaligned<long>(pointer)).AsByte()).AsByte();
                        break;
                    case 0x03:
                        value = Vector128.WidenLower(Vector128.CreateScalar(Unsafe.ReadUnaligned<long>(pointer)).AsInt16()).AsByte();
                        break;
                    case 0x04:
                        value = Vector128.WidenLower(Vector128.CreateScalar(Unsafe.ReadUnaligned<long>(pointer)).AsUInt16()).AsByte();
                        break;
                    case 0x05:
                        value = Vector128.WidenLower(Vector128.CreateScalar(Unsafe.ReadUnaligned<long>(pointer)).AsInt32()).AsByte();
                        break;
                    case 0x06:
                        value = Vector128.WidenLower(Vector128.CreateScalar(Unsafe.ReadUnaligned<long>(pointer)).AsUInt32()).AsByte();
                        break;
                    case 0x07:
                        value = Vector128.Create(*pointer);
                        break;
                    case 0x08:
                        value = Vector128.Create(Unsafe.ReadUnaligned<short>(pointer)).AsByte();
                        break;
                    case 0x09:
                        value = Vector128.Create(Unsafe.ReadUnaligned<int>(pointer)).AsByte();
                        break;
                    case 0x0A:
                        value = Vector128.Create(Unsafe.ReadUnaligned<long>(pointer)).AsByte();
                        break;
                    case 0x5C:
                        value = Vector128.CreateScalar(Unsafe.ReadUnaligned<int>(pointer)).AsByte();
                        break;
                    default:
                        value = Vector128.CreateScalar(Unsafe.ReadUnaligned<long>(pointer)).AsByte();
                        break;
                }

                Simd.Store(value, ref slot);
                return;
            }

            case 0x0B: // v128.store
            {
                sp -= 2;
                var memory = instance.Memories[ins.I0];
                var pointer = memory.GetAddress(Address(memory, stack[sp].A), (ulong)ins.L0, 16);
                Unsafe.WriteUnaligned(pointer, Simd.ToVector(stack[sp + 1].A, stack[sp + 1].B));
                return;
            }

            case 0x0C: // v128.const
                stack[sp++] = new Slot { A = ins.L0, B = ins.L1 };
                return;

            case 0x0D: // i8x16.shuffle
            {
                sp--;
                ref var slot = ref stack[sp - 1];
                Simd.Store(Simd.Shuffle(Simd.ToVector(slot.A, slot.B), Simd.ToVector(stack[sp].A, stack[sp].B), ins.L0, ins.L1), ref slot);
                return;
            }

            case >= 0x0F and <= 0x14: // splat
            {
                ref var slot = ref stack[sp - 1];
                Simd.Store(Simd.Splat(opcode, slot.A), ref slot);
                return;
            }

            case 0x15 or 0x16 or 0x18 or 0x19 or 0x1B or 0x1D or 0x1F or 0x21: // extract_lane
            {
                ref var slot = ref stack[sp - 1];
                var value = Simd.ExtractLane(opcode, Simd.ToVector(slot.A, slot.B), ins.I1);
                slot = new Slot { A = value };
                return;
            }

            case 0x17 or 0x1A or 0x1C or 0x1E or 0x20 or 0x22: // replace_lane
            {
                sp--;
                ref var slot = ref stack[sp - 1];
                Simd.Store(Simd.ReplaceLane(opcode, Simd.ToVector(slot.A, slot.B), ins.I1, stack[sp].A), ref slot);
                return;
            }

            case >= 0x54 and <= 0x57: // load_lane
            {
                sp--;
                var memory = instance.Memories[ins.I0];
                var size = 1 << (opcode - 0x54);
                var pointer = memory.GetAddress(Address(memory, stack[sp - 1].A), (ulong)ins.L0, size);
                var vector = Simd.ToVector(stack[sp].A, stack[sp].B);
                vector = size switch
                {
                    1 => vector.WithElement(ins.I1, *pointer),
                    2 => vector.AsInt16().WithElement(ins.I1, Unsafe.ReadUnaligned<short>(pointer)).AsByte(),
                    4 => vector.AsInt32().WithElement(ins.I1, Unsafe.ReadUnaligned<int>(pointer)).AsByte(),
                    _ => vector.AsInt64().WithElement(ins.I1, Unsafe.ReadUnaligned<long>(pointer)).AsByte(),
                };
                Simd.Store(vector, ref stack[sp - 1]);
                return;
            }

            case >= 0x58 and <= 0x5B: // store_lane
            {
                sp -= 2;
                var memory = instance.Memories[ins.I0];
                var size = 1 << (opcode - 0x58);
                var pointer = memory.GetAddress(Address(memory, stack[sp].A), (ulong)ins.L0, size);
                var vector = Simd.ToVector(stack[sp + 1].A, stack[sp + 1].B);
                switch (size)
                {
                    case 1:
                        *pointer = vector.GetElement(ins.I1);
                        break;
                    case 2:
                        Unsafe.WriteUnaligned(pointer, vector.AsInt16().GetElement(ins.I1));
                        break;
                    case 4:
                        Unsafe.WriteUnaligned(pointer, vector.AsInt32().GetElement(ins.I1));
                        break;
                    default:
                        Unsafe.WriteUnaligned(pointer, vector.AsInt64().GetElement(ins.I1));
                        break;
                }

                return;
            }
        }

        switch (SimdShapes[opcode])
        {
            case 1: // unary
            {
                ref var slot = ref stack[sp - 1];
                Simd.Store(Simd.Unary(opcode, Simd.ToVector(slot.A, slot.B)), ref slot);
                break;
            }

            case 2: // binary
            {
                sp--;
                ref var slot = ref stack[sp - 1];
                Simd.Store(Simd.Binary(opcode, Simd.ToVector(slot.A, slot.B), Simd.ToVector(stack[sp].A, stack[sp].B)), ref slot);
                break;
            }

            case 3: // ternary
            {
                sp -= 2;
                ref var slot = ref stack[sp - 1];
                Simd.Store(Simd.Ternary(opcode, Simd.ToVector(slot.A, slot.B), Simd.ToVector(stack[sp].A, stack[sp].B), Simd.ToVector(stack[sp + 1].A, stack[sp + 1].B)), ref slot);
                break;
            }

            case 4: // test
            {
                ref var slot = ref stack[sp - 1];
                slot = new Slot { A = Simd.Test(opcode, Simd.ToVector(slot.A, slot.B)) };
                break;
            }

            default: // shift
            {
                sp--;
                ref var slot = ref stack[sp - 1];
                Simd.Store(Simd.Shift(opcode, Simd.ToVector(slot.A, slot.B), (int)stack[sp].A), ref slot);
                break;
            }
        }
    }
}
