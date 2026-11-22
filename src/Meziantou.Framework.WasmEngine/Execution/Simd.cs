using System.Numerics;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.WasmEngine;

/// <summary>
/// The vector instructions (https://webassembly.github.io/spec/core/exec/instructions.html#vector-instructions). The relaxed
/// instructions have their deterministic semantics (https://github.com/WebAssembly/relaxed-simd/blob/main/proposals/relaxed-simd/Overview.md).
/// </summary>
internal static unsafe class Simd
{
    public static Vector128<byte> ToVector(long low, long high) => Vector128.Create(low, high).AsByte();

    public static void Store(Vector128<byte> value, ref Slot slot)
    {
        var lanes = value.AsInt64();
        slot.A = lanes.GetElement(0);
        slot.B = lanes.GetElement(1);
        slot.R = null;
    }

    private static Vector128<byte> AllOnes(bool value) => value ? Vector128<byte>.AllBitsSet : Vector128<byte>.Zero;

    public static Vector128<byte> Splat(int opcode, long value) => opcode switch
    {
        0x0F => Vector128.Create((byte)value),
        0x10 => Vector128.Create((short)value).AsByte(),
        0x11 => Vector128.Create((int)value).AsByte(),
        0x12 => Vector128.Create(value).AsByte(),
        0x13 => Vector128.Create((int)value).AsByte(),
        _ => Vector128.Create(value).AsByte(),
    };

    public static long ExtractLane(int opcode, Vector128<byte> v, int lane) => opcode switch
    {
        0x15 => v.AsSByte().GetElement(lane),
        0x16 => v.GetElement(lane),
        0x18 => v.AsInt16().GetElement(lane),
        0x19 => v.AsUInt16().GetElement(lane),
        0x1B or 0x1F => v.AsInt32().GetElement(lane),
        _ => v.AsInt64().GetElement(lane),
    };

    public static Vector128<byte> ReplaceLane(int opcode, Vector128<byte> v, int lane, long value) => opcode switch
    {
        0x17 => v.WithElement(lane, (byte)value),
        0x1A => v.AsInt16().WithElement(lane, (short)value).AsByte(),
        0x1C or 0x20 => v.AsInt32().WithElement(lane, (int)value).AsByte(),
        _ => v.AsInt64().WithElement(lane, value).AsByte(),
    };

    public static Vector128<byte> Shuffle(Vector128<byte> a, Vector128<byte> b, long low, long high)
    {
        var lanes = ToVector(low, high);
        Vector128<byte> result = default;
        var r = (byte*)&result;
        var pa = (byte*)&a;
        var pb = (byte*)&b;
        var pl = (byte*)&lanes;
        for (var i = 0; i < 16; i++)
        {
            var lane = pl[i];
            r[i] = lane < 16 ? pa[lane] : pb[lane - 16];
        }

        return result;
    }

    public static int Test(int opcode, Vector128<byte> v) => opcode switch
    {
        0x53 => v != Vector128<byte>.Zero ? 1 : 0,
        0x63 => Vector128.EqualsAny(v, Vector128<byte>.Zero) ? 0 : 1,
        0x83 => Vector128.EqualsAny(v.AsUInt16(), Vector128<ushort>.Zero) ? 0 : 1,
        0xA3 => Vector128.EqualsAny(v.AsUInt32(), Vector128<uint>.Zero) ? 0 : 1,
        0xC3 => Vector128.EqualsAny(v.AsUInt64(), Vector128<ulong>.Zero) ? 0 : 1,
        0x64 => (int)v.ExtractMostSignificantBits(),
        0x84 => (int)v.AsInt16().ExtractMostSignificantBits(),
        0xA4 => (int)v.AsInt32().ExtractMostSignificantBits(),
        _ => (int)v.AsInt64().ExtractMostSignificantBits(),
    };

