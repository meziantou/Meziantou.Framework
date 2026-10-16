using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The exact integer inverse transforms of VP8 (RFC 6386 section 14): the Walsh-Hadamard transform of the Y2 block and the
/// 4x4 inverse DCT with its 16-bit fixed-point constants, added to the prediction with clamping. The forward transforms
/// used by the encoder are in <c>Vp8ForwardTransforms</c>.
/// </summary>
internal static class Vp8Transforms
{
    /// <summary>cos(pi/8) * sqrt(2) - 1, in 16-bit fixed point.</summary>
    public const int CosPi8Sqrt2Minus1 = 20091;

    /// <summary>sin(pi/8) * sqrt(2), in 16-bit fixed point.</summary>
    public const int SinPi8Sqrt2 = 35468;

    /// <summary>Inverts the Walsh-Hadamard transform of the Y2 block into coefficient 0 of each of the 16 luma blocks.</summary>
    /// <param name="input">The 16 dequantized Y2 coefficients, in raster order.</param>
    /// <param name="coefficients">The macroblock coefficients: block <c>i</c> starts at <c>16 * i</c>.</param>
    public static void InverseWalshHadamard(ReadOnlySpan<short> input, Span<short> coefficients)
    {
        Span<int> temp = stackalloc int[16];
        for (var i = 0; i < 4; i++)
        {
            var a1 = input[i] + input[12 + i];
            var b1 = input[4 + i] + input[8 + i];
            var c1 = input[4 + i] - input[8 + i];
            var d1 = input[i] - input[12 + i];
            temp[i] = a1 + b1;
            temp[4 + i] = c1 + d1;
            temp[8 + i] = a1 - b1;
            temp[12 + i] = d1 - c1;
        }

        for (var i = 0; i < 4; i++)
        {
            var row = temp.Slice(4 * i, 4);
            var a1 = row[0] + row[3];
            var b1 = row[1] + row[2];
            var c1 = row[1] - row[2];
            var d1 = row[0] - row[3];
            coefficients[16 * ((4 * i) + 0)] = (short)((a1 + b1 + 3) >> 3);
            coefficients[16 * ((4 * i) + 1)] = (short)((c1 + d1 + 3) >> 3);
            coefficients[16 * ((4 * i) + 2)] = (short)((a1 - b1 + 3) >> 3);
            coefficients[16 * ((4 * i) + 3)] = (short)((d1 - c1 + 3) >> 3);
        }
    }

    /// <summary>Adds the inverse DCT of a block to the 4x4 prediction at <paramref name="destination"/>, clamping to 0-255.</summary>
    /// <param name="block">The 16 dequantized coefficients, in raster order.</param>
    /// <param name="destination">The top-left pixel of the block; rows are <paramref name="stride"/> bytes apart.</param>
    /// <param name="stride">The row length of the destination plane.</param>
    public static void InverseDctAdd(ReadOnlySpan<short> block, Span<byte> destination, int stride)
    {
        var acZero = true;
        for (var i = 1; i < 16; i++)
        {
            if (block[i] != 0)
            {
                acZero = false;
                break;
            }
        }

        if (acZero)
        {
            InverseDctAddDc(block[0], destination, stride);
        }
        else
        {
            InverseDctAddFull(block, destination, stride);
        }
    }

    /// <summary>Adds the inverse DCT of a block whose only non-zero coefficient is the DC: exactly (dc + 4) >> 3 everywhere.</summary>
    public static void InverseDctAddDc(short dcCoefficient, Span<byte> destination, int stride)
    {
        if (dcCoefficient == 0)
            return;

        var dc = (dcCoefficient + 4) >> 3;
        for (var y = 0; y < 4; y++)
        {
            var row = destination.Slice(y * stride, 4);
            for (var x = 0; x < 4; x++)
            {
                row[x] = ClampByte(row[x] + dc);
            }
        }
    }

