namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The loop restoration filters of AV1 (AV1 specification, sections 7.17.2 to 7.17.5): the Wiener filter and the self-guided
/// filter of a block of samples. The filters read the frame before restoration from a copy of the stripe being restored, padded
/// with the rows and columns that the get source sample process (section 7.17.6) selects around it.
/// </summary>
internal static class Av1LoopRestoration
{
    /// <summary>The number of rows and columns of padding of the source on each side of a stripe.</summary>
    public const int Padding = 3;

    /// <summary>The largest width of a block filtered in one call.</summary>
    public const int MaxBlockWidth = 64;

    /// <summary>The largest height of a block filtered in one call: the height of a stripe.</summary>
    public const int MaxBlockHeight = 64;

    /// <summary>The number of entries of the scratch of the filters.</summary>
    public const int ScratchLength = (2 * (MaxBlockHeight + 2) * (MaxBlockWidth + 2)) + (2 * MaxBlockHeight * MaxBlockWidth);

    /// <summary>The Wiener filter process (section 7.17.4).</summary>
    /// <param name="source">The padded source: the sample (0, 0) of the block is at <paramref name="sourceOffset"/>.</param>
    /// <param name="sourceStride">The number of samples of a row of <paramref name="source"/>.</param>
    /// <param name="sourceOffset">The position of the top left sample of the block in <paramref name="source"/>.</param>
    /// <param name="destination">The restored block.</param>
    /// <param name="destinationStride">The number of samples of a row of <paramref name="destination"/>.</param>
    /// <param name="w">The width of the block.</param>
    /// <param name="h">The height of the block.</param>
    /// <param name="unit">The parameters of the loop restoration unit.</param>
    /// <param name="bitDepth">The bit depth of the samples.</param>
    /// <param name="scratch">Scratch of <see cref="ScratchLength"/> entries.</param>
    public static void ApplyWiener(ReadOnlySpan<ushort> source, int sourceStride, int sourceOffset, Span<ushort> destination, int destinationStride, int w, int h, ReadOnlySpan<sbyte> unit, int bitDepth, Span<int> scratch)
    {
        // Section 7.11.3.2 with isCompound equal to 0
        var interRound0 = bitDepth is 12 ? 5 : 3;
        var interRound1 = bitDepth is 12 ? 9 : 11;
        Span<int> vfilter = stackalloc int[7];
        Span<int> hfilter = stackalloc int[7];
        GetWienerCoefficients(unit.Slice(1, 3), vfilter);
        GetWienerCoefficients(unit.Slice(4, 3), hfilter);

        var offset = 1 << (bitDepth + Av1Constants.FilterBits - interRound0 - 1);
        var limit = (1 << (bitDepth + 1 + Av1Constants.FilterBits - interRound0)) - 1;
        var intermediate = scratch[..((h + 6) * w)];
        for (var r = 0; r < h + 6; r++)
        {
            var row = source[(sourceOffset + ((r - 3) * sourceStride) - 3)..];
            for (var c = 0; c < w; c++)
            {
                var s = 0;
                for (var t = 0; t < 7; t++)
                {
                    s += hfilter[t] * row[c + t];
                }

                var v = (s + (1 << (interRound0 - 1))) >> interRound0;
                intermediate[(r * w) + c] = Math.Clamp(v, -offset, limit - offset);
            }
        }

        var maxSample = (1 << bitDepth) - 1;
        for (var r = 0; r < h; r++)
        {
            var row = destination.Slice(r * destinationStride, w);
            for (var c = 0; c < w; c++)
            {
                var s = 0;
                for (var t = 0; t < 7; t++)
                {
                    s += vfilter[t] * intermediate[((r + t) * w) + c];
                }

                row[c] = (ushort)Math.Clamp((s + (1 << (interRound1 - 1))) >> interRound1, 0, maxSample);
            }
        }
    }

    // Section 7.17.5 "Wiener coefficient process"
    private static void GetWienerCoefficients(ReadOnlySpan<sbyte> coeff, Span<int> filter)
    {
        filter[3] = 128;
        for (var i = 0; i < 3; i++)
        {
            int c = coeff[i];
            filter[i] = c;
            filter[6 - i] = c;
            filter[3] -= 2 * c;
        }
    }