    public static Vector128<byte> Shift(int opcode, Vector128<byte> v, int count) => opcode switch
    {
        0x6B => Vector128.ShiftLeft(v, count & 7),
        0x6C => Vector128.ShiftRightArithmetic(v.AsSByte(), count & 7).AsByte(),
        0x6D => Vector128.ShiftRightLogical(v, count & 7),
        0x8B => Vector128.ShiftLeft(v.AsInt16(), count & 15).AsByte(),
        0x8C => Vector128.ShiftRightArithmetic(v.AsInt16(), count & 15).AsByte(),
        0x8D => Vector128.ShiftRightLogical(v.AsUInt16(), count & 15).AsByte(),
        0xAB => Vector128.ShiftLeft(v.AsInt32(), count & 31).AsByte(),
        0xAC => Vector128.ShiftRightArithmetic(v.AsInt32(), count & 31).AsByte(),
        0xAD => Vector128.ShiftRightLogical(v.AsUInt32(), count & 31).AsByte(),
        0xCB => Vector128.ShiftLeft(v.AsInt64(), count & 63).AsByte(),
        0xCC => Vector128.ShiftRightArithmetic(v.AsInt64(), count & 63).AsByte(),
        _ => Vector128.ShiftRightLogical(v.AsUInt64(), count & 63).AsByte(),
    };

    public static Vector128<byte> Unary(int opcode, Vector128<byte> v)
    {
        switch (opcode)
        {
            case 0x4D: return ~v;
            case 0x5E: // f32x4.demote_f64x2_zero
            {
                var d = v.AsDouble();
                return Vector128.Create((float)d.GetElement(0), (float)d.GetElement(1), 0f, 0f).AsByte();
            }

            case 0x5F: // f64x2.promote_low_f32x4
            {
                var f = v.AsSingle();
                return Vector128.Create((double)f.GetElement(0), f.GetElement(1)).AsByte();
            }

            case 0x60: return Vector128.Abs(v.AsSByte()).AsByte();
            case 0x61: return Vector128.Negate(v.AsSByte()).AsByte();
            case 0x62: return MapBytes(v, b => (byte)BitOperations.PopCount(b));
            case 0x67: return MapSingle(v, Numerics.Ceil);
            case 0x68: return MapSingle(v, Numerics.Floor);
            case 0x69: return MapSingle(v, Numerics.Trunc);
            case 0x6A: return MapSingle(v, Numerics.Nearest);
            case 0x74: return MapDouble(v, Numerics.Ceil);
            case 0x75: return MapDouble(v, Numerics.Floor);
            case 0x7A: return MapDouble(v, Numerics.Trunc);
            case 0x94: return MapDouble(v, Numerics.Nearest);
            case 0x7C: return ExtAddPairwise8(v, signed: true);
            case 0x7D: return ExtAddPairwise8(v, signed: false);
            case 0x7E: return ExtAddPairwise16(v, signed: true);
            case 0x7F: return ExtAddPairwise16(v, signed: false);
            case 0x80: return Vector128.Abs(v.AsInt16()).AsByte();
            case 0x81: return Vector128.Negate(v.AsInt16()).AsByte();
            case 0x87: return Vector128.WidenLower(v.AsSByte()).AsByte();
            case 0x88: return Vector128.WidenUpper(v.AsSByte()).AsByte();
            case 0x89: return Vector128.WidenLower(v).AsByte();
            case 0x8A: return Vector128.WidenUpper(v).AsByte();
            case 0xA0: return Vector128.Abs(v.AsInt32()).AsByte();
            case 0xA1: return Vector128.Negate(v.AsInt32()).AsByte();
            case 0xA7: return Vector128.WidenLower(v.AsInt16()).AsByte();
            case 0xA8: return Vector128.WidenUpper(v.AsInt16()).AsByte();
            case 0xA9: return Vector128.WidenLower(v.AsUInt16()).AsByte();
            case 0xAA: return Vector128.WidenUpper(v.AsUInt16()).AsByte();
            case 0xC0: return Vector128.Abs(v.AsInt64()).AsByte();
            case 0xC1: return Vector128.Negate(v.AsInt64()).AsByte();
            case 0xC7: return Vector128.WidenLower(v.AsInt32()).AsByte();
            case 0xC8: return Vector128.WidenUpper(v.AsInt32()).AsByte();
            case 0xC9: return Vector128.WidenLower(v.AsUInt32()).AsByte();
            case 0xCA: return Vector128.WidenUpper(v.AsUInt32()).AsByte();
            case 0xE0: return (v.AsUInt32() & Vector128.Create(0x7FFF_FFFFu)).AsByte();
            case 0xE1: return (v.AsUInt32() ^ Vector128.Create(0x8000_0000u)).AsByte();
            case 0xE3: return MapSingle(v, MathF.Sqrt);
            case 0xEC: return (v.AsUInt64() & Vector128.Create(0x7FFF_FFFF_FFFF_FFFFul)).AsByte();
            case 0xED: return (v.AsUInt64() ^ Vector128.Create(0x8000_0000_0000_0000ul)).AsByte();
            case 0xEF: return MapDouble(v, Math.Sqrt);
            case 0xF8 or 0x101: // i32x4.trunc_sat_f32x4_s, i32x4.relaxed_trunc_f32x4_s
            {
                var f = v.AsSingle();
                return Vector128.Create(Numerics.TruncSatI32(f.GetElement(0)), Numerics.TruncSatI32(f.GetElement(1)), Numerics.TruncSatI32(f.GetElement(2)), Numerics.TruncSatI32(f.GetElement(3))).AsByte();
            }

            case 0xF9 or 0x102:
            {
                var f = v.AsSingle();
                return Vector128.Create(Numerics.TruncSatU32(f.GetElement(0)), Numerics.TruncSatU32(f.GetElement(1)), Numerics.TruncSatU32(f.GetElement(2)), Numerics.TruncSatU32(f.GetElement(3))).AsByte();
            }

            case 0xFA:
            {
                var i = v.AsInt32();
                return Vector128.Create((float)i.GetElement(0), i.GetElement(1), i.GetElement(2), i.GetElement(3)).AsByte();
            }

            case 0xFB:
            {
                var i = v.AsUInt32();
                return Vector128.Create((float)i.GetElement(0), i.GetElement(1), i.GetElement(2), i.GetElement(3)).AsByte();
            }

            case 0xFC or 0x103:
            {
                var d = v.AsDouble();
                return Vector128.Create(Numerics.TruncSatI32(d.GetElement(0)), Numerics.TruncSatI32(d.GetElement(1)), 0, 0).AsByte();
            }

            case 0xFD or 0x104:
            {
                var d = v.AsDouble();
                return Vector128.Create(Numerics.TruncSatU32(d.GetElement(0)), Numerics.TruncSatU32(d.GetElement(1)), 0, 0).AsByte();
            }

            case 0xFE:
            {
                var i = v.AsInt32();
                return Vector128.Create((double)i.GetElement(0), i.GetElement(1)).AsByte();
            }

            case 0xFF:
            {
                var i = v.AsUInt32();
                return Vector128.Create((double)i.GetElement(0), i.GetElement(1)).AsByte();
            }

            default:
                throw new InvalidOperationException("Unknown vector instruction " + opcode);
        }
    }