    /// <summary>Adds the inverse DCT of a block to the 4x4 prediction (any coefficients; exact for DC-only blocks too).</summary>
    /// <remarks>
    /// The vector path computes the vertical pass on the 4 columns at once (rows of 32-bit lanes), transposes, computes the
    /// horizontal pass on the 4 rows at once, transposes back and adds the rows with clamping. The 32-bit products and
    /// shifts are the ones of the scalar pass (<see cref="InverseDctAddFullScalar"/>), wrapping included.
    /// </remarks>
    public static void InverseDctAddFull(ReadOnlySpan<short> block, Span<byte> destination, int stride)
    {
        if (!Vector128.IsHardwareAccelerated)
        {
            InverseDctAddFullScalar(block, destination, stride);
            return;
        }

        block = block[..16];
        var low = Vector128.Create(block[..8]);
        var high = Vector128.Create(block.Slice(8, 8));
        var r0 = Vector128.WidenLower(low);
        var r1 = Vector128.WidenUpper(low);
        var r2 = Vector128.WidenLower(high);
        var r3 = Vector128.WidenUpper(high);
        Pass(ref r0, ref r1, ref r2, ref r3);
        Transpose4x4(ref r0, ref r1, ref r2, ref r3);
        Pass(ref r0, ref r1, ref r2, ref r3);

        // Columns 0 to 3 of the rows, rounded: back to rows
        var four = Vector128.Create(4);
        r0 = Vector128.ShiftRightArithmetic(r0 + four, 3);
        r1 = Vector128.ShiftRightArithmetic(r1 + four, 3);
        r2 = Vector128.ShiftRightArithmetic(r2 + four, 3);
        r3 = Vector128.ShiftRightArithmetic(r3 + four, 3);
        Transpose4x4(ref r0, ref r1, ref r2, ref r3);
        // Clamped in 32-bit lanes (the sums can exceed 16 bits), then narrowed
        var sum01 = Vector128.Narrow(Clamp(r0 + Load(destination, 0)), Clamp(r1 + Load(destination, stride)));
        var sum23 = Vector128.Narrow(Clamp(r2 + Load(destination, 2 * stride)), Clamp(r3 + Load(destination, 3 * stride)));
        var bytes = Vector128.Narrow(sum01.AsUInt16(), sum23.AsUInt16()).AsUInt32();
        BinaryPrimitives.WriteUInt32LittleEndian(destination[..4], Little(bytes.GetElement(0)));
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(stride, 4), Little(bytes.GetElement(1)));
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(2 * stride, 4), Little(bytes.GetElement(2)));
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(3 * stride, 4), Little(bytes.GetElement(3)));

        // The 4 bytes of a row as 32-bit lanes
        static Vector128<int> Load(ReadOnlySpan<byte> destination, int offset)
        {
            var row = Vector128.CreateScalar(BinaryPrimitives.ReadUInt32LittleEndian(destination.Slice(offset, 4))).AsByte();
            if (!BitConverter.IsLittleEndian)
            {
                row = Vector128.Create(destination[offset], destination[offset + 1], destination[offset + 2], destination[offset + 3], 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            }

            return Vector128.WidenLower(Vector128.WidenLower(row)).AsInt32();
        }

        static Vector128<int> Clamp(Vector128<int> value) => Vector128.Min(Vector128.Max(value, Vector128<int>.Zero), Vector128.Create(255));

        // The lane holds the bytes in memory order: on a big-endian machine, written back in that order
        static uint Little(uint value) => BitConverter.IsLittleEndian ? value : BinaryPrimitives.ReverseEndianness(value);

        // One pass on 4 vectors (the inputs 0 to 3 of the 4 lanes): a1, b1, c1, d1 and the outputs in place
        static void Pass(ref Vector128<int> i0, ref Vector128<int> i1, ref Vector128<int> i2, ref Vector128<int> i3)
        {
            var a1 = i0 + i2;
            var b1 = i0 - i2;
            var c1 = Vector128.ShiftRightArithmetic(i1 * SinPi8Sqrt2, 16) - (i3 + Vector128.ShiftRightArithmetic(i3 * CosPi8Sqrt2Minus1, 16));
            var d1 = (i1 + Vector128.ShiftRightArithmetic(i1 * CosPi8Sqrt2Minus1, 16)) + Vector128.ShiftRightArithmetic(i3 * SinPi8Sqrt2, 16);
            i0 = a1 + d1;
            i1 = b1 + c1;
            i2 = b1 - c1;
            i3 = a1 - d1;
        }
    }

    /// <summary>Transposes a 4x4 matrix of rows: 2x2 blocks of lanes are exchanged, then 2x2 blocks of 64-bit halves.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Transpose4x4(ref Vector128<int> r0, ref Vector128<int> r1, ref Vector128<int> r2, ref Vector128<int> r3)
    {
        var odd = Vector128.Create(0, -1, 0, -1);
        var swapLanes = Vector128.Create(1, 0, 3, 2);
        var a0 = Vector128.ConditionalSelect(odd, Vector128.Shuffle(r1, swapLanes), r0);
        var a1 = Vector128.ConditionalSelect(odd, r1, Vector128.Shuffle(r0, swapLanes));
        var a2 = Vector128.ConditionalSelect(odd, Vector128.Shuffle(r3, swapLanes), r2);
        var a3 = Vector128.ConditionalSelect(odd, r3, Vector128.Shuffle(r2, swapLanes));
        var high = Vector128.Create(0L, -1L);
        var swapHalves = Vector128.Create(1L, 0L);
        r0 = Vector128.ConditionalSelect(high, Vector128.Shuffle(a2.AsInt64(), swapHalves), a0.AsInt64()).AsInt32();
        r2 = Vector128.ConditionalSelect(high, a2.AsInt64(), Vector128.Shuffle(a0.AsInt64(), swapHalves)).AsInt32();
        r1 = Vector128.ConditionalSelect(high, Vector128.Shuffle(a3.AsInt64(), swapHalves), a1.AsInt64()).AsInt32();
        r3 = Vector128.ConditionalSelect(high, a3.AsInt64(), Vector128.Shuffle(a1.AsInt64(), swapHalves)).AsInt32();
    }

    /// <summary>The scalar reference of <see cref="InverseDctAddFull"/>.</summary>
    internal static void InverseDctAddFullScalar(ReadOnlySpan<short> block, Span<byte> destination, int stride)
    {
        // Vertical pass (columns), then horizontal pass (rows) with rounding
        Span<int> temp = stackalloc int[16];
        for (var i = 0; i < 4; i++)
        {
            int i0 = block[i];
            int i1 = block[4 + i];
            int i2 = block[8 + i];
            int i3 = block[12 + i];
            var a1 = i0 + i2;
            var b1 = i0 - i2;
            var c1 = ((i1 * SinPi8Sqrt2) >> 16) - (i3 + ((i3 * CosPi8Sqrt2Minus1) >> 16));
            var d1 = (i1 + ((i1 * CosPi8Sqrt2Minus1) >> 16)) + ((i3 * SinPi8Sqrt2) >> 16);
            temp[i] = a1 + d1;
            temp[12 + i] = a1 - d1;
            temp[4 + i] = b1 + c1;
            temp[8 + i] = b1 - c1;
        }

        for (var y = 0; y < 4; y++)
        {
            var i0 = temp[4 * y];
            var i1 = temp[(4 * y) + 1];
            var i2 = temp[(4 * y) + 2];
            var i3 = temp[(4 * y) + 3];
            var a1 = i0 + i2;
            var b1 = i0 - i2;
            var c1 = ((i1 * SinPi8Sqrt2) >> 16) - (i3 + ((i3 * CosPi8Sqrt2Minus1) >> 16));
            var d1 = (i1 + ((i1 * CosPi8Sqrt2Minus1) >> 16)) + ((i3 * SinPi8Sqrt2) >> 16);
            var row = destination.Slice(y * stride, 4);
            row[0] = ClampByte(row[0] + ((a1 + d1 + 4) >> 3));
            row[3] = ClampByte(row[3] + ((a1 - d1 + 4) >> 3));
            row[1] = ClampByte(row[1] + ((b1 + c1 + 4) >> 3));
            row[2] = ClampByte(row[2] + ((b1 - c1 + 4) >> 3));
        }
    }

    public static byte ClampByte(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
}
