namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The sample adaptive offset process (Rec. ITU-T H.265, clause 8.7.3): the samples of each coding tree block are
/// modified from the deblocked samples of the picture.
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
            Span<byte> bandTable = stackalloc byte[32];
            for (var k = 0; k < 4; k++)
            {
                bandTable[(k + saoLeftClass) & 31] = (byte)(k + 1);
            }

            for (var j = 0; j < height; j++)
            {
                var row = (yCtb + j) * stride;
                for (var i = 0; i < width; i++)
                {
                    if (checkUnfiltered && IsUnfiltered(state, sps, (xCtb + i) * subWidth, (yCtb + j) * subHeight))
                        continue;

                    int sample = recPicture[row + xCtb + i];
                    var bandIdx = bandTable[sample >> bandShift];
                    saoPicture[row + xCtb + i] = (ushort)Math.Clamp(sample + saoOffsetVal[bandIdx], 0, maxValue);
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
        for (var j = 0; j < height; j++)
        {
            var row = (yCtb + j) * stride;
            for (var i = 0; i < width; i++)
            {
                if (checkUnfiltered && IsUnfiltered(state, sps, (xCtb + i) * subWidth, (yCtb + j) * subHeight))
                    continue;

                if (!IsSampleUsable(usable, i + hPos0, j + vPos0, width, height, nCtbSw, nCtbSh)
                    || !IsSampleUsable(usable, i + hPos1, j + vPos1, width, height, nCtbSw, nCtbSh))
                {
                    // edgeIdx is 0
                    continue;
                }

                // Equations 8-411 to 8-413
                int sample = recPicture[row + xCtb + i];
                var edgeIdx = 2 + Math.Sign(sample - recPicture[row + (vPos0 * stride) + xCtb + i + hPos0]) + Math.Sign(sample - recPicture[row + (vPos1 * stride) + xCtb + i + hPos1]);
                if (edgeIdx is 0 or 1 or 2)
                {
                    edgeIdx = edgeIdx is 2 ? 0 : edgeIdx + 1;
                }

                saoPicture[row + xCtb + i] = (ushort)Math.Clamp(sample + saoOffsetVal[edgeIdx], 0, maxValue);
            }
        }
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