    public static Vector128<byte> Binary(int opcode, Vector128<byte> a, Vector128<byte> b)
    {
        switch (opcode)
        {
            case 0x0E or 0x100: return Vector128.Shuffle(a, b);

            // i8x16 comparisons
            case 0x23: return Vector128.Equals(a, b);
            case 0x24: return ~Vector128.Equals(a, b);
            case 0x25: return Vector128.LessThan(a.AsSByte(), b.AsSByte()).AsByte();
            case 0x26: return Vector128.LessThan(a, b);
            case 0x27: return Vector128.GreaterThan(a.AsSByte(), b.AsSByte()).AsByte();
            case 0x28: return Vector128.GreaterThan(a, b);
            case 0x29: return Vector128.LessThanOrEqual(a.AsSByte(), b.AsSByte()).AsByte();
            case 0x2A: return Vector128.LessThanOrEqual(a, b);
            case 0x2B: return Vector128.GreaterThanOrEqual(a.AsSByte(), b.AsSByte()).AsByte();
            case 0x2C: return Vector128.GreaterThanOrEqual(a, b);

            // i16x8 comparisons
            case 0x2D: return Vector128.Equals(a.AsInt16(), b.AsInt16()).AsByte();
            case 0x2E: return (~Vector128.Equals(a.AsInt16(), b.AsInt16())).AsByte();
            case 0x2F: return Vector128.LessThan(a.AsInt16(), b.AsInt16()).AsByte();
            case 0x30: return Vector128.LessThan(a.AsUInt16(), b.AsUInt16()).AsByte();
            case 0x31: return Vector128.GreaterThan(a.AsInt16(), b.AsInt16()).AsByte();
            case 0x32: return Vector128.GreaterThan(a.AsUInt16(), b.AsUInt16()).AsByte();
            case 0x33: return Vector128.LessThanOrEqual(a.AsInt16(), b.AsInt16()).AsByte();
            case 0x34: return Vector128.LessThanOrEqual(a.AsUInt16(), b.AsUInt16()).AsByte();
            case 0x35: return Vector128.GreaterThanOrEqual(a.AsInt16(), b.AsInt16()).AsByte();
            case 0x36: return Vector128.GreaterThanOrEqual(a.AsUInt16(), b.AsUInt16()).AsByte();

            // i32x4 comparisons
            case 0x37: return Vector128.Equals(a.AsInt32(), b.AsInt32()).AsByte();
            case 0x38: return (~Vector128.Equals(a.AsInt32(), b.AsInt32())).AsByte();
            case 0x39: return Vector128.LessThan(a.AsInt32(), b.AsInt32()).AsByte();
            case 0x3A: return Vector128.LessThan(a.AsUInt32(), b.AsUInt32()).AsByte();
            case 0x3B: return Vector128.GreaterThan(a.AsInt32(), b.AsInt32()).AsByte();
            case 0x3C: return Vector128.GreaterThan(a.AsUInt32(), b.AsUInt32()).AsByte();
            case 0x3D: return Vector128.LessThanOrEqual(a.AsInt32(), b.AsInt32()).AsByte();
            case 0x3E: return Vector128.LessThanOrEqual(a.AsUInt32(), b.AsUInt32()).AsByte();
            case 0x3F: return Vector128.GreaterThanOrEqual(a.AsInt32(), b.AsInt32()).AsByte();
            case 0x40: return Vector128.GreaterThanOrEqual(a.AsUInt32(), b.AsUInt32()).AsByte();

            // f32x4 and f64x2 comparisons (a NaN compares false, except ne)
            case 0x41: return Vector128.Equals(a.AsSingle(), b.AsSingle()).AsByte();
            case 0x42: return (~Vector128.Equals(a.AsSingle(), b.AsSingle())).AsByte();
            case 0x43: return Vector128.LessThan(a.AsSingle(), b.AsSingle()).AsByte();
            case 0x44: return Vector128.GreaterThan(a.AsSingle(), b.AsSingle()).AsByte();
            case 0x45: return Vector128.LessThanOrEqual(a.AsSingle(), b.AsSingle()).AsByte();
            case 0x46: return Vector128.GreaterThanOrEqual(a.AsSingle(), b.AsSingle()).AsByte();
            case 0x47: return Vector128.Equals(a.AsDouble(), b.AsDouble()).AsByte();
            case 0x48: return (~Vector128.Equals(a.AsDouble(), b.AsDouble())).AsByte();
            case 0x49: return Vector128.LessThan(a.AsDouble(), b.AsDouble()).AsByte();
            case 0x4A: return Vector128.GreaterThan(a.AsDouble(), b.AsDouble()).AsByte();
            case 0x4B: return Vector128.LessThanOrEqual(a.AsDouble(), b.AsDouble()).AsByte();
            case 0x4C: return Vector128.GreaterThanOrEqual(a.AsDouble(), b.AsDouble()).AsByte();

            case 0x4E: return a & b;
            case 0x4F: return Vector128.AndNot(a, b);
            case 0x50: return a | b;
            case 0x51: return a ^ b;

            case 0x65: return NarrowI16(a, b, signed: true);
            case 0x66: return NarrowI16(a, b, signed: false);
            case 0x85: return NarrowI32(a, b, signed: true);
            case 0x86: return NarrowI32(a, b, signed: false);

            // i8x16 arithmetic
            case 0x6E: return a + b;
            case 0x6F: return Map2SByte(a, b, (x, y) => (sbyte)Math.Clamp(x + y, sbyte.MinValue, sbyte.MaxValue));
            case 0x70: return Map2Bytes(a, b, (x, y) => (byte)Math.Min(x + y, byte.MaxValue));
            case 0x71: return a - b;
            case 0x72: return Map2SByte(a, b, (x, y) => (sbyte)Math.Clamp(x - y, sbyte.MinValue, sbyte.MaxValue));
            case 0x73: return Map2Bytes(a, b, (x, y) => (byte)Math.Max(x - y, 0));
            case 0x76: return Vector128.Min(a.AsSByte(), b.AsSByte()).AsByte();
            case 0x77: return Vector128.Min(a, b);
            case 0x78: return Vector128.Max(a.AsSByte(), b.AsSByte()).AsByte();
            case 0x79: return Vector128.Max(a, b);
            case 0x7B: return Map2Bytes(a, b, (x, y) => (byte)((x + y + 1) >> 1));

            // i16x8 arithmetic
            case 0x82 or 0x111: return Map2Int16(a, b, (x, y) => (short)Math.Clamp((x * y + 0x4000) >> 15, short.MinValue, short.MaxValue));
            case 0x8E: return (a.AsInt16() + b.AsInt16()).AsByte();
            case 0x8F: return Map2Int16(a, b, (x, y) => (short)Math.Clamp(x + y, short.MinValue, short.MaxValue));
            case 0x90: return Map2UInt16(a, b, (x, y) => (ushort)Math.Min(x + y, ushort.MaxValue));
            case 0x91: return (a.AsInt16() - b.AsInt16()).AsByte();
            case 0x92: return Map2Int16(a, b, (x, y) => (short)Math.Clamp(x - y, short.MinValue, short.MaxValue));
            case 0x93: return Map2UInt16(a, b, (x, y) => (ushort)Math.Max(x - y, 0));
            case 0x95: return (a.AsInt16() * b.AsInt16()).AsByte();
            case 0x96: return Vector128.Min(a.AsInt16(), b.AsInt16()).AsByte();
            case 0x97: return Vector128.Min(a.AsUInt16(), b.AsUInt16()).AsByte();
            case 0x98: return Vector128.Max(a.AsInt16(), b.AsInt16()).AsByte();
            case 0x99: return Vector128.Max(a.AsUInt16(), b.AsUInt16()).AsByte();
            case 0x9B: return Map2UInt16(a, b, (x, y) => (ushort)((x + y + 1) >> 1));
            case 0x9C: return (Vector128.WidenLower(a.AsSByte()) * Vector128.WidenLower(b.AsSByte())).AsByte();
            case 0x9D: return (Vector128.WidenUpper(a.AsSByte()) * Vector128.WidenUpper(b.AsSByte())).AsByte();
            case 0x9E: return (Vector128.WidenLower(a) * Vector128.WidenLower(b)).AsByte();
            case 0x9F: return (Vector128.WidenUpper(a) * Vector128.WidenUpper(b)).AsByte();

            // i32x4 arithmetic
            case 0xAE: return (a.AsInt32() + b.AsInt32()).AsByte();
            case 0xB1: return (a.AsInt32() - b.AsInt32()).AsByte();
            case 0xB5: return (a.AsInt32() * b.AsInt32()).AsByte();
            case 0xB6: return Vector128.Min(a.AsInt32(), b.AsInt32()).AsByte();
            case 0xB7: return Vector128.Min(a.AsUInt32(), b.AsUInt32()).AsByte();
            case 0xB8: return Vector128.Max(a.AsInt32(), b.AsInt32()).AsByte();
            case 0xB9: return Vector128.Max(a.AsUInt32(), b.AsUInt32()).AsByte();
            case 0xBA: return DotI16(a, b);
            case 0xBC: return (Vector128.WidenLower(a.AsInt16()) * Vector128.WidenLower(b.AsInt16())).AsByte();
            case 0xBD: return (Vector128.WidenUpper(a.AsInt16()) * Vector128.WidenUpper(b.AsInt16())).AsByte();
            case 0xBE: return (Vector128.WidenLower(a.AsUInt16()) * Vector128.WidenLower(b.AsUInt16())).AsByte();
            case 0xBF: return (Vector128.WidenUpper(a.AsUInt16()) * Vector128.WidenUpper(b.AsUInt16())).AsByte();

            // i64x2 arithmetic and comparisons
            case 0xCE: return (a.AsInt64() + b.AsInt64()).AsByte();
            case 0xD1: return (a.AsInt64() - b.AsInt64()).AsByte();
            case 0xD5: return (a.AsInt64() * b.AsInt64()).AsByte();
            case 0xD6: return Vector128.Equals(a.AsInt64(), b.AsInt64()).AsByte();
            case 0xD7: return (~Vector128.Equals(a.AsInt64(), b.AsInt64())).AsByte();
            case 0xD8: return Vector128.LessThan(a.AsInt64(), b.AsInt64()).AsByte();
            case 0xD9: return Vector128.GreaterThan(a.AsInt64(), b.AsInt64()).AsByte();
            case 0xDA: return Vector128.LessThanOrEqual(a.AsInt64(), b.AsInt64()).AsByte();
            case 0xDB: return Vector128.GreaterThanOrEqual(a.AsInt64(), b.AsInt64()).AsByte();
            case 0xDC: return (Vector128.WidenLower(a.AsInt32()) * Vector128.WidenLower(b.AsInt32())).AsByte();
            case 0xDD: return (Vector128.WidenUpper(a.AsInt32()) * Vector128.WidenUpper(b.AsInt32())).AsByte();
            case 0xDE: return (Vector128.WidenLower(a.AsUInt32()) * Vector128.WidenLower(b.AsUInt32())).AsByte();
            case 0xDF: return (Vector128.WidenUpper(a.AsUInt32()) * Vector128.WidenUpper(b.AsUInt32())).AsByte();

            // f32x4 arithmetic
            case 0xE4: return (a.AsSingle() + b.AsSingle()).AsByte();
            case 0xE5: return (a.AsSingle() - b.AsSingle()).AsByte();
            case 0xE6: return (a.AsSingle() * b.AsSingle()).AsByte();
            case 0xE7: return (a.AsSingle() / b.AsSingle()).AsByte();
            case 0xE8 or 0x10D: return Map2Single(a, b, Numerics.Min);
            case 0xE9 or 0x10E: return Map2Single(a, b, Numerics.Max);
            case 0xEA: return Map2Single(a, b, (x, y) => y < x ? y : x);
            case 0xEB: return Map2Single(a, b, (x, y) => x < y ? y : x);

            // f64x2 arithmetic
            case 0xF0: return (a.AsDouble() + b.AsDouble()).AsByte();
            case 0xF1: return (a.AsDouble() - b.AsDouble()).AsByte();
            case 0xF2: return (a.AsDouble() * b.AsDouble()).AsByte();
            case 0xF3: return (a.AsDouble() / b.AsDouble()).AsByte();
            case 0xF4 or 0x10F: return Map2Double(a, b, Numerics.Min);
            case 0xF5 or 0x110: return Map2Double(a, b, Numerics.Max);
            case 0xF6: return Map2Double(a, b, (x, y) => y < x ? y : x);
            case 0xF7: return Map2Double(a, b, (x, y) => x < y ? y : x);

            case 0x112: return DotI8I7(a, b);

            default:
                throw new InvalidOperationException("Unknown vector instruction " + opcode);
        }
    }