    /// <summary>The self-guided filter process (section 7.17.2). The parameters are those of <see cref="ApplyWiener"/>.</summary>
    public static void ApplySelfGuided(ReadOnlySpan<ushort> source, int sourceStride, int sourceOffset, Span<ushort> destination, int destinationStride, int w, int h, ReadOnlySpan<sbyte> unit, int bitDepth, Span<int> scratch)
    {
        int set = unit[7];
        var boxLength = (h + 2) * (w + 2);
        var a = scratch[..boxLength];
        var b = scratch.Slice(boxLength, boxLength);
        var flt0 = scratch.Slice(2 * (MaxBlockHeight + 2) * (MaxBlockWidth + 2), w * h);
        var flt1 = scratch.Slice((2 * (MaxBlockHeight + 2) * (MaxBlockWidth + 2)) + (MaxBlockHeight * MaxBlockWidth), w * h);
        int r0 = Av1Tables.SgrParams[set * 4];
        int r1 = Av1Tables.SgrParams[(set * 4) + 2];
        if (r0 is not 0)
            BoxFilter(source, sourceStride, sourceOffset, w, h, set, pass: 0, bitDepth, a, b, flt0);

        if (r1 is not 0)
            BoxFilter(source, sourceStride, sourceOffset, w, h, set, pass: 1, bitDepth, a, b, flt1);

        int w0 = unit[8];
        int w1 = unit[9];
        var w2 = (1 << Av1Constants.SgrprojPrjBits) - w0 - w1;
        var maxSample = (1 << bitDepth) - 1;
        const int Shift = Av1Constants.SgrprojRstBits + Av1Constants.SgrprojPrjBits;
        for (var i = 0; i < h; i++)
        {
            var row = destination.Slice(i * destinationStride, w);
            for (var j = 0; j < w; j++)
            {
                var u = source[sourceOffset + (i * sourceStride) + j] << Av1Constants.SgrprojRstBits;
                var v = w1 * u;
                v += w0 * (r0 is not 0 ? flt0[(i * w) + j] : u);
                v += w2 * (r1 is not 0 ? flt1[(i * w) + j] : u);
                row[j] = (ushort)Math.Clamp((v + (1 << (Shift - 1))) >> Shift, 0, maxSample);
            }
        }
    }

    // Section 7.17.3 "Box filter process": A and B are valid for the coordinates -1 to h and -1 to w
    private static void BoxFilter(ReadOnlySpan<ushort> source, int sourceStride, int sourceOffset, int w, int h, int set, int pass, int bitDepth, Span<int> a, Span<int> b, Span<int> output)
    {
        int r = Av1Tables.SgrParams[(set * 4) + (pass * 2)];
        int eps = Av1Tables.SgrParams[(set * 4) + (pass * 2) + 1];
        var n = ((2 * r) + 1) * ((2 * r) + 1);
        var n2e = n * n * eps;
        var s = ((1 << Av1Constants.SgrprojMtableBits) + (n2e / 2)) / n2e;
        var oneOverN = ((1 << Av1Constants.SgrprojRecipBits) + (n / 2)) / n;
        var boxStride = w + 2;
        var depthShift = bitDepth - 8;
        for (var i = -1; i < h + 1; i++)
        {
            for (var j = -1; j < w + 1; j++)
            {
                long sumSquares = 0;
                long sum = 0;
                for (var dy = -r; dy <= r; dy++)
                {
                    var row = source[(sourceOffset + ((i + dy) * sourceStride) + j - r)..];
                    for (var dx = 0; dx <= 2 * r; dx++)
                    {
                        long c = row[dx];
                        sumSquares += c * c;
                        sum += c;
                    }
                }

                var aa = Round2(sumSquares, 2 * depthShift);
                var d = Round2(sum, depthShift);
                var p = Math.Max(0, (aa * n) - (d * d));
                var z = Round2(p * s, Av1Constants.SgrprojMtableBits);
                long a2;
                if (z >= 255)
                {
                    a2 = 256;
                }
                else if (z is 0)
                {
                    a2 = 1;
                }
                else
                {
                    a2 = ((z << Av1Constants.SgrprojSgrBits) + (z / 2)) / (z + 1);
                }

                var b2 = ((1 << Av1Constants.SgrprojSgrBits) - a2) * sum * oneOverN;
                var index = ((i + 1) * boxStride) + j + 1;
                a[index] = (int)a2;
                b[index] = (int)Round2(b2, Av1Constants.SgrprojRecipBits);
            }
        }

        for (var i = 0; i < h; i++)
        {
            var shift = pass is 0 && (i & 1) is not 0 ? 4 : 5;
            for (var j = 0; j < w; j++)
            {
                var sumA = 0;
                var sumB = 0;
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        int weight;
                        if (pass is 0)
                        {
                            if (((i + dy) & 1) is not 0)
                            {
                                weight = dx is 0 ? 6 : 5;
                            }
                            else
                            {
                                weight = 0;
                            }
                        }
                        else
                        {
                            weight = dx is 0 || dy is 0 ? 4 : 3;
                        }

                        var index = ((i + dy + 1) * boxStride) + j + dx + 1;
                        sumA += weight * a[index];
                        sumB += weight * b[index];
                    }
                }

                var v = (sumA * source[sourceOffset + (i * sourceStride) + j]) + sumB;
                var bits = Av1Constants.SgrprojSgrBits + shift - Av1Constants.SgrprojRstBits;
                output[(i * w) + j] = (v + (1 << (bits - 1))) >> bits;
            }
        }
    }

    private static long Round2(long value, int bits) => bits is 0 ? value : (value + (1L << (bits - 1))) >> bits;
}
