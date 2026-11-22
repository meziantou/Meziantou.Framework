using System.Numerics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.WasmEngine;

/// <summary>
/// A slot of the stack of the interpreter: a number in <see cref="A"/>, a vector in <see cref="A"/> (low 64 bits) and
/// <see cref="B"/> (high 64 bits), or a reference in <see cref="R"/>.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal struct Slot
{
    public long A;

    public long B;

    public object? R;

    public static Slot FromValue(WasmValue value) => new() { A = value.Kind == WasmValueKind.V128 ? value.V128Low : value.I64, B = value.V128High, R = value.Reference };

    public readonly WasmValue ToValue(ValType type) => type.Code switch
    {
        ValueCode.I32 or ValueCode.I8 or ValueCode.I16 => WasmValue.FromI32((int)A),
        ValueCode.I64 => WasmValue.FromI64(A),
        ValueCode.F32 => WasmValue.FromF32Bits((int)A),
        ValueCode.F64 => WasmValue.FromF64Bits(A),
        ValueCode.V128 => WasmValue.FromV128(A, B),
        _ => WasmValue.FromReference(R),
    };
}

internal static class Traps
{
    public static WasmTrapException Unreachable() => new("unreachable");

    public static WasmTrapException OutOfBoundsMemory() => new("out of bounds memory access");

    public static WasmTrapException OutOfBoundsTable() => new("out of bounds table access");

    public static WasmTrapException DivideByZero() => new("integer divide by zero");

    public static WasmTrapException IntegerOverflow() => new("integer overflow");

    public static WasmTrapException InvalidConversion() => new("invalid conversion to integer");

    public static WasmTrapException UndefinedElement() => new("undefined element");

    public static WasmTrapException UninitializedElement() => new("uninitialized element");

    public static WasmTrapException IndirectCallTypeMismatch() => new("indirect call type mismatch");

    public static WasmTrapException NullFunctionReference() => new("null function reference");

    public static WasmTrapException NullReference() => new("null reference");

    public static WasmTrapException NullStructureReference() => new("null structure reference");

    public static WasmTrapException NullArrayReference() => new("null array reference");

    public static WasmTrapException NullI31Reference() => new("null i31 reference");

    public static WasmTrapException NullExceptionReference() => new("null exception reference");

    public static WasmTrapException CastFailure() => new("cast failure");

    public static WasmTrapException OutOfBoundsArray() => new("out of bounds array access");

    public static WasmTrapException UnalignedAtomic() => new("unaligned atomic");

    public static WasmTrapException ExpectedSharedMemory() => new("expected shared memory");

    public static WasmTrapException StackOverflow() => new("call stack exhausted", WasmTrapKind.StackOverflow);

    public static WasmTrapException ArrayTooLarge() => new("allocation failure: array is too large");
}

/// <summary>The numeric instructions whose semantics differ from .NET (https://webassembly.github.io/spec/core/exec/numerics.html).</summary>
internal static class Numerics
{
    public static int DivS(int a, int b)
    {
        if (b == 0)
            throw Traps.DivideByZero();

        if (a == int.MinValue && b == -1)
            throw Traps.IntegerOverflow();

        return a / b;
    }

    public static int RemS(int a, int b)
    {
        if (b == 0)
            throw Traps.DivideByZero();

        return b == -1 ? 0 : a % b;
    }

    public static uint DivU(uint a, uint b) => b == 0 ? throw Traps.DivideByZero() : a / b;

    public static uint RemU(uint a, uint b) => b == 0 ? throw Traps.DivideByZero() : a % b;

    public static long DivS(long a, long b)
    {
        if (b == 0)
            throw Traps.DivideByZero();

        if (a == long.MinValue && b == -1)
            throw Traps.IntegerOverflow();

        return a / b;
    }

    public static long RemS(long a, long b)
    {
        if (b == 0)
            throw Traps.DivideByZero();

        return b == -1 ? 0 : a % b;
    }

    public static ulong DivU(ulong a, ulong b) => b == 0 ? throw Traps.DivideByZero() : a / b;

    public static ulong RemU(ulong a, ulong b) => b == 0 ? throw Traps.DivideByZero() : a % b;

    public static float Min(float a, float b) => float.IsNaN(a) || float.IsNaN(b) ? float.NaN + a + b : MathF.Min(a, b);

    public static float Max(float a, float b) => float.IsNaN(a) || float.IsNaN(b) ? float.NaN + a + b : MathF.Max(a, b);

    public static double Min(double a, double b) => double.IsNaN(a) || double.IsNaN(b) ? double.NaN + a + b : Math.Min(a, b);

    public static double Max(double a, double b) => double.IsNaN(a) || double.IsNaN(b) ? double.NaN + a + b : Math.Max(a, b);

    // The rounding of a signaling NaN gives a quiet NaN (an arithmetic NaN): .NET returns the NaN unchanged
    public static float Ceil(float value) => float.IsNaN(value) ? value + value : MathF.Ceiling(value);

    public static float Floor(float value) => float.IsNaN(value) ? value + value : MathF.Floor(value);

    public static float Trunc(float value) => float.IsNaN(value) ? value + value : MathF.Truncate(value);

    public static double Ceil(double value) => double.IsNaN(value) ? value + value : Math.Ceiling(value);

    public static double Floor(double value) => double.IsNaN(value) ? value + value : Math.Floor(value);

    public static double Trunc(double value) => double.IsNaN(value) ? value + value : Math.Truncate(value);

    public static float Nearest(float value) => float.IsNaN(value) ? value + value : MathF.Round(value, MidpointRounding.ToEven);

    public static double Nearest(double value) => double.IsNaN(value) ? value + value : Math.Round(value, MidpointRounding.ToEven);