    public static Vector128<byte> Ternary(int opcode, Vector128<byte> a, Vector128<byte> b, Vector128<byte> c)
    {
        switch (opcode)
        {
            case 0x52 or 0x109 or 0x10A or 0x10B or 0x10C: return Vector128.ConditionalSelect(c, a, b);
            case 0x105: return Map3Single(a, b, c, (x, y, z) => MathF.FusedMultiplyAdd(x, y, z));
            case 0x106: return Map3Single(a, b, c, (x, y, z) => MathF.FusedMultiplyAdd(-x, y, z));
            case 0x107: return Map3Double(a, b, c, (x, y, z) => Math.FusedMultiplyAdd(x, y, z));
            case 0x108: return Map3Double(a, b, c, (x, y, z) => Math.FusedMultiplyAdd(-x, y, z));
            case 0x113: // i32x4.relaxed_dot_i8x16_i7x16_add_s
            {
                var dot = DotI8I7(a, b).AsInt16();
                var acc = c.AsInt32();
                Vector128<int> result = default;
                var r = (int*)&result;
                for (var i = 0; i < 4; i++)
                {
                    r[i] = dot.GetElement(2 * i) + dot.GetElement((2 * i) + 1) + acc.GetElement(i);
                }

                return result.AsByte();
            }

            default:
                throw new InvalidOperationException("Unknown vector instruction " + opcode);
        }
    }

