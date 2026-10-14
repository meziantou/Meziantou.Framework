using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The loop restoration filters of AV1 (AV1 specification, sections 7.17.2 to 7.17.5): the Wiener filter and the self-guided
/// filter of a block of samples. The filters read the frame before restoration from a copy of the stripe being restored, padded
/// with the rows and columns that the get source sample process (section 7.17.6) selects around it.
/// The vector paths process 8 samples of a row at a time with the 32-bit arithmetic of their scalar references
/// (<see cref="ApplyWienerScalar"/>, <see cref="ApplySelfGuidedScalar"/>), which they equal bit for bit.
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

    // The number of samples of a row processed at a time by the vector paths
    private const int VectorWidth = 8;

    // a2 of the box filter process for the values 0 to 254 of z (section 7.17.3), instead of a division per box
    private static readonly byte[] SgrA2 = CreateSgrA2();

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
        if (Vector128.IsHardwareAccelerated && w >= VectorWidth)
        {
            ApplyWienerVector(source, sourceStride, sourceOffset, destination, destinationStride, w, h, unit, bitDepth, scratch);
        }
        else
        {
            ApplyWienerScalar(source, sourceStride, sourceOffset, destination, destinationStride, w, h, unit, bitDepth, scratch);
        }
    }

    /// <summary>The Wiener filter process, one sample at a time: the reference of the vector path.</summary>
    internal static void ApplyWienerScalar(ReadOnlySpan<ushort> source, int sourceStride, int sourceOffset, Span<ushort> destination, int destinationStride, int w, int h, ReadOnlySpan<sbyte> unit, int bitDepth, Span<int> scratch)
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
        if (Vector128.IsHardwareAccelerated && w >= VectorWidth)
        {
            ApplySelfGuidedVector(source, sourceStride, sourceOffset, destination, destinationStride, w, h, unit, bitDepth, scratch);
        }
        else
        {
            ApplySelfGuidedScalar(source, sourceStride, sourceOffset, destination, destinationStride, w, h, unit, bitDepth, scratch);
        }
    }

    /// <summary>The self-guided filter process, one sample at a time: the reference of the vector path.</summary>
    internal static void ApplySelfGuidedScalar(ReadOnlySpan<ushort> source, int sourceStride, int sourceOffset, Span<ushort> destination, int destinationStride, int w, int h, ReadOnlySpan<sbyte> unit, int bitDepth, Span<int> scratch)
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
            BoxFilterScalar(source, sourceStride, sourceOffset, w, h, set, pass: 0, bitDepth, a, b, flt0);

        if (r1 is not 0)
            BoxFilterScalar(source, sourceStride, sourceOffset, w, h, set, pass: 1, bitDepth, a, b, flt1);

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
    private static void BoxFilterScalar(ReadOnlySpan<ushort> source, int sourceStride, int sourceOffset, int w, int h, int set, int pass, int bitDepth, Span<int> a, Span<int> b, Span<int> output)
    {
        int r = Av1Tables.SgrParams[(set * 4) + (pass * 2)];
        int eps = Av1Tables.SgrParams[(set * 4) + (pass * 2) + 1];
        var n = ((2 * r) + 1) * ((2 * r) + 1);
        var n2e = n * n * eps;
        var s = ((1 << Av1Constants.SgrprojMtableBits) + (n2e / 2)) / n2e;
        var oneOverN = ((1 << Av1Constants.SgrprojRecipBits) + (n / 2)) / n;
        var boxStride = w + 2;
        var depthShift = bitDepth - 8;

        // The sums of the samples and of their squares over the 2r + 1 rows of a box, for each column from -1 - r to w + r.
        // A box is the sum of 2r + 1 consecutive columns: from one box to the next, a column leaves and another one enters.
        // A sum of squares of 12-bit samples over 25 samples is below 2^29
        var diameter = (2 * r) + 1;
        var columnCount = w + 2 + (2 * r);
        Span<int> columnSums = stackalloc int[MaxBlockWidth + 2 + 4];
        Span<int> columnSquares = stackalloc int[MaxBlockWidth + 2 + 4];
        columnSums = columnSums[..columnCount];
        columnSquares = columnSquares[..columnCount];
        for (var i = -1; i < h + 1; i++)
        {
            // The first pass only uses the odd rows of A and B (the other rows have a weight of zero)
            if (pass is 0 && (i & 1) is 0)
                continue;

            columnSums.Clear();
            columnSquares.Clear();
            for (var dy = -r; dy <= r; dy++)
            {
                var row = source.Slice(sourceOffset + ((i + dy) * sourceStride) - 1 - r, columnCount);
                for (var k = 0; k < row.Length; k++)
                {
                    int c = row[k];
                    columnSums[k] += c;
                    columnSquares[k] += c * c;
                }
            }

            long sumSquares = 0;
            long sum = 0;
            for (var k = 0; k < diameter - 1; k++)
            {
                sumSquares += columnSquares[k];
                sum += columnSums[k];
            }

            for (var j = -1; j < w + 1; j++)
            {
                // The box of column j: the columns j - r to j + r, at the indexes j + 1 to j + 1 + 2r
                sumSquares += columnSquares[j + diameter];
                sum += columnSums[j + diameter];
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
                sumSquares -= columnSquares[j + 1];
                sum -= columnSums[j + 1];
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

    private static void ApplyWienerVector(ReadOnlySpan<ushort> source, int sourceStride, int sourceOffset, Span<ushort> destination, int destinationStride, int w, int h, ReadOnlySpan<sbyte> unit, int bitDepth, Span<int> scratch)
    {
        var interRound0 = bitDepth is 12 ? 5 : 3;
        var interRound1 = bitDepth is 12 ? 9 : 11;
        Span<int> vfilter = stackalloc int[7];
        Span<int> hfilter = stackalloc int[7];
        GetWienerCoefficients(unit.Slice(1, 3), vfilter);
        GetWienerCoefficients(unit.Slice(4, 3), hfilter);

        var offset = 1 << (bitDepth + Av1Constants.FilterBits - interRound0 - 1);
        var limit = (1 << (bitDepth + 1 + Av1Constants.FilterBits - interRound0)) - 1;
        var vectorEnd = w - (w % VectorWidth);
        var intermediate = scratch[..((h + 6) * w)];
        var round0 = Vector128.Create(1 << (interRound0 - 1));
        var min0 = Vector128.Create(-offset);
        var max0 = Vector128.Create(limit - offset);
        for (var r = 0; r < h + 6; r++)
        {
            var row = source[(sourceOffset + ((r - 3) * sourceStride) - 3)..];
            var output = intermediate.Slice(r * w, w);
            for (var c = 0; c < vectorEnd; c += VectorWidth)
            {
                var low = Vector128<int>.Zero;
                var high = Vector128<int>.Zero;
                for (var t = 0; t < 7; t++)
                {
                    var samples = Vector128.Create(row.Slice(c + t, VectorWidth));
                    var tap = Vector128.Create(hfilter[t]);
                    low += Vector128.WidenLower(samples).AsInt32() * tap;
                    high += Vector128.WidenUpper(samples).AsInt32() * tap;
                }

                Vector128.Min(Vector128.Max((low + round0) >> interRound0, min0), max0).CopyTo(output[c..]);
                Vector128.Min(Vector128.Max((high + round0) >> interRound0, min0), max0).CopyTo(output[(c + 4)..]);
            }

            for (var c = vectorEnd; c < w; c++)
            {
                var s = 0;
                for (var t = 0; t < 7; t++)
                {
                    s += hfilter[t] * row[c + t];
                }

                var v = (s + (1 << (interRound0 - 1))) >> interRound0;
                output[c] = Math.Clamp(v, -offset, limit - offset);
            }
        }

        var maxSample = (1 << bitDepth) - 1;
        var round1 = Vector128.Create(1 << (interRound1 - 1));
        var max1 = Vector128.Create(maxSample);
        for (var r = 0; r < h; r++)
        {
            var row = destination.Slice(r * destinationStride, w);
            for (var c = 0; c < vectorEnd; c += VectorWidth)
            {
                var low = Vector128<int>.Zero;
                var high = Vector128<int>.Zero;
                for (var t = 0; t < 7; t++)
                {
                    var line = intermediate.Slice(((r + t) * w) + c, VectorWidth);
                    var tap = Vector128.Create(vfilter[t]);
                    low += Vector128.Create(line) * tap;
                    high += Vector128.Create(line[4..]) * tap;
                }

                low = Vector128.Min(Vector128.Max((low + round1) >> interRound1, Vector128<int>.Zero), max1);
                high = Vector128.Min(Vector128.Max((high + round1) >> interRound1, Vector128<int>.Zero), max1);
                Vector128.Narrow(low.AsUInt32(), high.AsUInt32()).CopyTo(row[c..]);
            }

            for (var c = vectorEnd; c < w; c++)
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

    private static void ApplySelfGuidedVector(ReadOnlySpan<ushort> source, int sourceStride, int sourceOffset, Span<ushort> destination, int destinationStride, int w, int h, ReadOnlySpan<sbyte> unit, int bitDepth, Span<int> scratch)
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
            BoxFilterVector(source, sourceStride, sourceOffset, w, h, set, pass: 0, bitDepth, a, b, flt0);

        if (r1 is not 0)
            BoxFilterVector(source, sourceStride, sourceOffset, w, h, set, pass: 1, bitDepth, a, b, flt1);

        int w0 = unit[8];
        int w1 = unit[9];
        var w2 = (1 << Av1Constants.SgrprojPrjBits) - w0 - w1;
        var maxSample = (1 << bitDepth) - 1;
        const int Shift = Av1Constants.SgrprojRstBits + Av1Constants.SgrprojPrjBits;
        var vectorEnd = w - (w % VectorWidth);
        var weight0 = Vector128.Create(w0);
        var weight1 = Vector128.Create(w1);
        var weight2 = Vector128.Create(w2);
        var round = Vector128.Create(1 << (Shift - 1));
        var max = Vector128.Create(maxSample);
        for (var i = 0; i < h; i++)
        {
            var samples = source.Slice(sourceOffset + (i * sourceStride), w);
            var row = destination.Slice(i * destinationStride, w);
            for (var j = 0; j < vectorEnd; j += VectorWidth)
            {
                var input = Vector128.Create(samples.Slice(j, VectorWidth));
                var index = (i * w) + j;
                var low = Combine(Vector128.WidenLower(input).AsInt32(), r0 is not 0 ? flt0.Slice(index, 4) : default, r1 is not 0 ? flt1.Slice(index, 4) : default, weight0, weight1, weight2, round, max);
                var high = Combine(Vector128.WidenUpper(input).AsInt32(), r0 is not 0 ? flt0.Slice(index + 4, 4) : default, r1 is not 0 ? flt1.Slice(index + 4, 4) : default, weight0, weight1, weight2, round, max);
                Vector128.Narrow(low.AsUInt32(), high.AsUInt32()).CopyTo(row[j..]);
            }

            for (var j = vectorEnd; j < w; j++)
            {
                var u = samples[j] << Av1Constants.SgrprojRstBits;
                var v = w1 * u;
                v += w0 * (r0 is not 0 ? flt0[(i * w) + j] : u);
                v += w2 * (r1 is not 0 ? flt1[(i * w) + j] : u);
                row[j] = (ushort)Math.Clamp((v + (1 << (Shift - 1))) >> Shift, 0, maxSample);
            }
        }
    }

    // The projection of section 7.17.2 for 4 samples: the filtered values of a pass that is not used are empty (the sample)
    private static Vector128<int> Combine(Vector128<int> sample, ReadOnlySpan<int> filtered0, ReadOnlySpan<int> filtered1, Vector128<int> weight0, Vector128<int> weight1, Vector128<int> weight2, Vector128<int> round, Vector128<int> max)
    {
        const int Shift = Av1Constants.SgrprojRstBits + Av1Constants.SgrprojPrjBits;
        var u = sample << Av1Constants.SgrprojRstBits;
        var v = weight1 * u;
        v += weight0 * (filtered0.IsEmpty ? u : Vector128.Create(filtered0));
        v += weight2 * (filtered1.IsEmpty ? u : Vector128.Create(filtered1));
        return Vector128.Min(Vector128.Max((v + round) >> Shift, Vector128<int>.Zero), max);
    }

    // The box filter process of BoxFilterScalar: the column sums are added 8 columns at a time, a2 comes from a table, and the
    // weights of the 3x3 neighborhoods are applied to 8 samples at a time
    private static void BoxFilterVector(ReadOnlySpan<ushort> source, int sourceStride, int sourceOffset, int w, int h, int set, int pass, int bitDepth, Span<int> a, Span<int> b, Span<int> output)
    {
        int r = Av1Tables.SgrParams[(set * 4) + (pass * 2)];
        int eps = Av1Tables.SgrParams[(set * 4) + (pass * 2) + 1];
        var n = ((2 * r) + 1) * ((2 * r) + 1);
        var n2e = n * n * eps;
        var s = ((1 << Av1Constants.SgrprojMtableBits) + (n2e / 2)) / n2e;
        var oneOverN = ((1 << Av1Constants.SgrprojRecipBits) + (n / 2)) / n;
        var boxStride = w + 2;
        var depthShift = bitDepth - 8;
        var diameter = (2 * r) + 1;
        var columnCount = w + 2 + (2 * r);
        Span<int> columnSums = stackalloc int[MaxBlockWidth + 2 + 4];
        Span<int> columnSquares = stackalloc int[MaxBlockWidth + 2 + 4];
        columnSums = columnSums[..columnCount];
        columnSquares = columnSquares[..columnCount];
        var columnVectorEnd = columnCount - (columnCount % VectorWidth);
        for (var i = -1; i < h + 1; i++)
        {
            // The first pass only uses the odd rows of A and B (the other rows have a weight of zero)
            if (pass is 0 && (i & 1) is 0)
                continue;

            for (var dy = -r; dy <= r; dy++)
            {
                // The first row of the box sets the sums, the next ones add to them
                var row = source.Slice(sourceOffset + ((i + dy) * sourceStride) - 1 - r, columnCount);
                var first = dy == -r;
                for (var k = 0; k < columnVectorEnd; k += VectorWidth)
                {
                    var samples = Vector128.Create(row.Slice(k, VectorWidth));
                    var low = Vector128.WidenLower(samples).AsInt32();
                    var high = Vector128.WidenUpper(samples).AsInt32();
                    var sums = columnSums.Slice(k, VectorWidth);
                    var squares = columnSquares.Slice(k, VectorWidth);
                    if (first)
                    {
                        low.CopyTo(sums);
                        high.CopyTo(sums[4..]);
                        (low * low).CopyTo(squares);
                        (high * high).CopyTo(squares[4..]);
                    }
                    else
                    {
                        (Vector128.Create(sums) + low).CopyTo(sums);
                        (Vector128.Create(sums[4..]) + high).CopyTo(sums[4..]);
                        (Vector128.Create(squares) + (low * low)).CopyTo(squares);
                        (Vector128.Create(squares[4..]) + (high * high)).CopyTo(squares[4..]);
                    }
                }

                for (var k = columnVectorEnd; k < columnCount; k++)
                {
                    int c = row[k];
                    columnSums[k] = first ? c : columnSums[k] + c;
                    columnSquares[k] = first ? c * c : columnSquares[k] + (c * c);
                }
            }

            long sumSquares = 0;
            long sum = 0;
            for (var k = 0; k < diameter - 1; k++)
            {
                sumSquares += columnSquares[k];
                sum += columnSums[k];
            }

            var aRow = a.Slice((i + 1) * boxStride, boxStride);
            var bRow = b.Slice((i + 1) * boxStride, boxStride);
            for (var j = -1; j < w + 1; j++)
            {
                sumSquares += columnSquares[j + diameter];
                sum += columnSums[j + diameter];
                var aa = Round2(sumSquares, 2 * depthShift);
                var d = Round2(sum, depthShift);
                var p = Math.Max(0, (aa * n) - (d * d));
                var z = Round2(p * s, Av1Constants.SgrprojMtableBits);
                long a2 = z >= 255 ? 256 : SgrA2[(int)z];
                var b2 = ((1 << Av1Constants.SgrprojSgrBits) - a2) * sum * oneOverN;
                aRow[j + 1] = (int)a2;
                bRow[j + 1] = (int)Round2(b2, Av1Constants.SgrprojRecipBits);
                sumSquares -= columnSquares[j + 1];
                sum -= columnSums[j + 1];
            }
        }

        // The weighted sums of section 7.17.3 over the 3x3 neighborhood of a sample, whose rows are the rows i, i + 1 and i + 2
        // of A and B (the box rows start at -1). The first pass weights its odd rows only, 6 in the middle column and 5 on its
        // sides, and normalizes by 2^4 on an odd row (one weighted row) and 2^5 on an even one (two); the second pass weights the
        // middle row and column 4 and the corners 3, and normalizes by 2^5.
        var vectorEnd = w - (w % VectorWidth);
        for (var i = 0; i < h; i++)
        {
            var shift = pass is 0 && (i & 1) is not 0 ? 4 : 5;
            var bits = Av1Constants.SgrprojSgrBits + shift - Av1Constants.SgrprojRstBits;
            var round = Vector128.Create(1 << (bits - 1));
            var samples = source.Slice(sourceOffset + (i * sourceStride), w);
            var outputRow = output.Slice(i * w, w);
            var top = i * boxStride;
            var middle = (i + 1) * boxStride;
            var bottom = (i + 2) * boxStride;
            for (var j = 0; j < vectorEnd; j += VectorWidth)
            {
                var input = Vector128.Create(samples.Slice(j, VectorWidth));
                Neighborhood(a, b, pass, i, top + j, middle + j, bottom + j, Vector128.WidenLower(input).AsInt32(), round, bits).CopyTo(outputRow[j..]);
                Neighborhood(a, b, pass, i, top + j + 4, middle + j + 4, bottom + j + 4, Vector128.WidenUpper(input).AsInt32(), round, bits).CopyTo(outputRow[(j + 4)..]);
            }

            for (var j = vectorEnd; j < w; j++)
            {
                var sumA = 0;
                var sumB = 0;
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var weight = GetWeight(pass, i, dy, dx);
                        var index = ((i + dy + 1) * boxStride) + j + dx + 1;
                        sumA += weight * a[index];
                        sumB += weight * b[index];
                    }
                }

                var v = (sumA * samples[j]) + sumB;
                outputRow[j] = (v + (1 << (bits - 1))) >> bits;
            }
        }
    }

    // The filtered values of 4 samples of row i from their 3x3 neighborhoods: top, middle and bottom are the index of the
    // column before the first sample in the rows i - 1, i and i + 1 of A and B
    private static Vector128<int> Neighborhood(ReadOnlySpan<int> a, ReadOnlySpan<int> b, int pass, int i, int top, int middle, int bottom, Vector128<int> sample, Vector128<int> round, int bits)
    {
        Vector128<int> sumA;
        Vector128<int> sumB;
        if (pass is 0)
        {
            if ((i & 1) is not 0)
            {
                sumA = Weigh(a, middle, 5, 6);
                sumB = Weigh(b, middle, 5, 6);
            }
            else
            {
                sumA = Weigh(a, top, 5, 6) + Weigh(a, bottom, 5, 6);
                sumB = Weigh(b, top, 5, 6) + Weigh(b, bottom, 5, 6);
            }
        }
        else
        {
            sumA = Weigh(a, top, 3, 4) + Weigh(a, middle, 4, 4) + Weigh(a, bottom, 3, 4);
            sumB = Weigh(b, top, 3, 4) + Weigh(b, middle, 4, 4) + Weigh(b, bottom, 3, 4);
        }

        return ((sumA * sample) + sumB + round) >> bits;
    }

    // The weighted sum of 3 consecutive values of a row for 4 positions: the values index to index + 2 for the first one
    private static Vector128<int> Weigh(ReadOnlySpan<int> values, int index, int sideWeight, int middleWeight)
    {
        var sides = Vector128.Create(values.Slice(index, 4)) + Vector128.Create(values.Slice(index + 2, 4));
        return (sides * sideWeight) + (Vector128.Create(values.Slice(index + 1, 4)) * middleWeight);
    }

    // The weight of the neighbor (dy, dx) of the sample of row i in the box filter process (section 7.17.3)
    private static int GetWeight(int pass, int i, int dy, int dx)
    {
        if (pass is 0)
            return ((i + dy) & 1) is not 0 ? (dx is 0 ? 6 : 5) : 0;

        return dx is 0 || dy is 0 ? 4 : 3;
    }

    private static byte[] CreateSgrA2()
    {
        var values = new byte[255];
        values[0] = 1;
        for (var z = 1; z < values.Length; z++)
        {
            values[z] = (byte)(((z << Av1Constants.SgrprojSgrBits) + (z / 2)) / (z + 1));
        }

        return values;
    }

    private static long Round2(long value, int bits) => bits is 0 ? value : (value + (1L << (bits - 1))) >> bits;
}
