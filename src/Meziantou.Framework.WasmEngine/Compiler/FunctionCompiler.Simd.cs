namespace Meziantou.Framework.WasmEngine;

internal sealed partial class FunctionCompiler
{
    private static readonly ValType[] V128x1 = [ValType.V128];
    private static readonly ValType[] V128x2 = [ValType.V128, ValType.V128];
    private static readonly ValType[] V128x3 = [ValType.V128, ValType.V128, ValType.V128];
    private static readonly ValType[] V128I32 = [ValType.V128, ValType.I32];

    private enum SimdShape : byte
    {
        Invalid,
        Unary,
        Binary,
        Ternary,
        Test,
        Shift,
    }

    /// <summary>The shape of a vector instruction that has no immediate: 0 (invalid), 1 (unary), 2 (binary), 3 (ternary), 4 (test), 5 (shift).</summary>
    internal static int GetSimdShapeCode(uint opcode) => (int)GetSimdShape(opcode);

    private static SimdShape GetSimdShape(uint opcode) => opcode switch
    {
        0x0E or >= 0x23 and <= 0x4C or >= 0x4E and <= 0x51 => SimdShape.Binary,
        0x4D => SimdShape.Unary,
        0x52 => SimdShape.Ternary,
        0x53 or 0x63 or 0x83 or 0xA3 or 0xC3 or 0x64 or 0x84 or 0xA4 or 0xC4 => SimdShape.Test,
        0x5E or 0x5F or 0x60 or 0x61 or 0x62 or 0x67 or 0x68 or 0x69 or 0x6A or 0x74 or 0x75 or 0x7A or 0x94 => SimdShape.Unary,
        0x65 or 0x66 or 0x85 or 0x86 => SimdShape.Binary,
        0x6B or 0x6C or 0x6D or 0x8B or 0x8C or 0x8D or 0xAB or 0xAC or 0xAD or 0xCB or 0xCC or 0xCD => SimdShape.Shift,
        >= 0x6E and <= 0x73 or >= 0x76 and <= 0x79 or 0x7B => SimdShape.Binary,
        >= 0x7C and <= 0x81 => SimdShape.Unary,
        0x82 => SimdShape.Binary,
        >= 0x87 and <= 0x8A => SimdShape.Unary,
        >= 0x8E and <= 0x93 or >= 0x95 and <= 0x99 or >= 0x9B and <= 0x9F => SimdShape.Binary,
        0xA0 or 0xA1 or >= 0xA7 and <= 0xAA => SimdShape.Unary,
        0xAE or 0xB1 or >= 0xB5 and <= 0xBA or >= 0xBC and <= 0xBF => SimdShape.Binary,
        0xC0 or 0xC1 or >= 0xC7 and <= 0xCA => SimdShape.Unary,
        0xCE or 0xD1 or >= 0xD5 and <= 0xDF => SimdShape.Binary,
        0xE0 or 0xE1 or 0xE3 or 0xEC or 0xED or 0xEF => SimdShape.Unary,
        >= 0xE4 and <= 0xEB or >= 0xF0 and <= 0xF7 => SimdShape.Binary,
        >= 0xF8 and <= 0xFF => SimdShape.Unary,
        0x100 => SimdShape.Binary,
        >= 0x101 and <= 0x104 => SimdShape.Unary,
        >= 0x105 and <= 0x10C => SimdShape.Ternary,
        >= 0x10D and <= 0x112 => SimdShape.Binary,
        0x113 => SimdShape.Ternary,
        _ => SimdShape.Invalid,
    };

