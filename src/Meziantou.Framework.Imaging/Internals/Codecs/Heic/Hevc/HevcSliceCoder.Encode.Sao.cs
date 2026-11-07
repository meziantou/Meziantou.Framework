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
        var bandShift = _bitDepth - 5;
        var samples = plane.Samples;
        var stride = plane.Stride;
        var hPos = HevcSampleAdaptiveOffset.HPos;
        var vPos = HevcSampleAdaptiveOffset.VPos;
        for (var j = 0; j < height; j++)
        {
            var y = yCtb + j;
            var original = source.Samples.Slice((y * source.Stride) + xCtb, width);
            for (var i = 0; i < width; i++)
            {
                var x = xCtb + i;
                int sample = samples[(y * stride) + x];
                var difference = original[i] - sample;
                var band = SaoEdgeStatistics + (sample >> bandShift);
                counts[band]++;
                sums[band] += difference;
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
}
