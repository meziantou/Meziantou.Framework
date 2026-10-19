using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The sample adaptive offset process (Rec. ITU-T H.265, clause 8.7.3): the samples of each coding tree block are
/// modified from the deblocked samples of the picture. The coding tree blocks are modified in place, in raster order: the
/// deblocked samples that the process still reads once their coding tree block is modified (the last row of the coding
/// tree blocks above, the last column of the coding tree block on the left and the rows of the coding tree block itself)
/// are copied before. A row of samples is modified 8 samples at a time (<see cref="EdgeOffsetRow"/>,
/// <see cref="BandOffsetRow"/>), with the results of their scalar references; the samples that keep their value are
/// written back from the copies.
/// </summary>
internal static class HevcSampleAdaptiveOffset
{
    // Table 8-13 - Specification of hPos and vPos according to the sample adaptive offset class
    internal static ReadOnlySpan<sbyte> HPos => [-1, 1, 0, 0, -1, 1, 1, -1];

    internal static ReadOnlySpan<sbyte> VPos => [0, 0, -1, 1, -1, 1, -1, 1];

    /// <summary>Applies the process to the picture, whose slices are all decoded and deblocked.</summary>
    /// <exception cref="ImageResourceLimitException">The copies of the deblocked samples exceed the live-allocation limit.</exception>
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

