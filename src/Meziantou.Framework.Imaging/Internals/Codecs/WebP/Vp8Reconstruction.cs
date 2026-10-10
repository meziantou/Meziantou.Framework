namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// VP8 intra prediction and residual reconstruction of one macroblock (RFC 6386 section 12), shared by the decoder and the
/// encoder's reconstruction loop: the 16x16 luma and 8x8 chroma modes (DC, V, H, TM) and the ten 4x4 sub-block modes.
/// </summary>
/// <remarks>
/// Pixels outside the frame are 127 above the first row (including the above-left corner of the first row) and 129 left of
/// the first column. DC prediction of whole macroblocks averages only the available edges (128 without any). Sub-blocks of
/// the right column read their above-right pixels from the macroblock row above (for every sub-block row); on the last
/// macroblock of a row they repeat the last above pixel, and on the first row they are 127.
/// </remarks>
internal static class Vp8Reconstruction
{
    /// <summary>Predicts the macroblock and adds the residuals of its 24 blocks (luma blocks 0-15, U 16-19, V 20-23).</summary>
    public static void ReconstructMacroblock(Vp8Planes planes, int mbX, int mbY, int mbWidth, int yMode, ReadOnlySpan<byte> subModes, int uvMode, ReadOnlySpan<short> coefficients)
    {
        var y = planes.Y;
        var yStride = planes.YStride;
        var yOffset = (mbY * 16 * yStride) + (mbX * 16);
        if (yMode == Vp8Tables.BPred)
        {
            Span<byte> aboveRight = stackalloc byte[4];
            GetMacroblockAboveRight(y, yStride, mbX, mbY, mbWidth, aboveRight);
            for (var by = 0; by < 4; by++)
            {
                for (var bx = 0; bx < 4; bx++)
                {
                    var offset = yOffset + (by * 4 * yStride) + (bx * 4);
                    PredictSubBlock(y, yStride, offset, mbX * 16 + bx * 4, mbY * 16 + by * 4, bx == 3 ? aboveRight : default, subModes[(by * 4) + bx]);
                    var index = (by * 4) + bx;
                    Vp8Transforms.InverseDctAdd(coefficients.Slice(index * 16, 16), y[offset..], yStride);
                }
            }
        }
        else
        {
            PredictBlock(y, yStride, yOffset, 16, mbX, mbY, yMode);
            for (var index = 0; index < 16; index++)
            {
                var offset = yOffset + ((index >> 2) * 4 * yStride) + ((index & 3) * 4);
                Vp8Transforms.InverseDctAdd(coefficients.Slice(index * 16, 16), y[offset..], yStride);
            }
        }

        var uvStride = planes.UVStride;
        var uvOffset = (mbY * 8 * uvStride) + (mbX * 8);
        for (var plane = 0; plane < 2; plane++)
        {
            var data = plane == 0 ? planes.U : planes.V;
            PredictBlock(data, uvStride, uvOffset, 8, mbX, mbY, uvMode);
            for (var block = 0; block < 4; block++)
            {
                var offset = uvOffset + ((block >> 1) * 4 * uvStride) + ((block & 1) * 4);
                var index = 16 + (plane * 4) + block;
                Vp8Transforms.InverseDctAdd(coefficients.Slice(index * 16, 16), data[offset..], uvStride);
            }
        }
    }