    public static int TruncI32(double value)
    {
        if (double.IsNaN(value))
            throw Traps.InvalidConversion();

        if (value <= -2147483649.0 || value >= 2147483648.0)
            throw Traps.IntegerOverflow();

        return (int)value;
    }

    public static uint TruncU32(double value)
    {
        if (double.IsNaN(value))
            throw Traps.InvalidConversion();

        if (value <= -1.0 || value >= 4294967296.0)
            throw Traps.IntegerOverflow();

        return (uint)value;
    }

    public static long TruncI64(double value)
    {
        if (double.IsNaN(value))
            throw Traps.InvalidConversion();

        if (value < -9223372036854775808.0 || value >= 9223372036854775808.0)
            throw Traps.IntegerOverflow();

        return (long)value;
    }

    public static ulong TruncU64(double value)
    {
        if (double.IsNaN(value))
            throw Traps.InvalidConversion();

        if (value <= -1.0 || value >= 18446744073709551616.0)
            throw Traps.IntegerOverflow();

        return (ulong)value;
    }

    public static int TruncSatI32(double value)
    {
        if (double.IsNaN(value))
            return 0;

        if (value <= int.MinValue)
            return int.MinValue;

        if (value >= int.MaxValue)
            return int.MaxValue;

        return (int)value;
    }

    public static uint TruncSatU32(double value)
    {
        if (double.IsNaN(value) || value <= 0)
            return 0;

        if (value >= uint.MaxValue)
            return uint.MaxValue;

        return (uint)value;
    }

    public static long TruncSatI64(double value)
    {
        if (double.IsNaN(value))
            return 0;

        if (value <= -9223372036854775808.0)
            return long.MinValue;

        if (value >= 9223372036854775808.0)
            return long.MaxValue;

        return (long)value;
    }

    public static ulong TruncSatU64(double value)
    {
        if (double.IsNaN(value) || value <= 0)
            return 0;

        if (value >= 18446744073709551616.0)
            return ulong.MaxValue;

        return (ulong)value;
    }

    /// <summary>Converts an unsigned 64-bit integer to the nearest f32 (rounding to even, without the double rounding of a conversion through f64).</summary>
    public static float ConvertU64ToF32(ulong value)
    {
        if (value < 1UL << 53)
            return (float)(double)value;

        // Keep 53 bits, with a sticky bit for the dropped ones: the conversion of the exact double rounds once
        var shift = 11 - BitOperations.LeadingZeroCount(value);
        var kept = value >> shift;
        if ((value & ((1UL << shift) - 1)) != 0)
        {
            kept |= 1;
        }

        return (float)Math.ScaleB(kept, shift);
    }

    public static float ConvertI64ToF32(long value)
    {
        if (value >= 0)
            return ConvertU64ToF32((ulong)value);

        return -ConvertU64ToF32(unchecked((ulong)-value));
    }

    /// <summary>Converts an unsigned 64-bit integer to the nearest f64.</summary>
    public static double ConvertU64ToF64(ulong value)
    {
        if (value < 1UL << 63)
            return (long)value;

        // Halve with a sticky bit: the conversion of the signed value rounds once
        return (double)(long)((value >> 1) | (value & 1)) * 2.0;
    }

    public static int Rotl(int value, int count) => (int)BitOperations.RotateLeft((uint)value, count & 31);

    public static int Rotr(int value, int count) => (int)BitOperations.RotateRight((uint)value, count & 31);

    public static long Rotl(long value, long count) => (long)BitOperations.RotateLeft((ulong)value, (int)(count & 63));

    public static long Rotr(long value, long count) => (long)BitOperations.RotateRight((ulong)value, (int)(count & 63));

    public static float F32(long bits) => BitConverter.Int32BitsToSingle((int)bits);

    public static double F64(long bits) => BitConverter.Int64BitsToDouble(bits);

    public static long Bits(float value) => (uint)BitConverter.SingleToInt32Bits(value);

    public static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);

    /// <summary>
    /// f32.demote_f64: a NaN stays a quiet NaN (the low bits of its payload are lost). The optimizations of the JIT of .NET
    /// remove the conversions of <c>(float)(double)x</c>, which keeps a signaling NaN: the NaNs are converted with their bits.
    /// </summary>
    public static float Demote(double value)
    {
        if (double.IsNaN(value))
        {
            var bits = BitConverter.DoubleToInt64Bits(value);
            return BitConverter.Int32BitsToSingle((int)((bits >>> 32) & 0x8000_0000) | 0x7FC0_0000 | (int)((bits >>> 29) & 0x003F_FFFF));
        }

        return (float)value;
    }

    public static double Promote(float value) => value;

    public static (long Low, long High) Add128(long aLow, long aHigh, long bLow, long bHigh)
    {
        var result = new UInt128((ulong)aHigh, (ulong)aLow) + new UInt128((ulong)bHigh, (ulong)bLow);
        return ((long)(ulong)result, (long)(ulong)(result >> 64));
    }

    public static (long Low, long High) Sub128(long aLow, long aHigh, long bLow, long bHigh)
    {
        var result = new UInt128((ulong)aHigh, (ulong)aLow) - new UInt128((ulong)bHigh, (ulong)bLow);
        return ((long)(ulong)result, (long)(ulong)(result >> 64));
    }

    public static (long Low, long High) MulWideS(long a, long b)
    {
        var high = Math.BigMul(a, b, out var low);
        return (low, high);
    }

    public static (long Low, long High) MulWideU(long a, long b)
    {
        var high = Math.BigMul((ulong)a, (ulong)b, out var low);
        return ((long)low, (long)high);
    }
}