            // The process reads the deblocked samples of the neighbours of a sample (recPicture): the copies of the rows above
            // the coding tree blocks (the current one and the next one), of the columns on their left (the current one and
            // the next one), and three rows of a coding tree block with a sample on each side
            var plane = picture.GetPlane(cIdx);
            var nCtbSw = sps.CtbSizeY / (cIdx is 0 ? 1 : sps.SubWidthC);
            var nCtbSh = sps.CtbSizeY / (cIdx is 0 ? 1 : sps.SubHeightC);
            var lineLength = nCtbSw + 2;
            using var buffers = Av1PooledArray<ushort>.Rent(scope, (2L * plane.Width) + (2L * nCtbSh) + (3L * lineLength), clear: false);
            var span = buffers.Span;
            var above = span[..plane.Width];
            var nextAbove = span.Slice(plane.Width, plane.Width);
            var left = span.Slice(2 * plane.Width, nCtbSh);
            var nextLeft = span.Slice((2 * plane.Width) + nCtbSh, nCtbSh);
            var lines = span.Slice((2 * plane.Width) + (2 * nCtbSh), 3 * lineLength);
            for (var ry = 0; ry < sps.PicHeightInCtbsY; ry++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var yCtb = ry * nCtbSh;
                var height = Math.Min(nCtbSh, plane.Height - yCtb);
                plane.GetRow(yCtb + height - 1).CopyTo(nextAbove);
                for (var rx = 0; rx < sps.PicWidthInCtbsY; rx++)
                {
                    var xCtb = rx * nCtbSw;
                    var width = Math.Min(nCtbSw, plane.Width - xCtb);
                    var samples = plane.Samples;
                    for (var j = 0; j < height; j++)
                    {
                        nextLeft[j] = samples[((yCtb + j) * plane.Stride) + xCtb + width - 1];
                    }

                    ModifyCtb(plane, above, left, lines, layout, state, cIdx, rx, ry);
                    var swap = left;
                    left = nextLeft;
                    nextLeft = swap;
                }

                var swapAbove = above;
                above = nextAbove;
                nextAbove = swapAbove;
            }
        }
    }

    // Clause 8.7.3.2 "CTB modification process". above holds the deblocked samples of the row above the coding tree block,
    // left the ones of the column on its left; the samples of the coding tree block itself, and the ones below it and on
    // its right, are not modified yet.
    private static void ModifyCtb(Av1Plane plane, ReadOnlySpan<ushort> above, ReadOnlySpan<ushort> left, Span<ushort> lines, HevcPictureLayout layout, HevcPictureState state, int cIdx, int rx, int ry)
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
            // Equation 8-414: the band of the sample selects the offset. A sample only depends on itself: the rows are
            // modified in place
            var bandShift = bitDepth - 5;
            int saoLeftClass = sao[1];
            for (var j = 0; j < height; j++)
            {
                var row = saoPicture.Slice(((yCtb + j) * stride) + xCtb, width);
                if (!checkUnfiltered)
                {
                    BandOffsetRow(row, row, bandShift, saoLeftClass, saoOffsetVal, maxValue);
                    continue;
                }

                for (var i = 0; i < width; i++)
                {
                    if (IsUnfiltered(state, sps, (xCtb + i) * subWidth, (yCtb + j) * subHeight))
                        continue;

                    BandOffsetRow(row.Slice(i, 1), row.Slice(i, 1), bandShift, saoLeftClass, saoOffsetVal, maxValue);
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

        // The deblocked samples of three rows: the sample i of the coding tree block is at i + 1, after the sample of the
        // column on its left. The rows that the class does not read are not copied, and neither are the samples outside the
        // picture: the samples that read them keep their value.
        var lineLength = nCtbSw + 2;
        var previous = lines[..lineLength];
        var current = lines.Slice(lineLength, lineLength);
        var next = lines.Slice(2 * lineLength, lineLength);
        var vertical = vPos0 is not 0;
        if (vertical && yCtb > 0)
        {
            CopyLine(above, previous, xCtb, width, plane.Width);
        }

        CopyRow(plane, left, current, xCtb, yCtb, 0, width, height);
        var firstUsable = false;
        var lastUsable = false;
        var middleUsable = false;
        for (var j = 0; j < height; j++)
        {
            if (vertical && yCtb + j + 1 < plane.Height)
            {
                CopyRow(plane, left, next, xCtb, yCtb, j + 1, width, height);
            }

            // The first column, the last one, and the other ones, whose neighbours are in the same coding tree blocks; the
            // neighbours of the rows other than the first and the last one are in the coding tree block or on its sides
            if (j <= 1 || j == height - 1)
            {
                firstUsable = IsSampleUsable(usable, hPos0, j + vPos0, width, height, nCtbSw, nCtbSh) && IsSampleUsable(usable, hPos1, j + vPos1, width, height, nCtbSw, nCtbSh);
                lastUsable = IsSampleUsable(usable, width - 1 + hPos0, j + vPos0, width, height, nCtbSw, nCtbSh) && IsSampleUsable(usable, width - 1 + hPos1, j + vPos1, width, height, nCtbSw, nCtbSh);
                middleUsable = IsSampleUsable(usable, 0, j + vPos0, width, height, nCtbSw, nCtbSh) && IsSampleUsable(usable, 0, j + vPos1, width, height, nCtbSw, nCtbSh);
            }

            if (firstUsable || lastUsable || middleUsable)
            {
                var destination = saoPicture.Slice(((yCtb + j) * stride) + xCtb, width);
                var samples = current.Slice(1, width);
                EdgeOffsetRow(
                    samples,
                    GetLine(vPos0, previous, current, next).Slice(1 + hPos0, width),
                    GetLine(vPos1, previous, current, next).Slice(1 + hPos1, width),
                    destination,
                    edgeOffsets,
                    maxValue);

                // edgeIdx is 0 where a neighbour cannot be read
                if (!middleUsable && width > 2)
                {
                    samples[1..^1].CopyTo(destination[1..]);
                }

                if (!firstUsable)
                {
                    destination[0] = samples[0];
                }

                if (!lastUsable)
                {
                    destination[^1] = samples[^1];
                }

                if (checkUnfiltered)
                {
                    for (var i = 0; i < width; i++)
                    {
                        if (IsUnfiltered(state, sps, (xCtb + i) * subWidth, (yCtb + j) * subHeight))
                        {
                            destination[i] = samples[i];
                        }
                    }
                }
            }

            // The rows move up: the copy of the current row is the previous one of the next row
            var swap = previous;
            previous = current;
            current = next;
            next = swap;
            if (!vertical && j + 1 < height)
            {
                CopyRow(plane, left, current, xCtb, yCtb, j + 1, width, height);
            }
        }

        static Span<ushort> GetLine(int vPos, Span<ushort> previous, Span<ushort> current, Span<ushort> next) => vPos switch
        {
            < 0 => previous,
            0 => current,
            _ => next,
        };
    }

    // The deblocked samples of the row j of a coding tree block (or of the row below it, j = height), and of the samples on
    // each side that are in the picture: the column on the left is modified once its coding tree block is, unless it is
    // below the coding tree block
    private static void CopyRow(Av1Plane plane, ReadOnlySpan<ushort> left, Span<ushort> line, int xCtb, int yCtb, int j, int width, int height)
    {
        var row = plane.GetRow(yCtb + j);
        if (xCtb > 0)
        {
            line[0] = j < height ? left[j] : row[xCtb - 1];
        }

        var end = Math.Min(xCtb + width + 1, plane.Width);
        row[xCtb..end].CopyTo(line[1..]);
    }

    // The samples of a saved row of the picture that a coding tree block reads
    private static void CopyLine(ReadOnlySpan<ushort> row, Span<ushort> line, int xCtb, int width, int planeWidth)
    {
        var start = Math.Max(xCtb - 1, 0);
        var end = Math.Min(xCtb + width + 1, planeWidth);
        row[start..end].CopyTo(line[(start - xCtb + 1)..]);
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
            // The offsets of a SAO are at most 31 << 6: they fit 16-bit lanes. The table holds their bytes, and the lane of
            // edgeIdx k selects the bytes 2k and 2k + 1
            var table = Vector128.Create((short)offsets[0], (short)offsets[1], (short)offsets[2], (short)offsets[3], (short)offsets[4], 0, 0, 0).AsByte();
            var two = Vector128.Create((short)2);
            for (; i <= samples.Length - Vector128<ushort>.Count; i += Vector128<ushort>.Count)
            {
                var sample = Vector128.Create(samples.Slice(i, Vector128<ushort>.Count));
                var edgeIdx = two + Sign(sample, Vector128.Create(neighbours0.Slice(i, Vector128<ushort>.Count))) + Sign(sample, Vector128.Create(neighbours1.Slice(i, Vector128<ushort>.Count)));
                AddOffsets(sample, Lookup(table, edgeIdx), maxValue).CopyTo(destination[i..]);
            }
        }

        if (i < samples.Length)
        {
            EdgeOffsetRowScalar(samples[i..], neighbours0[i..], neighbours1[i..], destination[i..], offsets, maxValue);
        }

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
    /// offsets 1 to 4 of <paramref name="offsets"/>, the other ones the offset 0. <paramref name="destination"/> may be
    /// <paramref name="samples"/>.
    /// </summary>
    internal static void BandOffsetRow(ReadOnlySpan<ushort> samples, Span<ushort> destination, int bandShift, int saoLeftClass, ReadOnlySpan<int> offsets, int maxValue)
    {
        var i = 0;
        if (Vector128.IsHardwareAccelerated && samples.Length >= Vector128<ushort>.Count)
        {
            // The band k places after saoLeftClass, modulo 32: its offset is offsets[ k + 1 ] for k from 0 to 3, and offsets[ 0 ]
            // from 4, which the table holds at index 4
            var leftClass = Vector128.Create((short)saoLeftClass);
            var mask = Vector128.Create((short)31);
            var four = Vector128.Create((short)4);
            var table = Vector128.Create((short)offsets[1], (short)offsets[2], (short)offsets[3], (short)offsets[4], (short)offsets[0], 0, 0, 0).AsByte();
            for (; i <= samples.Length - Vector128<ushort>.Count; i += Vector128<ushort>.Count)
            {
                var sample = Vector128.Create(samples.Slice(i, Vector128<ushort>.Count));
                var k = (Vector128.ShiftRightLogical(sample, bandShift).AsInt16() - leftClass) & mask;
                AddOffsets(sample, Lookup(table, Vector128.Min(k, four)), maxValue).CopyTo(destination[i..]);
            }
        }

        if (i < samples.Length)
        {
            BandOffsetRowScalar(samples[i..], destination[i..], bandShift, saoLeftClass, offsets, maxValue);
        }
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

    // The 16-bit values of a table of at most 8 entries at the indexes of the lanes, which are between 0 and 7: one byte
    // shuffle, whose byte indexes are 2 * index and 2 * index + 1 in each lane
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> Lookup(Vector128<byte> table, Vector128<short> indexes)
    {
        var byteIndexes = (indexes * Vector128.Create((short)0x0202)) + Vector128.Create((short)0x0100);
        return Vector128.ShuffleNative(table, byteIndexes.AsByte()).AsInt16();
    }

    // Clip3( 0, maxValue, sample + offset ) for 8 samples: in 16-bit lanes when the sums fit them (bit depths up to 14), in
    // 32-bit ones otherwise
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<ushort> AddOffsets(Vector128<ushort> samples, Vector128<short> offsets, int maxValue)
    {
        if (maxValue <= short.MaxValue >> 1)
            return Vector128.Min(Vector128.Max(samples.AsInt16() + offsets, Vector128<short>.Zero), Vector128.Create((short)maxValue)).AsUInt16();

        var max = Vector128.Create(maxValue);
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