    /// <summary>
    /// Writes a whole-block prediction (16x16 luma or 8x8 chroma) at <paramref name="offset"/> from the reconstructed
    /// neighbors in <paramref name="plane"/>.
    /// </summary>
    public static void PredictBlock(Span<byte> plane, int stride, int offset, int size, int mbX, int mbY, int mode)
    {
        Span<byte> above = stackalloc byte[16];
        Span<byte> left = stackalloc byte[16];
        above = above[..size];
        left = left[..size];
        GetEdges(plane, stride, offset, size, mbX, mbY, above, left, out var aboveLeft);
        switch (mode)
        {
            case Vp8Tables.DcPred:
            {
                var shift = size == 16 ? 3 : 2;
                int value;
                if (mbY > 0 && mbX > 0)
                {
                    value = (Sum(above) + Sum(left) + size) >> (shift + 2);
                }
                else if (mbY > 0)
                {
                    value = (Sum(above) + (size >> 1)) >> (shift + 1);
                }
                else if (mbX > 0)
                {
                    value = (Sum(left) + (size >> 1)) >> (shift + 1);
                }
                else
                {
                    value = 128;
                }

                for (var y = 0; y < size; y++)
                {
                    plane.Slice(offset + (y * stride), size).Fill((byte)value);
                }

                break;
            }

            case Vp8Tables.VPred:
                for (var y = 0; y < size; y++)
                {
                    above.CopyTo(plane.Slice(offset + (y * stride), size));
                }

                break;

            case Vp8Tables.HPred:
                for (var y = 0; y < size; y++)
                {
                    plane.Slice(offset + (y * stride), size).Fill(left[y]);
                }

                break;

            default:
                for (var y = 0; y < size; y++)
                {
                    var row = plane.Slice(offset + (y * stride), size);
                    var delta = left[y] - aboveLeft;
                    for (var x = 0; x < size; x++)
                    {
                        row[x] = Vp8Transforms.ClampByte(above[x] + delta);
                    }
                }

                break;
        }
    }

    /// <summary>
    /// Gets the above-right pixels of the right sub-block column of a B_PRED macroblock: the four pixels after the macroblock
    /// on the row above it (127 on the first row; the last above pixel repeated on the last macroblock of a row).
    /// </summary>
    public static void GetMacroblockAboveRight(ReadOnlySpan<byte> plane, int stride, int mbX, int mbY, int mbWidth, Span<byte> aboveRight)
    {
        if (mbY == 0)
        {
            aboveRight.Fill(127);
            return;
        }

        var row = ((mbY * 16) - 1) * stride;
        if (mbX == mbWidth - 1)
        {
            aboveRight.Fill(plane[row + (mbX * 16) + 15]);
        }
        else
        {
            plane.Slice(row + (mbX * 16) + 16, 4).CopyTo(aboveRight);
        }
    }

    /// <summary>Writes the 4x4 prediction of one sub-block at <paramref name="offset"/> (pixel position <paramref name="px"/>, <paramref name="py"/> in the plane).</summary>
    /// <param name="plane">The luma plane.</param>
    /// <param name="stride">The plane row length.</param>
    /// <param name="offset">The offset of the sub-block's top-left pixel.</param>
    /// <param name="px">The column of the sub-block's top-left pixel.</param>
    /// <param name="py">The row of the sub-block's top-left pixel.</param>
    /// <param name="mode">The sub-block mode.</param>
    /// <param name="macroblockAboveRight">For the right sub-block column, the macroblock's above-right pixels; otherwise empty (the above-right pixels are read from the plane).</param>
    public static void PredictSubBlock(Span<byte> plane, int stride, int offset, int px, int py, ReadOnlySpan<byte> macroblockAboveRight, int mode)
    {
        // Edge: E[0..3] = L3..L0, E[4] = P (above-left), E[5..8] = A0..A3, E[9..12] = above-right
        Span<byte> e = stackalloc byte[13];
        for (var i = 0; i < 4; i++)
        {
            e[3 - i] = px > 0 ? plane[offset + (i * stride) - 1] : (byte)129;
        }

        e[4] = py > 0 ? (px > 0 ? plane[offset - stride - 1] : (byte)129) : (byte)127;
        for (var i = 0; i < 4; i++)
        {
            e[5 + i] = py > 0 ? plane[offset - stride + i] : (byte)127;
        }

        if (!macroblockAboveRight.IsEmpty)
        {
            macroblockAboveRight.CopyTo(e[9..]);
        }
        else
        {
            for (var i = 0; i < 4; i++)
            {
                e[9 + i] = py > 0 ? plane[offset - stride + 4 + i] : (byte)127;
            }
        }

        Span<byte> b = stackalloc byte[16];
        PredictSubBlock(e, mode, b);
        for (var y = 0; y < 4; y++)
        {
            b.Slice(y * 4, 4).CopyTo(plane.Slice(offset + (y * stride), 4));
        }
    }