    private static Vector128<byte> MapBytes(Vector128<byte> v, Func<byte, byte> map)
    {
        var p = (byte*)&v;
        for (var i = 0; i < 16; i++)
        {
            p[i] = map(p[i]);
        }

        return v;
    }

    private static Vector128<byte> Map2Bytes(Vector128<byte> a, Vector128<byte> b, Func<int, int, byte> map)
    {
        var pa = (byte*)&a;
        var pb = (byte*)&b;
        for (var i = 0; i < 16; i++)
        {
            pa[i] = map(pa[i], pb[i]);
        }

        return a;
    }

    private static Vector128<byte> Map2SByte(Vector128<byte> a, Vector128<byte> b, Func<int, int, sbyte> map)
    {
        var pa = (sbyte*)&a;
        var pb = (sbyte*)&b;
        for (var i = 0; i < 16; i++)
        {
            pa[i] = map(pa[i], pb[i]);
        }

        return a;
    }

    private static Vector128<byte> Map2Int16(Vector128<byte> a, Vector128<byte> b, Func<int, int, short> map)
    {
        var pa = (short*)&a;
        var pb = (short*)&b;
        for (var i = 0; i < 8; i++)
        {
            pa[i] = map(pa[i], pb[i]);
        }

        return a;
    }

    private static Vector128<byte> Map2UInt16(Vector128<byte> a, Vector128<byte> b, Func<int, int, ushort> map)
    {
        var pa = (ushort*)&a;
        var pb = (ushort*)&b;
        for (var i = 0; i < 8; i++)
        {
            pa[i] = map(pa[i], pb[i]);
        }

        return a;
    }

