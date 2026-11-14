using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

// The sample adaptive offset of the coding tree blocks (clause 8.7.3): its parameters are decided from the deblocked
// picture, which the process of the decoder then modifies
internal ref partial struct HevcSliceCoder
{
    // The statistics of a component of a coding tree block: 4 categories for each of the 4 classes of the edge offset,
    // then the 32 bands of the band offset
    private const int SaoEdgeStatistics = 16;
    private const int SaoStatistics = SaoEdgeStatistics + 32;

    /// <summary>
    /// Decides the parameters of the sample adaptive offset of a coding tree block, which become those of the picture
    /// state: the best offsets of each component, or the parameters of the coding tree block to the left or above when
    /// they cost less (clause 7.4.9.3.2).
    /// </summary>
    private readonly void SearchSao(int rx, int ry)
    {
        var components = _chromaArrayType is 0 ? 1 : 3;
        var all = _state.Sao;
        var sao = all.Slice(_ctbAddrInRs * HevcPictureState.SaoValuesPerCtb, HevcPictureState.SaoValuesPerCtb);
        Span<long> counts = stackalloc long[3 * SaoStatistics];
        Span<long> sums = stackalloc long[3 * SaoStatistics];
        counts.Clear();
        sums.Clear();
        for (var cIdx = 0; cIdx < components; cIdx++)
        {
            if (cIdx is 0 ? _header.SliceSaoLumaFlag : _header.SliceSaoChromaFlag)
            {
                CollectSaoStatistics(cIdx, rx, ry, counts.Slice(cIdx * SaoStatistics, SaoStatistics), sums.Slice(cIdx * SaoStatistics, SaoStatistics));
            }
        }

        // The parameters of the coding tree block
        Span<sbyte> best = stackalloc sbyte[HevcPictureState.SaoValuesPerCtb];
        best.Clear();
        var cost = 0L;
        if (_header.SliceSaoLumaFlag)
        {
            cost += SearchSaoOffsets(0, 1, counts, sums, best);
        }

        if (_header.SliceSaoChromaFlag)
        {
            cost += SearchSaoOffsets(1, 2, counts, sums, best);
        }

        // sao_merge_left_flag and sao_merge_up_flag
        var context = _cabac.Contexts[HevcContextLayout.SaoMergeFlag];
        var merged = GetRateCost(HevcCost.GetBinCost(context, 1));
        var notMerged = GetRateCost(HevcCost.GetBinCost(context, 0));
        if (rx > 0)
        {
            cost += notMerged;
        }

        if (ry > 0)
        {
            cost += notMerged;
        }

        if (rx > 0)
        {
            var left = all.Slice((_ctbAddrInRs - 1) * HevcPictureState.SaoValuesPerCtb, HevcPictureState.SaoValuesPerCtb);
            var leftCost = merged + GetSaoDistortionCost(left, counts, sums);
            if (leftCost < cost)
            {
                cost = leftCost;
                left.CopyTo(best);
            }
        }

        if (ry > 0)
        {
            var up = all.Slice((_ctbAddrInRs - _picWidthInCtbsY) * HevcPictureState.SaoValuesPerCtb, HevcPictureState.SaoValuesPerCtb);
            var upCost = merged + (rx > 0 ? notMerged : 0) + GetSaoDistortionCost(up, counts, sums);
            if (upCost < cost)
            {
                up.CopyTo(best);
            }
        }

        best.CopyTo(sao);
    }

    // The cost of a number of bits, as HevcEncoderDecisions.GetCost gives it without distortion
    private readonly long GetRateCost(long rate) => (rate * _decisions.Lambda) >> 8;

    // The change of the squared error that an offset makes, from the statistics: each of the count samples of a category
    // gets closer to its source by the offset, sum being the differences with the source
    private static long GetSaoDistortion(long count, long sum, int offset) => (count * offset * offset) - (2 * offset * sum);

    // The cost of the parameters of another coding tree block for the samples of this one
    private readonly long GetSaoDistortionCost(ReadOnlySpan<sbyte> sao, ReadOnlySpan<long> counts, ReadOnlySpan<long> sums)
    {
        var distortion = 0L;
        for (var cIdx = 0; cIdx < (_chromaArrayType is 0 ? 1 : 3); cIdx++)
        {
            var values = sao.Slice(cIdx * 6, 6);
            var first = cIdx * SaoStatistics;
            for (var k = 0; k < 4 && values[0] is not 0; k++)
            {
                // The band offset (sao_type_idx 1) of four bands from sao_band_position, or a class of the edge offset
                var index = first + (values[0] is 1 ? SaoEdgeStatistics + ((values[1] + k) & 31) : (values[1] * 4) + k);
                distortion += GetSaoDistortion(counts[index], sums[index], values[2 + k]);
            }
        }

        return distortion << 8;
    }

    // The best type and offsets of the luma component, or of the two chroma components, which have the same type and the
    // same class of edge offset: returns their cost
    private readonly long SearchSaoOffsets(int first, int count, ReadOnlySpan<long> counts, ReadOnlySpan<long> sums, Span<sbyte> best)
    {
        var cMax = (1 << (Math.Min(_bitDepth, 10) - 5)) - 1;
        var context = _cabac.Contexts[HevcContextLayout.SaoTypeIdx];
        var bestCost = GetRateCost(HevcCost.GetBinCost(context, 0));
        Span<sbyte> candidate = stackalloc sbyte[12];
        Span<sbyte> offsets = stackalloc sbyte[4];
        var typeCost = GetRateCost(HevcCost.GetBinCost(context, 1) + HevcCost.GetBypassCost(1));

        // sao_type_idx 2: the edge offset, whose first two offsets are positive and whose last two are negative
        for (var saoEoClass = 0; saoEoClass < 4; saoEoClass++)
        {
            var cost = typeCost + GetRateCost(HevcCost.GetBypassCost(2));
            for (var c = 0; c < count; c++)
            {
                var values = candidate.Slice(c * 6, 6);
                values[0] = 2;
                values[1] = (sbyte)saoEoClass;
                for (var k = 0; k < 4; k++)
                {
                    var index = ((first + c) * SaoStatistics) + (saoEoClass * 4) + k;
                    cost += GetBestSaoOffset(counts[index], sums[index], k < 2 ? 0 : -cMax, k < 2 ? cMax : 0, cMax, signed: false, out var offset);
                    values[2 + k] = (sbyte)offset;
                }
            }

            if (cost < bestCost)
            {
                bestCost = cost;
                candidate[..(count * 6)].CopyTo(best[(first * 6)..]);
            }
        }

        if (!_decisions.SampleAdaptiveOffsetBands)
            return bestCost;

        // sao_type_idx 1: the band offset of the four consecutive bands that gain the most, for each component
        var bandCost = typeCost;
        for (var c = 0; c < count; c++)
        {
            var values = candidate.Slice(c * 6, 6);
            var componentCost = long.MaxValue;
            for (var saoBandPosition = 0; saoBandPosition < 32; saoBandPosition++)
            {
                var cost = GetRateCost(HevcCost.GetBypassCost(5));
                for (var k = 0; k < 4; k++)
                {
                    var index = ((first + c) * SaoStatistics) + SaoEdgeStatistics + ((saoBandPosition + k) & 31);
                    cost += GetBestSaoOffset(counts[index], sums[index], -cMax, cMax, cMax, signed: true, out var offset);
                    offsets[k] = (sbyte)offset;
                }

                if (cost < componentCost)
                {
                    componentCost = cost;
                    values[0] = 1;
                    values[1] = (sbyte)saoBandPosition;
                    offsets.CopyTo(values[2..]);
                }
            }

            bandCost += componentCost;
        }

        if (bandCost < bestCost)
        {
            bestCost = bandCost;
            candidate[..(count * 6)].CopyTo(best[(first * 6)..]);
        }

        return bestCost;
    }

    // The offset of a category that costs the least, between the mean of the differences with the source and 0: returns
    // its cost. sao_offset_abs is a truncated unary code, followed by a sign for the band offset.
    private readonly long GetBestSaoOffset(long count, long sum, int minimum, int maximum, int cMax, bool signed, out int offset)
    {
        offset = 0;
        var bestCost = GetRateCost(HevcCost.GetBypassCost(cMax is 0 ? 0 : 1));
        if (count is 0)
            return bestCost;

        var mean = (int)Math.Clamp(sum >= 0 ? ((2 * sum) + count) / (2 * count) : -(((-2 * sum) + count) / (2 * count)), minimum, maximum);
        for (var value = mean; value is not 0; value -= Math.Sign(value))
        {
            var magnitude = Math.Abs(value);
            var bits = magnitude + (magnitude < cMax ? 1 : 0) + (signed ? 1 : 0);
            var cost = (GetSaoDistortion(count, sum, value) << 8) + GetRateCost(HevcCost.GetBypassCost(bits));
            if (cost < bestCost)
            {
                bestCost = cost;
                offset = value;
            }
        }

        return bestCost;
    }

    // The samples of a component of a coding tree block by category (clause 8.7.3.2): their number, and the sum of
    // their differences with the source
    private readonly void CollectSaoStatistics(int cIdx, int rx, int ry, Span<long> counts, Span<long> sums)
    {
        var plane = _picture.GetPlane(cIdx);
        var source = _workspace.GetSourcePlane(cIdx);
        var subWidth = cIdx is 0 ? 1 : _sps.SubWidthC;
        var subHeight = cIdx is 0 ? 1 : _sps.SubHeightC;
        var nCtbSw = _sps.CtbSizeY / subWidth;
        var nCtbSh = _sps.CtbSizeY / subHeight;
        var xCtb = rx * nCtbSw;
        var yCtb = ry * nCtbSh;
        var pictureWidth = _sps.PicWidthInLumaSamples / subWidth;
        var pictureHeight = _sps.PicHeightInLumaSamples / subHeight;
        var width = Math.Min(nCtbSw, pictureWidth - xCtb);
        var height = Math.Min(nCtbSh, pictureHeight - yCtb);
        CollectSaoStatistics(plane.Samples, plane.Stride, source.Samples, source.Stride, xCtb, yCtb, width, height, pictureWidth, pictureHeight, _bitDepth, counts, sums);
    }

    /// <summary>
    /// The statistics of the samples of a block of at most 64x64 samples of a plane (clause 8.7.3.2): the edge classes of
    /// the samples whose neighbours are all in the picture in vectors of 16-bit lanes, and the rest one sample at a time.
    /// </summary>
    /// <param name="samples">The deblocked samples of the plane.</param>
    /// <param name="stride">The stride of <paramref name="samples"/>.</param>
    /// <param name="original">The samples of the source, at the same positions.</param>
    /// <param name="originalStride">The stride of <paramref name="original"/>.</param>
    /// <param name="xCtb">The horizontal position of the block.</param>
    /// <param name="yCtb">The vertical position of the block.</param>
    /// <param name="width">The width of the block.</param>
    /// <param name="height">The height of the block.</param>
    /// <param name="pictureWidth">The width of the plane.</param>
    /// <param name="pictureHeight">The height of the plane.</param>
    /// <param name="bitDepth">The bit depth of the samples.</param>
    /// <param name="counts">The numbers of samples of the 16 categories of the edge offset, then of the 32 bands, which the method increases.</param>
    /// <param name="sums">The sums of the differences between the source and the samples, as <paramref name="counts"/>.</param>
    internal static void CollectSaoStatistics(ReadOnlySpan<ushort> samples, int stride, ReadOnlySpan<ushort> original, int originalStride, int xCtb, int yCtb, int width, int height, int pictureWidth, int pictureHeight, int bitDepth, Span<long> counts, Span<long> sums)
    {
        // The 16-bit lanes hold the differences (13 bits with their sign), and add those of the 8 vectors at most of a row
        // of 64 samples (8 * 4095 < 2^15). The counts of a lane are at most 64 rows of 8 vectors
        var x0 = Math.Max(xCtb, 1);
        var x1 = Math.Min(xCtb + width, pictureWidth - 1);
        if (!Vector128.IsHardwareAccelerated || bitDepth > 12 || width > 64 || height > 64 || x1 - x0 < Vector128<short>.Count)
        {
            CollectSaoStatisticsScalar(samples, stride, original, originalStride, xCtb, yCtb, width, height, pictureWidth, pictureHeight, bitDepth, counts, sums);
            return;
        }

        // The bands, then the edge classes of the samples on the edges of the picture
        var bandShift = bitDepth - 5;
        for (var y = yCtb; y < yCtb + height; y++)
        {
            var row = samples.Slice((y * stride) + xCtb, width);
            var originalRow = original.Slice((y * originalStride) + xCtb, width);
            for (var i = 0; i < row.Length; i++)
            {
                int sample = row[i];
                var band = SaoEdgeStatistics + (sample >> bandShift);
                counts[band]++;
                sums[band] += originalRow[i] - sample;
            }

            if (y is 0 || y == pictureHeight - 1)
            {
                AddSaoEdgeStatistics(samples, stride, original, originalStride, xCtb, xCtb + width, y, pictureWidth, pictureHeight, counts, sums);
            }
            else
            {
                AddSaoEdgeStatistics(samples, stride, original, originalStride, xCtb, x0, y, pictureWidth, pictureHeight, counts, sums);
                AddSaoEdgeStatistics(samples, stride, original, originalStride, x1, xCtb + width, y, pictureWidth, pictureHeight, counts, sums);
            }
        }

        // The other samples, a class at a time: the lanes of the last vector of a row that the one before it has already
        // counted are left out. A category is 0 to 3 for edgeIdx 1, 2, 3 and 4 (Equations 8-411 to 8-413), which are the
        // sums -2, -1, 1 and 2 of the signs of the differences with the two neighbours
        var first = Math.Max(yCtb, 1);
        var last = Math.Min(yCtb + height, pictureHeight - 1);
        var lastX = x1 - Vector128<short>.Count;
        var skipped = Vector128.LessThan(Vector128.Create((short)0, 1, 2, 3, 4, 5, 6, 7), Vector128.Create((short)(Vector128<short>.Count - ((x1 - x0) % Vector128<short>.Count))));
        if ((x1 - x0) % Vector128<short>.Count is 0)
        {
            skipped = Vector128<short>.Zero;
        }

        var hPos = HevcSampleAdaptiveOffset.HPos;
        var vPos = HevcSampleAdaptiveOffset.VPos;
        var minusTwo = Vector128.Create((short)-2);
        var minusOne = Vector128.Create((short)-1);
        var one = Vector128.Create((short)1);
        var two = Vector128.Create((short)2);
        for (var saoEoClass = 0; saoEoClass < 4; saoEoClass++)
        {
            var offset0 = (vPos[saoEoClass * 2] * stride) + hPos[saoEoClass * 2];
            var offset1 = (vPos[(saoEoClass * 2) + 1] * stride) + hPos[(saoEoClass * 2) + 1];
            var count0 = Vector128<short>.Zero;
            var count1 = Vector128<short>.Zero;
            var count2 = Vector128<short>.Zero;
            var count3 = Vector128<short>.Zero;
            var sum0 = Vector128<int>.Zero;
            var sum1 = Vector128<int>.Zero;
            var sum2 = Vector128<int>.Zero;
            var sum3 = Vector128<int>.Zero;
            for (var y = first; y < last; y++)
            {
                var rowSum0 = Vector128<short>.Zero;
                var rowSum1 = Vector128<short>.Zero;
                var rowSum2 = Vector128<short>.Zero;
                var rowSum3 = Vector128<short>.Zero;
                var rowStart = y * stride;
                var originalStart = (y * originalStride) - rowStart;
                for (var x = x0; x < x1; x += Vector128<short>.Count)
                {
                    var excluded = Vector128<short>.Zero;
                    var position = rowStart + x;
                    if (x > lastX)
                    {
                        position = rowStart + lastX;
                        excluded = skipped;
                    }

                    var sample = Vector128.Create(samples.Slice(position, Vector128<short>.Count)).AsInt16();
                    var difference = Vector128.Create(original.Slice(originalStart + position, Vector128<short>.Count)).AsInt16() - sample;
                    var neighbour0 = Vector128.Create(samples.Slice(position + offset0, Vector128<short>.Count)).AsInt16();
                    var neighbour1 = Vector128.Create(samples.Slice(position + offset1, Vector128<short>.Count)).AsInt16();
                    var signs = Vector128.LessThan(sample, neighbour0) - Vector128.GreaterThan(sample, neighbour0) + Vector128.LessThan(sample, neighbour1) - Vector128.GreaterThan(sample, neighbour1);
                    signs = Vector128.AndNot(signs, excluded);
                    var mask0 = Vector128.Equals(signs, minusTwo);
                    var mask1 = Vector128.Equals(signs, minusOne);
                    var mask2 = Vector128.Equals(signs, one);
                    var mask3 = Vector128.Equals(signs, two);
                    count0 -= mask0;
                    count1 -= mask1;
                    count2 -= mask2;
                    count3 -= mask3;
                    rowSum0 += difference & mask0;
                    rowSum1 += difference & mask1;
                    rowSum2 += difference & mask2;
                    rowSum3 += difference & mask3;
                }

                sum0 += Vector128.WidenLower(rowSum0) + Vector128.WidenUpper(rowSum0);
                sum1 += Vector128.WidenLower(rowSum1) + Vector128.WidenUpper(rowSum1);
                sum2 += Vector128.WidenLower(rowSum2) + Vector128.WidenUpper(rowSum2);
                sum3 += Vector128.WidenLower(rowSum3) + Vector128.WidenUpper(rowSum3);
            }

            var category = saoEoClass * 4;
            counts[category] += Vector128.Sum(Vector128.WidenLower(count0) + Vector128.WidenUpper(count0));
            counts[category + 1] += Vector128.Sum(Vector128.WidenLower(count1) + Vector128.WidenUpper(count1));
            counts[category + 2] += Vector128.Sum(Vector128.WidenLower(count2) + Vector128.WidenUpper(count2));
            counts[category + 3] += Vector128.Sum(Vector128.WidenLower(count3) + Vector128.WidenUpper(count3));
            sums[category] += Vector128.Sum(sum0);
            sums[category + 1] += Vector128.Sum(sum1);
            sums[category + 2] += Vector128.Sum(sum2);
            sums[category + 3] += Vector128.Sum(sum3);
        }
    }

    /// <summary>The statistics of <see cref="CollectSaoStatistics(ReadOnlySpan{ushort}, int, ReadOnlySpan{ushort}, int, int, int, int, int, int, int, int, Span{long}, Span{long})"/>, one sample at a time: the reference of the vector path.</summary>
    internal static void CollectSaoStatisticsScalar(ReadOnlySpan<ushort> samples, int stride, ReadOnlySpan<ushort> original, int originalStride, int xCtb, int yCtb, int width, int height, int pictureWidth, int pictureHeight, int bitDepth, Span<long> counts, Span<long> sums)
    {
        var bandShift = bitDepth - 5;
        for (var y = yCtb; y < yCtb + height; y++)
        {
            var row = samples.Slice((y * stride) + xCtb, width);
            var originalRow = original.Slice((y * originalStride) + xCtb, width);
            for (var i = 0; i < width; i++)
            {
                int sample = row[i];
                var band = SaoEdgeStatistics + (sample >> bandShift);
                counts[band]++;
                sums[band] += originalRow[i] - sample;
            }

            AddSaoEdgeStatistics(samples, stride, original, originalStride, xCtb, xCtb + width, y, pictureWidth, pictureHeight, counts, sums);
        }
    }

    // The edge classes of the samples start to end - 1 of the row y, one sample at a time
    private static void AddSaoEdgeStatistics(ReadOnlySpan<ushort> samples, int stride, ReadOnlySpan<ushort> original, int originalStride, int start, int end, int y, int pictureWidth, int pictureHeight, Span<long> counts, Span<long> sums)
    {
        var hPos = HevcSampleAdaptiveOffset.HPos;
        var vPos = HevcSampleAdaptiveOffset.VPos;
        for (var x = start; x < end; x++)
        {
            int sample = samples[(y * stride) + x];
            var difference = original[(y * originalStride) + x] - sample;
            for (var saoEoClass = 0; saoEoClass < 4; saoEoClass++)
            {
                // The sample is not modified when one of the two samples it is compared with is outside the picture
                var x0 = x + hPos[saoEoClass * 2];
                var y0 = y + vPos[saoEoClass * 2];
                var x1 = x + hPos[(saoEoClass * 2) + 1];
                var y1 = y + vPos[(saoEoClass * 2) + 1];
                if (x0 < 0 || x1 < 0 || y0 < 0 || x0 >= pictureWidth || x1 >= pictureWidth || y1 >= pictureHeight)
                    continue;

                // Equations 8-411 to 8-413: edgeIdx 1 to 4 are the categories
                var edgeIdx = 2 + Math.Sign(sample - samples[(y0 * stride) + x0]) + Math.Sign(sample - samples[(y1 * stride) + x1]);
                if (edgeIdx is 2)
                    continue;

                var category = (saoEoClass * 4) + (edgeIdx < 2 ? edgeIdx : edgeIdx - 1);
                counts[category]++;
                sums[category] += difference;
            }
        }
    }
}