    /// <summary>Computes a 4x4 sub-block prediction from its 13 edge pixels (left bottom-up, above-left, above, above-right).</summary>
    /// <param name="e">The edge pixels.</param>
    /// <param name="mode">The sub-block mode.</param>
    /// <param name="b">The 16 predicted pixels, raster order.</param>
    public static void PredictSubBlock(ReadOnlySpan<byte> e, int mode, Span<byte> b)
    {
        // Indexes into e: L0 = 3, L1 = 2, L2 = 1, L3 = 0, P = 4, A0 = 5 ... A7 = 12
        const int P = 4;
        switch (mode)
        {
            case Vp8Tables.BDcPred:
            {
                var sum = 4;
                for (var i = 0; i < 4; i++)
                {
                    sum += e[5 + i] + e[3 - i];
                }

                b.Fill((byte)(sum >> 3));
                break;
            }

            case Vp8Tables.BTmPred:
                for (var r = 0; r < 4; r++)
                {
                    for (var c = 0; c < 4; c++)
                    {
                        b[(r * 4) + c] = Vp8Transforms.ClampByte(e[3 - r] + e[5 + c] - e[P]);
                    }
                }

                break;

            case Vp8Tables.BVePred:
                for (var c = 0; c < 4; c++)
                {
                    var value = Avg3(e[4 + c], e[5 + c], e[6 + c]);
                    for (var r = 0; r < 4; r++)
                    {
                        b[(r * 4) + c] = value;
                    }
                }

                break;

            case Vp8Tables.BHePred:
            {
                // Rows: avg3(P, L0, L1), avg3(L0, L1, L2), avg3(L1, L2, L3), avg3(L2, L3, L3)
                b[..4].Fill(Avg3(e[P], e[3], e[2]));
                b.Slice(4, 4).Fill(Avg3(e[3], e[2], e[1]));
                b.Slice(8, 4).Fill(Avg3(e[2], e[1], e[0]));
                b.Slice(12, 4).Fill(Avg3(e[1], e[0], e[0]));

                break;
            }

            case Vp8Tables.BLdPred:
                // Down-left: A[r + c], A[r + c + 1], A[r + c + 2] with A = e[5..12]; the last one repeats A7
                for (var r = 0; r < 4; r++)
                {
                    for (var c = 0; c < 4; c++)
                    {
                        var i = r + c;
                        b[(r * 4) + c] = i == 6 ? Avg3(e[11], e[12], e[12]) : Avg3(e[5 + i], e[6 + i], e[7 + i]);
                    }
                }

                break;

            case Vp8Tables.BRdPred:
                // Down-right along the edge E[0..8] = L3, L2, L1, L0, P, A0, A1, A2, A3
                for (var r = 0; r < 4; r++)
                {
                    for (var c = 0; c < 4; c++)
                    {
                        var i = 4 - r + c;
                        b[(r * 4) + c] = Avg3(e[i - 1], e[i], e[i + 1]);
                    }
                }

                break;

            case Vp8Tables.BVrPred:
                Set(b, 3, 0, Avg3(e[1], e[2], e[3]));
                Set(b, 2, 0, Avg3(e[2], e[3], e[4]));
                Set(b, 3, 1, Avg3(e[3], e[4], e[5]));
                Set(b, 1, 0, Avg3(e[3], e[4], e[5]));
                Set(b, 2, 1, Avg2(e[4], e[5]));
                Set(b, 0, 0, Avg2(e[4], e[5]));
                Set(b, 3, 2, Avg3(e[4], e[5], e[6]));
                Set(b, 1, 1, Avg3(e[4], e[5], e[6]));
                Set(b, 2, 2, Avg2(e[5], e[6]));
                Set(b, 0, 1, Avg2(e[5], e[6]));
                Set(b, 3, 3, Avg3(e[5], e[6], e[7]));
                Set(b, 1, 2, Avg3(e[5], e[6], e[7]));
                Set(b, 2, 3, Avg2(e[6], e[7]));
                Set(b, 0, 2, Avg2(e[6], e[7]));
                Set(b, 1, 3, Avg3(e[6], e[7], e[8]));
                Set(b, 0, 3, Avg2(e[7], e[8]));
                break;

            case Vp8Tables.BVlPred:
                // A0..A7 = e[5..12]
                Set(b, 0, 0, Avg2(e[5], e[6]));
                Set(b, 1, 0, Avg3(e[5], e[6], e[7]));
                Set(b, 2, 0, Avg2(e[6], e[7]));
                Set(b, 0, 1, Avg2(e[6], e[7]));
                Set(b, 1, 1, Avg3(e[6], e[7], e[8]));
                Set(b, 3, 0, Avg3(e[6], e[7], e[8]));
                Set(b, 2, 1, Avg2(e[7], e[8]));
                Set(b, 0, 2, Avg2(e[7], e[8]));
                Set(b, 3, 1, Avg3(e[7], e[8], e[9]));
                Set(b, 1, 2, Avg3(e[7], e[8], e[9]));
                Set(b, 2, 2, Avg2(e[8], e[9]));
                Set(b, 0, 3, Avg2(e[8], e[9]));
                Set(b, 3, 2, Avg3(e[8], e[9], e[10]));
                Set(b, 1, 3, Avg3(e[8], e[9], e[10]));

                // The last two values do not follow the pattern
                Set(b, 2, 3, Avg3(e[9], e[10], e[11]));
                Set(b, 3, 3, Avg3(e[10], e[11], e[12]));
                break;

            case Vp8Tables.BHdPred:
                Set(b, 3, 0, Avg2(e[0], e[1]));
                Set(b, 3, 1, Avg3(e[0], e[1], e[2]));
                Set(b, 2, 0, Avg2(e[1], e[2]));
                Set(b, 3, 2, Avg2(e[1], e[2]));
                Set(b, 2, 1, Avg3(e[1], e[2], e[3]));
                Set(b, 3, 3, Avg3(e[1], e[2], e[3]));
                Set(b, 2, 2, Avg2(e[2], e[3]));
                Set(b, 1, 0, Avg2(e[2], e[3]));
                Set(b, 2, 3, Avg3(e[2], e[3], e[4]));
                Set(b, 1, 1, Avg3(e[2], e[3], e[4]));
                Set(b, 1, 2, Avg2(e[3], e[4]));
                Set(b, 0, 0, Avg2(e[3], e[4]));
                Set(b, 1, 3, Avg3(e[3], e[4], e[5]));
                Set(b, 0, 1, Avg3(e[3], e[4], e[5]));
                Set(b, 0, 2, Avg3(e[4], e[5], e[6]));
                Set(b, 0, 3, Avg3(e[5], e[6], e[7]));
                break;

            default: // B_HU_PRED: L0..L3 = e[3], e[2], e[1], e[0]
                Set(b, 0, 0, Avg2(e[3], e[2]));
                Set(b, 0, 1, Avg3(e[3], e[2], e[1]));
                Set(b, 0, 2, Avg2(e[2], e[1]));
                Set(b, 1, 0, Avg2(e[2], e[1]));
                Set(b, 0, 3, Avg3(e[2], e[1], e[0]));
                Set(b, 1, 1, Avg3(e[2], e[1], e[0]));
                Set(b, 1, 2, Avg2(e[1], e[0]));
                Set(b, 2, 0, Avg2(e[1], e[0]));
                Set(b, 1, 3, Avg3(e[1], e[0], e[0]));
                Set(b, 2, 1, Avg3(e[1], e[0], e[0]));
                Set(b, 2, 2, e[0]);
                Set(b, 2, 3, e[0]);
                Set(b, 3, 0, e[0]);
                Set(b, 3, 1, e[0]);
                Set(b, 3, 2, e[0]);
                Set(b, 3, 3, e[0]);
                break;
        }
    }

    private static void Set(Span<byte> b, int row, int column, byte value) => b[(row * 4) + column] = value;

    private static byte Avg2(int a, int b) => (byte)((a + b + 1) >> 1);

    private static byte Avg3(int a, int b, int c) => (byte)((a + (2 * b) + c + 2) >> 2);

    private static int Sum(ReadOnlySpan<byte> values)
    {
        var sum = 0;
        foreach (var value in values)
        {
            sum += value;
        }

        return sum;
    }

    private static void GetEdges(ReadOnlySpan<byte> plane, int stride, int offset, int size, int mbX, int mbY, Span<byte> above, Span<byte> left, out int aboveLeft)
    {
        if (mbY > 0)
        {
            plane.Slice(offset - stride, size).CopyTo(above);
        }
        else
        {
            above.Fill(127);
        }

        if (mbX > 0)
        {
            for (var i = 0; i < size; i++)
            {
                left[i] = plane[offset + (i * stride) - 1];
            }
        }
        else
        {
            left.Fill(129);
        }

        aboveLeft = mbY == 0 ? 127 : mbX == 0 ? 129 : plane[offset - stride - 1];
    }
}