    private static Vector128<byte> MapSingle(Vector128<byte> v, Func<float, float> map)
    {
        var p = (float*)&v;
        for (var i = 0; i < 4; i++)
        {
            p[i] = map(p[i]);
        }

        return v;
    }

    private static Vector128<byte> MapDouble(Vector128<byte> v, Func<double, double> map)
    {
        var p = (double*)&v;
        for (var i = 0; i < 2; i++)
        {
            p[i] = map(p[i]);
        }

        return v;
    }

    private static Vector128<byte> Map2Single(Vector128<byte> a, Vector128<byte> b, Func<float, float, float> map)
    {
        var pa = (float*)&a;
        var pb = (float*)&b;
        for (var i = 0; i < 4; i++)
        {
            pa[i] = map(pa[i], pb[i]);
        }

        return a;
    }

    private static Vector128<byte> Map2Double(Vector128<byte> a, Vector128<byte> b, Func<double, double, double> map)
    {
        var pa = (double*)&a;
        var pb = (double*)&b;
        for (var i = 0; i < 2; i++)
        {
            pa[i] = map(pa[i], pb[i]);
        }

        return a;
    }

    private static Vector128<byte> Map3Single(Vector128<byte> a, Vector128<byte> b, Vector128<byte> c, Func<float, float, float, float> map)
    {
        var pa = (float*)&a;
        var pb = (float*)&b;
        var pc = (float*)&c;
        for (var i = 0; i < 4; i++)
        {
            pa[i] = map(pa[i], pb[i], pc[i]);
        }

        return a;
    }