    private void CompileSimd(ref WasmReader reader)
    {
        var opcode = reader.ReadU32();
        var op = (ushort)(Op.SimdPrefix + opcode);
        switch (opcode)
        {
            case <= 0x0A or 0x5C or 0x5D: // loads
            {
                var alignment = opcode switch
                {
                    0x00 => 4,
                    <= 0x06 => 3,
                    0x07 => 0,
                    0x08 => 1,
                    0x09 or 0x5C => 2,
                    _ => 3,
                };
                var (memory, info, offset) = ReadMemArg(ref reader, alignment);
                Pop(info.AddressType);
                Push(ValType.V128);
                Emit(op, i0: memory, l0: (long)offset);
                return;
            }

            case 0x0B: // v128.store
            {
                var (memory, info, offset) = ReadMemArg(ref reader, 4);
                Pop(ValType.V128);
                Pop(info.AddressType);
                Emit(op, i0: memory, l0: (long)offset);
                return;
            }

            case 0x0C: // v128.const
            {
                var bytes = reader.ReadBytes(16);
                var low = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(bytes);
                var high = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(bytes[8..]);
                Push(ValType.V128);
                Emit(op, l0: low, l1: high);
                return;
            }

            case 0x0D: // i8x16.shuffle
            {
                var bytes = reader.ReadBytes(16);
                foreach (var lane in bytes)
                {
                    if (lane >= 32)
                        throw Error("invalid lane index");
                }

                Pop(V128x2);
                Push(ValType.V128);
                Emit(op, l0: System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(bytes), l1: System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(bytes[8..]));
                return;
            }

            case >= 0x0F and <= 0x14: // splat
                Pop(opcode switch { <= 0x11 => ValType.I32, 0x12 => ValType.I64, 0x13 => ValType.F32, _ => ValType.F64 });
                Push(ValType.V128);
                Emit(op);
                return;

            case >= 0x15 and <= 0x22: // extract_lane, replace_lane
            {
                var lane = reader.ReadByte();
                var (laneCount, scalar, replace) = opcode switch
                {
                    0x15 or 0x16 => (16, ValType.I32, false),
                    0x17 => (16, ValType.I32, true),
                    0x18 or 0x19 => (8, ValType.I32, false),
                    0x1A => (8, ValType.I32, true),
                    0x1B => (4, ValType.I32, false),
                    0x1C => (4, ValType.I32, true),
                    0x1D => (2, ValType.I64, false),
                    0x1E => (2, ValType.I64, true),
                    0x1F => (4, ValType.F32, false),
                    0x20 => (4, ValType.F32, true),
                    0x21 => (2, ValType.F64, false),
                    _ => (2, ValType.F64, true),
                };
                if (lane >= laneCount)
                    throw Error("invalid lane index");

                if (replace)
                {
                    Pop(scalar);
                    Pop(ValType.V128);
                    Push(ValType.V128);
                }
                else
                {
                    Pop(ValType.V128);
                    Push(scalar);
                }

                Emit(op, i1: lane);
                return;
            }

            case >= 0x54 and <= 0x5B: // load_lane, store_lane
            {
                var size = (int)((opcode - 0x54) % 4);
                var (memory, info, offset) = ReadMemArg(ref reader, size);
                var lane = reader.ReadByte();
                if (lane >= 16 >> size)
                    throw Error("invalid lane index");

                Pop(ValType.V128);
                Pop(info.AddressType);
                if (opcode <= 0x57)
                {
                    Push(ValType.V128);
                }

                Emit(op, i0: memory, i1: lane, l0: (long)offset);
                return;
            }
        }

        switch (GetSimdShape(opcode))
        {
            case SimdShape.Unary:
                Pop(V128x1);
                Push(ValType.V128);
                break;
            case SimdShape.Binary:
                Pop(V128x2);
                Push(ValType.V128);
                break;
            case SimdShape.Ternary:
                Pop(V128x3);
                Push(ValType.V128);
                break;
            case SimdShape.Test:
                Pop(V128x1);
                Push(ValType.I32);
                break;
            case SimdShape.Shift:
                Pop(V128I32);
                Push(ValType.V128);
                break;
            default:
                throw Error("illegal opcode fd " + opcode);
        }

        Emit(op);
    }

    private void CompileAtomic(ref WasmReader reader)
    {
        var opcode = reader.ReadU32();
        var op = (ushort)(Op.AtomicPrefix + opcode);
        switch (opcode)
        {
            case 0x00: // memory.atomic.notify
            {
                var (memory, info, offset) = ReadMemArg(ref reader, 2, exact: true);
                Pop(ValType.I32);
                Pop(info.AddressType);
                Push(ValType.I32);
                Emit(op, i0: memory, l0: (long)offset);
                return;
            }

            case 0x01 or 0x02: // memory.atomic.wait32, memory.atomic.wait64
            {
                var (memory, info, offset) = ReadMemArg(ref reader, opcode == 1 ? 2 : 3, exact: true);
                Pop(ValType.I64);
                Pop(opcode == 1 ? ValType.I32 : ValType.I64);
                Pop(info.AddressType);
                Push(ValType.I32);
                Emit(op, i0: memory, l0: (long)offset);
                return;
            }

            case 0x03: // atomic.fence
                if (reader.ReadByte() != 0)
                    throw Error("malformed atomic.fence");

                Emit(op);
                return;

            case >= 0x10 and <= 0x16: // loads
            {
                var (size, result) = GetAtomicAccess((uint)(opcode - 0x10));
                var (memory, info, offset) = ReadMemArg(ref reader, size, exact: true);
                Pop(info.AddressType);
                Push(result);
                Emit(op, i0: memory, l0: (long)offset);
                return;
            }

            case >= 0x17 and <= 0x1D: // stores
            {
                var (size, value) = GetAtomicAccess((uint)(opcode - 0x17));
                var (memory, info, offset) = ReadMemArg(ref reader, size, exact: true);
                Pop(value);
                Pop(info.AddressType);
                Emit(op, i0: memory, l0: (long)offset);
                return;
            }

            case >= 0x1E and <= 0x4E: // read-modify-write
            {
                var index = (int)(opcode - 0x1E);
                var operation = index / 7;
                var (size, type) = GetAtomicAccess((uint)(index % 7));
                var (memory, info, offset) = ReadMemArg(ref reader, size, exact: true);
                if (operation == 6)
                {
                    Pop(type); // replacement
                }

                Pop(type);
                Pop(info.AddressType);
                Push(type);
                Emit(op, i0: memory, l0: (long)offset);
                return;
            }

            default:
                throw Error("illegal opcode fe " + opcode);
        }
    }

    /// <summary>The alignment (log2 of the size) and the type of an atomic access: i32, i64, i32 8, i32 16, i64 8, i64 16, i64 32.</summary>
    private static (int Size, ValType Type) GetAtomicAccess(uint index) => index switch
    {
        0 => (2, ValType.I32),
        1 => (3, ValType.I64),
        2 => (0, ValType.I32),
        3 => (1, ValType.I32),
        4 => (0, ValType.I64),
        5 => (1, ValType.I64),
        _ => (2, ValType.I64),
    };
}
