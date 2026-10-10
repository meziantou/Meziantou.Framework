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
            // Exact shortcut: with only a DC coefficient every output is (dc + 4) >> 3
            if (block[0] == 0)
                return;

            var dc = (block[0] + 4) >> 3;
            for (var y = 0; y < 4; y++)
            {
                var row = destination.Slice(y * stride, 4);
                for (var x = 0; x < 4; x++)
                {
                    row[x] = ClampByte(row[x] + dc);
                }
            }

            return;
        }

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