    private static Vector128<byte> Map3Double(Vector128<byte> a, Vector128<byte> b, Vector128<byte> c, Func<double, double, double, double> map)
    {
        var pa = (double*)&a;
        var pb = (double*)&b;
        var pc = (double*)&c;
        for (var i = 0; i < 2; i++)
        {
            pa[i] = map(pa[i], pb[i], pc[i]);
        }

        return a;
    }

    private static Vector128<byte> NarrowI16(Vector128<byte> a, Vector128<byte> b, bool signed)
    {
        Vector128<byte> result = default;
        var r = (byte*)&result;
        var pa = (short*)&a;
        var pb = (short*)&b;
        for (var i = 0; i < 8; i++)
        {
            r[i] = signed ? (byte)(sbyte)Math.Clamp(pa[i], sbyte.MinValue, sbyte.MaxValue) : (byte)Math.Clamp(pa[i], (short)0, (short)byte.MaxValue);
            r[i + 8] = signed ? (byte)(sbyte)Math.Clamp(pb[i], sbyte.MinValue, sbyte.MaxValue) : (byte)Math.Clamp(pb[i], (short)0, (short)byte.MaxValue);
        }

        return result;
    }

    private static Vector128<byte> NarrowI32(Vector128<byte> a, Vector128<byte> b, bool signed)
    {
        Vector128<ushort> result = default;
        var r = (ushort*)&result;
        var pa = (int*)&a;
        var pb = (int*)&b;
        for (var i = 0; i < 4; i++)
        {
            r[i] = signed ? (ushort)(short)Math.Clamp(pa[i], short.MinValue, short.MaxValue) : (ushort)Math.Clamp(pa[i], 0, ushort.MaxValue);
            r[i + 4] = signed ? (ushort)(short)Math.Clamp(pb[i], short.MinValue, short.MaxValue) : (ushort)Math.Clamp(pb[i], 0, ushort.MaxValue);
        }

        return result.AsByte();
    }

