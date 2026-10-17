using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The sample adaptive offset process (Rec. ITU-T H.265, clause 8.7.3): the samples of each coding tree block are
/// modified from the deblocked samples of the picture. The rows of samples whose neighbours are all inside their coding
/// tree block are modified 8 samples at a time (<see cref="EdgeOffsetRow"/>, <see cref="BandOffsetRow"/>), with the
/// results of their scalar references.
/// </summary>
internal static class HevcSampleAdaptiveOffset
{
    // Table 8-13 - Specification of hPos and vPos according to the sample adaptive offset class
    internal static ReadOnlySpan<sbyte> HPos => [-1, 1, 0, 0, -1, 1, 1, -1];

    internal static ReadOnlySpan<sbyte> VPos => [0, 0, -1, 1, -1, 1, -1, 1];

    /// <summary>Applies the process to the picture, whose slices are all decoded and deblocked.</summary>
    /// <exception cref="ImageResourceLimitException">The copy of the deblocked samples of a plane exceeds the live-allocation limit.</exception>
    public static void Apply(AllocationScope scope, HevcPicture picture, HevcPictureLayout layout, HevcPictureState state, CancellationToken cancellationToken)
    {
        var sps = layout.Sps;
        if (!sps.SampleAdaptiveOffsetEnabledFlag)
            return;

        for (var cIdx = 0; cIdx < picture.PlaneCount; cIdx++)
        {
            var used = false;
            foreach (var slice in state.Slices)
            {
                used |= cIdx is 0 ? slice.SliceSaoLumaFlag : slice.SliceSaoChromaFlag;
            }

            if (!used)
                continue;

            // The process reads the deblocked samples of the neighbours of a sample: recPicture is a copy of the plane
            var plane = picture.GetPlane(cIdx);
            using var recPicture = Av1PooledArray<ushort>.Rent(scope, (long)plane.Stride * plane.Height, clear: false);
            plane.Samples[..recPicture.Length].CopyTo(recPicture.Span);
            for (var ry = 0; ry < sps.PicHeightInCtbsY; ry++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var rx = 0; rx < sps.PicWidthInCtbsY; rx++)
                {
                    ModifyCtb(recPicture.Span, plane, layout, state, cIdx, rx, ry);
                }
            }
        }
    }

    // Clause 8.7.3.2 "CTB modification process"
    private static void ModifyCtb(ReadOnlySpan<ushort> recPicture, Av1Plane plane, HevcPictureLayout layout, HevcPictureState state, int cIdx, int rx, int ry)
    {
        var sps = layout.Sps;
        var pps = layout.Pps;
        var ctbAddrRs = (ry * sps.PicWidthInCtbsY) + rx;
        var slice = state.Slices[state.SliceIndex[ctbAddrRs]];
        if (!(cIdx is 0 ? slice.SliceSaoLumaFlag : slice.SliceSaoChromaFlag))
            return;

        var sao = state.Sao.Slice((ctbAddrRs * HevcPictureState.SaoValuesPerCtb) + (cIdx * 6), 6);
        var saoTypeIdx = sao[0];
        if (saoTypeIdx is 0)
            return;

        var subWidth = cIdx is 0 ? 1 : sps.SubWidthC;
        var subHeight = cIdx is 0 ? 1 : sps.SubHeightC;
        var nCtbSw = sps.CtbSizeY / subWidth;
        var nCtbSh = sps.CtbSizeY / subHeight;
        var xCtb = rx * nCtbSw;
        var yCtb = ry * nCtbSh;
        var width = Math.Min(nCtbSw, plane.Width - xCtb);
        var height = Math.Min(nCtbSh, plane.Height - yCtb);
        var bitDepth = sps.BitDepth;
        var maxValue = (1 << bitDepth) - 1;
        var stride = plane.Stride;
        var saoPicture = plane.Samples;

        // Equation 7-72: SaoOffsetVal, whose first value is 0
        var log2OffsetScale = cIdx is 0 ? pps.Log2SaoOffsetScaleLuma : pps.Log2SaoOffsetScaleChroma;
        Span<int> saoOffsetVal = stackalloc int[5];
        for (var i = 0; i < 4; i++)
        {
            saoOffsetVal[i + 1] = sao[2 + i] * (1 << log2OffsetScale);
        }

        if (log2OffsetScale is not 0)
        {
            state.Tools |= HevcTools.SaoOffsetScale;
        }

        // The samples of the coding units that the in-loop filters leave as they are
        var checkUnfiltered = pps.TransquantBypassEnabledFlag || (sps.PcmEnabledFlag && sps.PcmLoopFilterDisabledFlag);
        if (saoTypeIdx is 1)
        {
            // Equation 8-414: the band of the sample selects the offset
            var bandShift = bitDepth - 5;
            int saoLeftClass = sao[1];
            for (var j = 0; j < height; j++)
            {
                var row = (yCtb + j) * stride;
                if (!checkUnfiltered)
                {
                    BandOffsetRow(recPicture.Slice(row + xCtb, width), saoPicture.Slice(row + xCtb, width), bandShift, saoLeftClass, saoOffsetVal, maxValue);
                    continue;
                }

                for (var i = 0; i < width; i++)
                {
                    if (IsUnfiltered(state, sps, (xCtb + i) * subWidth, (yCtb + j) * subHeight))
                        continue;

                    BandOffsetRow(recPicture.Slice(row + xCtb + i, 1), saoPicture.Slice(row + xCtb + i, 1), bandShift, saoLeftClass, saoOffsetVal, maxValue);
                }
            }

            return;
        }

        // The edge offset: the samples of the neighbouring coding tree blocks that the process may read
        Span<bool> usable = stackalloc bool[9];
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                usable[((dy + 1) * 3) + dx + 1] = IsNeighbourUsable(layout, state, rx, ry, dx, dy);
            }
        }

        int saoEoClass = sao[1];
        int hPos0 = HPos[saoEoClass * 2];
        int hPos1 = HPos[(saoEoClass * 2) + 1];
        int vPos0 = VPos[saoEoClass * 2];
        int vPos1 = VPos[(saoEoClass * 2) + 1];

        // Equations 8-411 to 8-413: edgeIdx is 2 + the signs of the differences with the two neighbours, and its values 0, 1
        // and 2 become 1, 2 and 0
        Span<int> edgeOffsets = [saoOffsetVal[1], saoOffsetVal[2], saoOffsetVal[0], saoOffsetVal[3], saoOffsetVal[4]];

        // The columns whose two neighbours are in the coding tree block
        var interiorStart = Math.Max(0, -Math.Min(hPos0, hPos1));
        var interiorEnd = width - Math.Max(0, Math.Max(hPos0, hPos1));
        for (var j = 0; j < height; j++)
        {
            var row = (yCtb + j) * stride;
            var vectorized = !checkUnfiltered && j + Math.Min(vPos0, vPos1) >= 0 && j + Math.Max(vPos0, vPos1) < height && interiorStart < interiorEnd;
            if (vectorized)
            {
                // A neighbour inside the coding tree block is always usable
                var position = row + xCtb + interiorStart;
                var length = interiorEnd - interiorStart;
                EdgeOffsetRow(
                    recPicture.Slice(position, length),
                    recPicture.Slice(position + (vPos0 * stride) + hPos0, length),
                    recPicture.Slice(position + (vPos1 * stride) + hPos1, length),
                    saoPicture.Slice(position, length),
                    edgeOffsets,
                    maxValue);
            }

            for (var i = 0; i < width; i++)
            {
                if (vectorized && i == interiorStart)
                {
                    i = interiorEnd - 1;
                    continue;
                }

                if (checkUnfiltered && IsUnfiltered(state, sps, (xCtb + i) * subWidth, (yCtb + j) * subHeight))
                    continue;

                if (!IsSampleUsable(usable, i + hPos0, j + vPos0, width, height, nCtbSw, nCtbSh)
                    || !IsSampleUsable(usable, i + hPos1, j + vPos1, width, height, nCtbSw, nCtbSh))
                {
                    // edgeIdx is 0
                    continue;
                }

                var position = row + xCtb + i;
                EdgeOffsetRow(
                    recPicture.Slice(position, 1),
                    recPicture.Slice(position + (vPos0 * stride) + hPos0, 1),
                    recPicture.Slice(position + (vPos1 * stride) + hPos1, 1),
                    saoPicture.Slice(position, 1),
                    edgeOffsets,
                    maxValue);
            }
        }
    }

    /// <summary>
    /// The edge offset of a run of samples (equations 8-411 to 8-413): <paramref name="offsets"/> is indexed by 2 + the signs of
    /// the differences of a sample with its two neighbours.
    /// </summary>
    internal static void EdgeOffsetRow(ReadOnlySpan<ushort> samples, ReadOnlySpan<ushort> neighbours0, ReadOnlySpan<ushort> neighbours1, Span<ushort> destination, ReadOnlySpan<int> offsets, int maxValue)
    {
        var i = 0;
        if (Vector128.IsHardwareAccelerated && samples.Length >= Vector128<ushort>.Count)
        {
            // The offsets of a SAO are at most 31 << 6: they fit 16-bit lanes, the sums of a sample and an offset 32-bit ones
            var offset0 = Vector128.Create((short)offsets[0]);
            var offset1 = Vector128.Create((short)offsets[1]);
            var offset2 = Vector128.Create((short)offsets[2]);
            var offset3 = Vector128.Create((short)offsets[3]);
            var offset4 = Vector128.Create((short)offsets[4]);
            var max = Vector128.Create(maxValue);
            for (; i <= samples.Length - Vector128<ushort>.Count; i += Vector128<ushort>.Count)
            {
                var sample = Vector128.Create(samples.Slice(i, Vector128<ushort>.Count));
                var edgeIdx = Vector128.Create((short)2) + Sign(sample, Vector128.Create(neighbours0.Slice(i, Vector128<ushort>.Count))) + Sign(sample, Vector128.Create(neighbours1.Slice(i, Vector128<ushort>.Count)));
                var offset = Vector128.ConditionalSelect(Vector128.Equals(edgeIdx, Vector128.Create((short)1)), offset1, offset0);
                offset = Vector128.ConditionalSelect(Vector128.Equals(edgeIdx, Vector128.Create((short)2)), offset2, offset);
                offset = Vector128.ConditionalSelect(Vector128.Equals(edgeIdx, Vector128.Create((short)3)), offset3, offset);
                offset = Vector128.ConditionalSelect(Vector128.Equals(edgeIdx, Vector128.Create((short)4)), offset4, offset);
                AddOffsets(sample, offset, max).CopyTo(destination[i..]);
            }
        }

        EdgeOffsetRowScalar(samples[i..], neighbours0[i..], neighbours1[i..], destination[i..], offsets, maxValue);

        // The sign of a - b: -1 where a < b, 1 where a > b
        static Vector128<short> Sign(Vector128<ushort> a, Vector128<ushort> b) => Vector128.LessThan(a, b).AsInt16() - Vector128.GreaterThan(a, b).AsInt16();
    }

    /// <summary>The edge offset of <see cref="EdgeOffsetRow"/>, one sample at a time: the reference of the vector path.</summary>
    internal static void EdgeOffsetRowScalar(ReadOnlySpan<ushort> samples, ReadOnlySpan<ushort> neighbours0, ReadOnlySpan<ushort> neighbours1, Span<ushort> destination, ReadOnlySpan<int> offsets, int maxValue)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            int sample = samples[i];
            var edgeIdx = 2 + Math.Sign(sample - neighbours0[i]) + Math.Sign(sample - neighbours1[i]);
            destination[i] = (ushort)Math.Clamp(sample + offsets[edgeIdx], 0, maxValue);
        }
    }

    /// <summary>
    /// The band offset of a run of samples (equation 8-414): the bands saoLeftClass to saoLeftClass + 3 (modulo 32) have the
    /// offsets 1 to 4 of <paramref name="offsets"/>, the other ones the offset 0.
    /// </summary>
    internal static void BandOffsetRow(ReadOnlySpan<ushort> samples, Span<ushort> destination, int bandShift, int saoLeftClass, ReadOnlySpan<int> offsets, int maxValue)
    {
        var i = 0;
        if (Vector128.IsHardwareAccelerated && samples.Length >= Vector128<ushort>.Count)
        {
            // The band k places after saoLeftClass, modulo 32: its offset is offsets[ k + 1 ] for k from 0 to 3
            var leftClass = Vector128.Create((short)saoLeftClass);
            var mask = Vector128.Create((short)31);
            var offset0 = Vector128.Create((short)offsets[0]);
            var offset1 = Vector128.Create((short)offsets[1]);
            var offset2 = Vector128.Create((short)offsets[2]);
            var offset3 = Vector128.Create((short)offsets[3]);
            var offset4 = Vector128.Create((short)offsets[4]);
            var max = Vector128.Create(maxValue);
            for (; i <= samples.Length - Vector128<ushort>.Count; i += Vector128<ushort>.Count)
            {
                var sample = Vector128.Create(samples.Slice(i, Vector128<ushort>.Count));
                var k = (Vector128.ShiftRightLogical(sample, bandShift).AsInt16() - leftClass) & mask;
                var offset = Vector128.ConditionalSelect(Vector128.Equals(k, Vector128.Create((short)0)), offset1, offset0);
                offset = Vector128.ConditionalSelect(Vector128.Equals(k, Vector128.Create((short)1)), offset2, offset);
                offset = Vector128.ConditionalSelect(Vector128.Equals(k, Vector128.Create((short)2)), offset3, offset);
                offset = Vector128.ConditionalSelect(Vector128.Equals(k, Vector128.Create((short)3)), offset4, offset);
                AddOffsets(sample, offset, max).CopyTo(destination[i..]);
            }
        }

        BandOffsetRowScalar(samples[i..], destination[i..], bandShift, saoLeftClass, offsets, maxValue);
    }

    /// <summary>The band offset of <see cref="BandOffsetRow"/>, one sample at a time: the reference of the vector path.</summary>
    internal static void BandOffsetRowScalar(ReadOnlySpan<ushort> samples, Span<ushort> destination, int bandShift, int saoLeftClass, ReadOnlySpan<int> offsets, int maxValue)
    {
        Span<byte> bandTable = stackalloc byte[32];
        for (var k = 0; k < 4; k++)
        {
            bandTable[(k + saoLeftClass) & 31] = (byte)(k + 1);
        }

        for (var i = 0; i < samples.Length; i++)
        {
            int sample = samples[i];
            var bandIdx = bandTable[sample >> bandShift];
            destination[i] = (ushort)Math.Clamp(sample + offsets[bandIdx], 0, maxValue);
        }
    }

    // Clip3( 0, maxValue, sample + offset ) for 8 samples, in 32-bit lanes
    private static Vector128<ushort> AddOffsets(Vector128<ushort> samples, Vector128<short> offsets, Vector128<int> max)
    {
        var low = Vector128.WidenLower(samples).AsInt32() + Vector128.WidenLower(offsets);
        var high = Vector128.WidenUpper(samples).AsInt32() + Vector128.WidenUpper(offsets);
        low = Vector128.Min(Vector128.Max(low, Vector128<int>.Zero), max);
        high = Vector128.Min(Vector128.Max(high, Vector128<int>.Zero), max);
        return Vector128.Narrow(low.AsUInt32(), high.AsUInt32());
    }

    // Whether the sample at ( x, y ) of the coding tree block, or of one of its neighbours, is a sample that the edge
    // offset may read: inside the picture, and in a coding tree block that the filter may cross into
    private static bool IsSampleUsable(ReadOnlySpan<bool> usable, int x, int y, int width, int height, int nCtbSw, int nCtbSh)
    {
        var dx = x < 0 ? -1 : x >= nCtbSw ? 1 : 0;
        var dy = y < 0 ? -1 : y >= nCtbSh ? 1 : 0;

        // A coding tree block that the edge of the picture cuts has no sample beyond it, nor have its neighbours
        if ((dx is 0 && x >= width) || (dy is 0 && y >= height))
            return false;

        return usable[((dy + 1) * 3) + dx + 1];
    }

    // The conditions of clause 8.7.3.2 on the samples of the coding tree block at ( rx + dx, ry + dy )
    private static bool IsNeighbourUsable(HevcPictureLayout layout, HevcPictureState state, int rx, int ry, int dx, int dy)
    {
        var sps = layout.Sps;
        var nx = rx + dx;
        var ny = ry + dy;
        if (nx < 0 || ny < 0 || nx >= sps.PicWidthInCtbsY || ny >= sps.PicHeightInCtbsY)
            return false;

        var ctbAddrRs = (ry * sps.PicWidthInCtbsY) + rx;
        var neighbourAddrRs = (ny * sps.PicWidthInCtbsY) + nx;
        if (neighbourAddrRs == ctbAddrRs)
            return true;

        var ctbAddrTs = layout.CtbAddrRsToTs[ctbAddrRs];
        var neighbourAddrTs = layout.CtbAddrRsToTs[neighbourAddrRs];
        var sliceIndex = state.SliceIndex[ctbAddrRs];
        var neighbourSliceIndex = state.SliceIndex[neighbourAddrRs];
        if (neighbourSliceIndex != sliceIndex)
        {
            // The slice that is decoded last tells whether the filter crosses the boundary of the two slices
            var later = neighbourAddrTs < ctbAddrTs ? sliceIndex : neighbourSliceIndex;
            if (!state.Slices[later].SliceLoopFilterAcrossSlicesEnabledFlag)
                return false;
        }

        return layout.Pps.LoopFilterAcrossTilesEnabledFlag || layout.TileId[neighbourAddrTs] == layout.TileId[ctbAddrTs];
    }

    private static bool IsUnfiltered(HevcPictureState state, HevcSequenceParameterSet sps, int xY, int yY)
    {
        var flags = state.CuFlags[((yY >> state.MinCbLog2SizeY) * state.WidthInMinCbs) + (xY >> state.MinCbLog2SizeY)];
        return (flags & HevcPictureState.CuTransquantBypass) is not 0 || (sps.PcmLoopFilterDisabledFlag && (flags & HevcPictureState.CuPcm) is not 0);
    }
}
