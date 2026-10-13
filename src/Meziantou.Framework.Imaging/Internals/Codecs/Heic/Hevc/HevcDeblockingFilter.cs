namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The deblocking filter process (Rec. ITU-T H.265, clause 8.7.2): the vertical edges of the whole picture are filtered,
/// then its horizontal edges, with the samples that the vertical edges modified as input.
/// </summary>
/// <remarks>
/// The specification filters the edges of a picture one coding unit at a time. The edges of the transform blocks and of
/// the prediction blocks were recorded for each 4x4 luma block when the picture was decoded
/// (<see cref="HevcPictureState.EdgeFlags"/>): the filter visits the segments of 4 samples of the edges that are on the
/// 8x8 luma sample grid. The edges of one direction do not share any sample they modify, so their order does not matter.
/// </remarks>
internal ref struct HevcDeblockingFilter
{
    private readonly HevcPicture _picture;
    private readonly HevcPictureState _state;
    private readonly HevcSequenceParameterSet _sps;
    private readonly HevcPictureParameterSet _pps;
    private readonly ReadOnlySpan<byte> _cuFlags;
    private readonly ReadOnlySpan<sbyte> _qpY;
    private readonly ReadOnlySpan<byte> _edgeFlags;
    private readonly ReadOnlySpan<int> _sliceIndex;
    private readonly ReadOnlySpan<int> _ctbAddrRsToTs;
    private readonly ReadOnlySpan<int> _tileId;
    private readonly int _bitDepth;
    private readonly bool _pcmLoopFilterDisabled;
    private HevcTools _tools;

    private HevcDeblockingFilter(HevcPicture picture, HevcPictureLayout layout, HevcPictureState state)
    {
        _picture = picture;
        _state = state;
        _sps = layout.Sps;
        _pps = layout.Pps;
        _cuFlags = state.CuFlags;
        _qpY = state.QpY;
        _edgeFlags = state.EdgeFlags;
        _sliceIndex = state.SliceIndex;
        _ctbAddrRsToTs = layout.CtbAddrRsToTs;
        _tileId = layout.TileId;
        _bitDepth = _sps.BitDepth;
        _pcmLoopFilterDisabled = _sps.PcmEnabledFlag && _sps.PcmLoopFilterDisabledFlag;
        _tools = state.Tools;
    }

    /// <summary>Filters the picture, whose slices are all decoded.</summary>
    public static void Apply(HevcPicture picture, HevcPictureLayout layout, HevcPictureState state, CancellationToken cancellationToken)
    {
        var enabled = false;
        foreach (var slice in state.Slices)
        {
            enabled |= !slice.SliceDeblockingFilterDisabledFlag;
        }

        if (!enabled)
            return;

        var filter = new HevcDeblockingFilter(picture, layout, state);
        filter.FilterEdges(vertical: true, cancellationToken);
        filter.FilterEdges(vertical: false, cancellationToken);
        state.Tools = filter._tools;
    }

    private void FilterEdges(bool vertical, CancellationToken cancellationToken)
    {
        var width4 = _sps.PicWidthInLumaSamples >> 2;
        var height4 = _sps.PicHeightInLumaSamples >> 2;
        var stride4 = _state.WidthIn4x4;
        var mask = vertical
            ? HevcPictureState.EdgeLeftTransform | HevcPictureState.EdgeLeftPrediction
            : HevcPictureState.EdgeTopTransform | HevcPictureState.EdgeTopPrediction;
        var transformMask = vertical ? HevcPictureState.EdgeLeftTransform : HevcPictureState.EdgeTopTransform;
        var ctbLog2SizeY = _sps.CtbLog2SizeY;
        var slices = _state.Slices;
        for (var by = 0; by < height4; by++)
        {
            if ((by & 15) is 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            // Edges that are not on the 8x8 sample grid are not filtered, nor are the edges of the picture
            if (!vertical && ((by & 1) is not 0 || by is 0))
                continue;

            for (var bx = vertical ? 2 : 0; bx < width4; bx += vertical ? 2 : 1)
            {
                var flags = _edgeFlags[(by * stride4) + bx];
                if ((flags & mask) is 0)
                    continue;

                // The samples q0 and p0 of the segment
                var xQ = bx << 2;
                var yQ = by << 2;
                var xP = vertical ? xQ - 1 : xQ;
                var yP = vertical ? yQ : yQ - 1;
                var ctbQ = ((yQ >> ctbLog2SizeY) * _sps.PicWidthInCtbsY) + (xQ >> ctbLog2SizeY);
                var sliceQ = slices[_sliceIndex[ctbQ]];
                if (sliceQ.SliceDeblockingFilterDisabledFlag)
                    continue;

                var ctbP = ((yP >> ctbLog2SizeY) * _sps.PicWidthInCtbsY) + (xP >> ctbLog2SizeY);
                if (ctbP != ctbQ)
                {
                    // filterLeftCbEdgeFlag and filterTopCbEdgeFlag: the boundaries of the tiles and of the slices are
                    // boundaries of coding tree blocks
                    if (!_pps.LoopFilterAcrossTilesEnabledFlag && _tileId[_ctbAddrRsToTs[ctbP]] != _tileId[_ctbAddrRsToTs[ctbQ]])
                        continue;

                    if (!sliceQ.SliceLoopFilterAcrossSlicesEnabledFlag && _sliceIndex[ctbP] != _sliceIndex[ctbQ])
                        continue;
                }

                // Clause 8.7.2.4 "Derivation process of boundary filtering strength"
                var cuP = GetCuFlags(xP, yP);
                var cuQ = GetCuFlags(xQ, yQ);
                int bS;
                if (((cuP | cuQ) & HevcPictureState.CuIntra) is not 0)
                {
                    bS = 2;
                }
                else if ((flags & transformMask) is not 0 && ((flags | _edgeFlags[((yP >> 2) * stride4) + (xP >> 2)]) & HevcPictureState.CodedLuma) is not 0)
                {
                    bS = 1;
                }
                else
                {
                    bS = GetMotionBoundaryStrength(((yP >> 2) * stride4) + (xP >> 2), (by * stride4) + bx, ctbP, ctbQ);
                }

                if (bS is 0)
                    continue;

                var qpQ = GetQpY(xQ, yQ);
                var qpP = GetQpY(xP, yP);
                var filterP = !IsUnfiltered(cuP);
                var filterQ = !IsUnfiltered(cuQ);
                FilterLumaEdge(xQ, yQ, vertical, bS, qpQ, qpP, sliceQ, filterP, filterQ);
                if (bS is 2 && _sps.ChromaArrayType is not 0)
                {
                    FilterChromaEdges(xQ, yQ, vertical, qpQ, qpP, sliceQ, filterP, filterQ);
                }

                _tools |= HevcTools.Deblocking;
            }
        }
    }

    // Clause 8.7.2.4: the prediction blocks of the two sides of an edge refer to different pictures, or have a different
    // number of motion vectors, or motion vectors that differ by a sample or more
    private readonly int GetMotionBoundaryStrength(int indexP, int indexQ, int ctbP, int ctbQ)
    {
        var motion = _state.Motion;
        if (motion.IsEmpty)
            return 0;

        var p = motion[indexP];
        var q = motion[indexQ];
        if (HevcMotion.AreSame(p, q) && _sliceIndex[ctbP] == _sliceIndex[ctbQ])
            return 0;

        var referencesP = _state.SliceReferences[_sliceIndex[ctbP]];
        var referencesQ = _state.SliceReferences[_sliceIndex[ctbQ]];
        if (referencesP is null || referencesQ is null)
            return 1;

        var bothP = p.PredFlags is HevcMotion.Bi;
        var bothQ = q.PredFlags is HevcMotion.Bi;
        if (bothP != bothQ)
            return 1;

        if (!bothP)
        {
            var listP = p.Uses(0) ? 0 : 1;
            var listQ = q.Uses(0) ? 0 : 1;
            if (referencesP.GetPoc(listP, p.GetRefIdx(listP)) != referencesQ.GetPoc(listQ, q.GetRefIdx(listQ)))
                return 1;

            return Differs(p.GetMvX(listP), p.GetMvY(listP), q.GetMvX(listQ), q.GetMvY(listQ)) ? 1 : 0;
        }

        var p0 = referencesP.GetPoc(0, p.RefIdx0);
        var p1 = referencesP.GetPoc(1, p.RefIdx1);
        var q0 = referencesQ.GetPoc(0, q.RefIdx0);
        var q1 = referencesQ.GetPoc(1, q.RefIdx1);
        if (!((p0 == q0 && p1 == q1) || (p0 == q1 && p1 == q0)))
            return 1;

        var same = Differs(p.Mv0X, p.Mv0Y, q.Mv0X, q.Mv0Y) || Differs(p.Mv1X, p.Mv1Y, q.Mv1X, q.Mv1Y);
        var crossed = Differs(p.Mv0X, p.Mv0Y, q.Mv1X, q.Mv1Y) || Differs(p.Mv1X, p.Mv1Y, q.Mv0X, q.Mv0Y);
        if (p0 != p1)
            return (p0 == q0 ? same : crossed) ? 1 : 0;

        // The two motion vectors of each block refer to the same picture
        return same && crossed ? 1 : 0;

        static bool Differs(int x0, int y0, int x1, int y1) => Math.Abs(x0 - x1) >= 4 || Math.Abs(y0 - y1) >= 4;
    }

    private readonly byte GetCuFlags(int x, int y) => _cuFlags[((y >> _state.MinCbLog2SizeY) * _state.WidthInMinCbs) + (x >> _state.MinCbLog2SizeY)];

    private readonly int GetQpY(int x, int y) => _qpY[((y >> _state.MinCbLog2SizeY) * _state.WidthInMinCbs) + (x >> _state.MinCbLog2SizeY)];

    // The samples of a coding unit that the in-loop filters leave as they are: nDp and nDq are set to 0 (clause 8.7.2.5.7)
    private readonly bool IsUnfiltered(byte cuFlags)
    {
        return (cuFlags & HevcPictureState.CuTransquantBypass) is not 0 || (_pcmLoopFilterDisabled && (cuFlags & HevcPictureState.CuPcm) is not 0);
    }

    // Clauses 8.7.2.5.3 "Decision process for luma block edges", 8.7.2.5.4 "Filtering process for luma block edges",
    // 8.7.2.5.6 "Decision process for a luma sample" and 8.7.2.5.7 "Filtering process for a luma sample"
    private void FilterLumaEdge(int xQ, int yQ, bool vertical, int bS, int qpQ, int qpP, HevcSliceSegmentHeader slice, bool filterP, bool filterQ)
    {
        var plane = _picture.GetPlane(0);
        var samples = plane.Samples;

        // The sample i of the line k of the segment is at origin + k * lineStep + i * sampleStep; p is on the negative side
        var origin = (yQ * plane.Stride) + xQ;
        var sampleStep = vertical ? 1 : plane.Stride;
        var lineStep = vertical ? plane.Stride : 1;

        // Equations 8-347 to 8-351
        var qPL = (qpQ + qpP + 1) >> 1;
        var beta = HevcQuantizationTables.BetaPrime[Math.Clamp(qPL + (slice.SliceBetaOffsetDiv2 << 1), 0, 51)] * (1 << (_bitDepth - 8));
        var tC = HevcQuantizationTables.TcPrime[Math.Clamp(qPL + (2 * (bS - 1)) + (slice.SliceTcOffsetDiv2 << 1), 0, 53)] * (1 << (_bitDepth - 8));

        // Equations 8-352 to 8-369: the lines 0 and 3 of the segment
        var line0 = origin;
        var line3 = origin + (3 * lineStep);
        var dp0 = Math.Abs(samples[line0 - (3 * sampleStep)] - (2 * samples[line0 - (2 * sampleStep)]) + samples[line0 - sampleStep]);
        var dp3 = Math.Abs(samples[line3 - (3 * sampleStep)] - (2 * samples[line3 - (2 * sampleStep)]) + samples[line3 - sampleStep]);
        var dq0 = Math.Abs(samples[line0 + (2 * sampleStep)] - (2 * samples[line0 + sampleStep]) + samples[line0]);
        var dq3 = Math.Abs(samples[line3 + (2 * sampleStep)] - (2 * samples[line3 + sampleStep]) + samples[line3]);
        var dpq0 = dp0 + dq0;
        var dpq3 = dp3 + dq3;
        var dp = dp0 + dp3;
        var dq = dq0 + dq3;
        var d = dpq0 + dpq3;
        if (d >= beta)
            return;

        var dSam0 = DecideLumaSample(samples, line0, sampleStep, 2 * dpq0, beta, tC);
        var dSam3 = DecideLumaSample(samples, line3, sampleStep, 2 * dpq3, beta, tC);
        var dE = dSam0 && dSam3 ? 2 : 1;
        var dEp = dp < (beta + (beta >> 1)) >> 3;
        var dEq = dq < (beta + (beta >> 1)) >> 3;
        var maxValue = (1 << _bitDepth) - 1;
        for (var k = 0; k < 4; k++)
        {
            var q = origin + (k * lineStep);
            int p0 = samples[q - sampleStep];
            int p1 = samples[q - (2 * sampleStep)];
            int p2 = samples[q - (3 * sampleStep)];
            int p3 = samples[q - (4 * sampleStep)];
            int q0 = samples[q];
            int q1 = samples[q + sampleStep];
            int q2 = samples[q + (2 * sampleStep)];
            int q3 = samples[q + (3 * sampleStep)];
            if (dE is 2)
            {
                // Equations 8-389 to 8-394: the strong filtering
                if (filterP)
                {
                    samples[q - sampleStep] = (ushort)Math.Clamp((p2 + (2 * p1) + (2 * p0) + (2 * q0) + q1 + 4) >> 3, p0 - (2 * tC), p0 + (2 * tC));
                    samples[q - (2 * sampleStep)] = (ushort)Math.Clamp((p2 + p1 + p0 + q0 + 2) >> 2, p1 - (2 * tC), p1 + (2 * tC));
                    samples[q - (3 * sampleStep)] = (ushort)Math.Clamp(((2 * p3) + (3 * p2) + p1 + p0 + q0 + 4) >> 3, p2 - (2 * tC), p2 + (2 * tC));
                }

                if (filterQ)
                {
                    samples[q] = (ushort)Math.Clamp((p1 + (2 * p0) + (2 * q0) + (2 * q1) + q2 + 4) >> 3, q0 - (2 * tC), q0 + (2 * tC));
                    samples[q + sampleStep] = (ushort)Math.Clamp((p0 + q0 + q1 + q2 + 2) >> 2, q1 - (2 * tC), q1 + (2 * tC));
                    samples[q + (2 * sampleStep)] = (ushort)Math.Clamp((p0 + q0 + q1 + (3 * q2) + (2 * q3) + 4) >> 3, q2 - (2 * tC), q2 + (2 * tC));
                }

                _tools |= HevcTools.DeblockingStrongFilter;
                continue;
            }

            // Equations 8-395 to 8-402: the weak filtering
            var delta = ((9 * (q0 - p0)) - (3 * (q1 - p1)) + 8) >> 4;
            if (Math.Abs(delta) >= tC * 10)
                continue;

            delta = Math.Clamp(delta, -tC, tC);
            if (filterP)
            {
                samples[q - sampleStep] = (ushort)Math.Clamp(p0 + delta, 0, maxValue);
                if (dEp)
                {
                    var deltaP = Math.Clamp((((p2 + p0 + 1) >> 1) - p1 + delta) >> 1, -(tC >> 1), tC >> 1);
                    samples[q - (2 * sampleStep)] = (ushort)Math.Clamp(p1 + deltaP, 0, maxValue);
                }
            }

            if (filterQ)
            {
                samples[q] = (ushort)Math.Clamp(q0 - delta, 0, maxValue);
                if (dEq)
                {
                    var deltaQ = Math.Clamp((((q2 + q0 + 1) >> 1) - q1 - delta) >> 1, -(tC >> 1), tC >> 1);
                    samples[q + sampleStep] = (ushort)Math.Clamp(q1 + deltaQ, 0, maxValue);
                }
            }
        }
    }

    // Clause 8.7.2.5.6 "Decision process for a luma sample"
    private static bool DecideLumaSample(ReadOnlySpan<ushort> samples, int q, int sampleStep, int dpq, int beta, int tC)
    {
        int p0 = samples[q - sampleStep];
        int p3 = samples[q - (4 * sampleStep)];
        int q0 = samples[q];
        int q3 = samples[q + (3 * sampleStep)];
        return dpq < beta >> 2 && Math.Abs(p3 - p0) + Math.Abs(q0 - q3) < beta >> 3 && Math.Abs(p0 - q0) < ((5 * tC) + 1) >> 1;
    }

    // Clauses 8.7.2.5.1 and 8.7.2.5.2, the edges of the chroma coding blocks: the chroma samples of a segment of a luma
    // edge whose filtering strength is 2, when the edge is on the 8x8 chroma sample grid
    private readonly void FilterChromaEdges(int xQ, int yQ, bool vertical, int qpQ, int qpP, HevcSliceSegmentHeader slice, bool filterP, bool filterQ)
    {
        var subWidthC = _sps.SubWidthC;
        var subHeightC = _sps.SubHeightC;
        var xC = xQ / subWidthC;
        var yC = yQ / subHeightC;
        if (((vertical ? xC : yC) & 7) is not 0)
            return;

        // The lines of the chroma planes that the 4 luma lines of the segment cover
        var lineCount = vertical ? 4 / subHeightC : 4 / subWidthC;
        for (var cIdx = 1; cIdx <= 2; cIdx++)
        {
            // Clause 8.7.2.5.5 "Filtering process for chroma block edges": Equations 8-382 to 8-384
            var cQpPicOffset = cIdx is 1 ? _pps.CbQpOffset : _pps.CrQpOffset;
            var qPi = ((qpQ + qpP + 1) >> 1) + cQpPicOffset;
            var qpC = _sps.ChromaArrayType is 1 ? HevcSliceCoder.MapChromaQp420(qPi) : Math.Min(qPi, 51);
            var tC = HevcQuantizationTables.TcPrime[Math.Clamp(qpC + 2 + (slice.SliceTcOffsetDiv2 << 1), 0, 53)] * (1 << (_bitDepth - 8));
            if (tC is 0)
                continue;

            var plane = _picture.GetPlane(cIdx);
            var samples = plane.Samples;
            var origin = (yC * plane.Stride) + xC;
            var sampleStep = vertical ? 1 : plane.Stride;
            var lineStep = vertical ? plane.Stride : 1;
            var maxValue = (1 << _bitDepth) - 1;
            for (var k = 0; k < lineCount; k++)
            {
                // Clause 8.7.2.5.8 "Filtering process for a chroma sample"
                var q = origin + (k * lineStep);
                int p0 = samples[q - sampleStep];
                int p1 = samples[q - (2 * sampleStep)];
                int q0 = samples[q];
                int q1 = samples[q + sampleStep];
                var delta = Math.Clamp((((q0 - p0) << 2) + p1 - q1 + 4) >> 3, -tC, tC);
                if (filterP)
                {
                    samples[q - sampleStep] = (ushort)Math.Clamp(p0 + delta, 0, maxValue);
                }

                if (filterQ)
                {
                    samples[q] = (ushort)Math.Clamp(q0 - delta, 0, maxValue);
                }
            }
        }
    }
}