    private static Vector128<byte> ExtAddPairwise8(Vector128<byte> v, bool signed)
    {
        Vector128<short> result = default;
        var r = (short*)&result;
        var p = (byte*)&v;
        for (var i = 0; i < 8; i++)
        {
            r[i] = signed ? (short)((sbyte)p[2 * i] + (sbyte)p[(2 * i) + 1]) : (short)(p[2 * i] + p[(2 * i) + 1]);
        }

        return result.AsByte();
    }

    private static Vector128<byte> ExtAddPairwise16(Vector128<byte> v, bool signed)
    {
        Vector128<int> result = default;
        var r = (int*)&result;
        var p = (ushort*)&v;
        for (var i = 0; i < 4; i++)
        {
            r[i] = signed ? (short)p[2 * i] + (short)p[(2 * i) + 1] : p[2 * i] + p[(2 * i) + 1];
        }

        return result.AsByte();
    }

    private static Vector128<byte> DotI16(Vector128<byte> a, Vector128<byte> b)
    {
        Vector128<int> result = default;
        var r = (int*)&result;
        var pa = (short*)&a;
        var pb = (short*)&b;
        for (var i = 0; i < 4; i++)
        {
            r[i] = unchecked((pa[2 * i] * pb[2 * i]) + (pa[(2 * i) + 1] * pb[(2 * i) + 1]));
        }

        return result.AsByte();
    }

    /// <summary>i16x8.relaxed_dot_i8x16_i7x16_s: the second operand is interpreted as signed.</summary>
    private static Vector128<byte> DotI8I7(Vector128<byte> a, Vector128<byte> b)
    {
        Vector128<short> result = default;
        var r = (short*)&result;
        var pa = (sbyte*)&a;
        var pb = (sbyte*)&b;
        for (var i = 0; i < 8; i++)
        {
            r[i] = unchecked((short)((pa[2 * i] * pb[2 * i]) + (pa[(2 * i) + 1] * pb[(2 * i) + 1])));
        }

        return result.AsByte();
    }

    public static bool AllOnesFor(bool value) => AllOnes(value) == Vector128<byte>.AllBitsSet;
}
