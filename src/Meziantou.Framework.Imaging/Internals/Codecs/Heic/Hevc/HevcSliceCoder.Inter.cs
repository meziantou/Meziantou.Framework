namespace Meziantou.Framework.Imaging.Internals;

// The coding units coded in inter prediction mode: the syntax of their prediction units, the derivation of their motion
// (clause 8.5.3.2 "Derivation process for motion vector components and reference indices") and their prediction samples
// (clause 8.5.3.3 "Decoding process for inter prediction samples")
internal ref partial struct HevcSliceCoder
{
    private const int PredL0 = 0;
    private const int PredL1 = 1;
    private const int PredBi = 2;

    // cu_skip_flag: clause 9.3.4.2.2 "Derivation process of ctxInc using left and above syntax elements"
    private bool ReadCuSkipFlag(int x0, int y0)
    {
        return _cabac.DecodeDecision(HevcContextLayout.CuSkipFlag + GetCuSkipFlagCtxInc(x0, y0)) is not 0;
    }

    private readonly int GetCuSkipFlagCtxInc(int x0, int y0)
    {
        var ctxInc = 0;
        if (IsAvailable(x0, y0, x0 - 1, y0) && (GetCuFlags(x0 - 1, y0) & HevcPictureState.CuSkip) is not 0)
        {
            ctxInc++;
        }

        if (IsAvailable(x0, y0, x0, y0 - 1) && (GetCuFlags(x0, y0 - 1) & HevcPictureState.CuSkip) is not 0)
        {
            ctxInc++;
        }

        return ctxInc;
    }

    // part_mode of a coding unit coded in inter prediction mode (Table 9-45): the third bin has a context of its own when
    // it tells the asymmetric partitions apart, and the fourth bin is a bypass bin
    private int ReadPartModeInter(int log2CbSize)
    {
        if (_cabac.DecodeDecision(HevcContextLayout.PartMode) is not 0)
            return HevcConstants.Part2Nx2N;

        if (log2CbSize == _minCbLog2SizeY)
        {
            if (_cabac.DecodeDecision(HevcContextLayout.PartMode + 1) is not 0)
                return HevcConstants.Part2NxN;

            if (log2CbSize is 3 || _cabac.DecodeDecision(HevcContextLayout.PartMode + 2) is not 0)
                return HevcConstants.PartNx2N;

            return HevcConstants.PartNxN;
        }

        var horizontal = _cabac.DecodeDecision(HevcContextLayout.PartMode + 1) is not 0;
        if (!_sps.AmpEnabledFlag || _cabac.DecodeDecision(HevcContextLayout.PartMode + 3) is not 0)
            return horizontal ? HevcConstants.Part2NxN : HevcConstants.PartNx2N;

        var second = _cabac.DecodeBypass() is not 0;
        if (horizontal)
            return second ? HevcConstants.Part2NxnD : HevcConstants.Part2NxnU;

        return second ? HevcConstants.PartnRx2N : HevcConstants.PartnLx2N;
    }

    // The prediction units of a coding unit (clause 7.3.8.5), each one decoded as it is read: the motion of a prediction
    // unit is a neighbour of the ones that follow it. Returns merge_flag of the first prediction unit.
    private bool DecodePredictionUnits(int x0, int y0, int nCbS, int partMode, bool skip)
    {
        var half = nCbS >> 1;
        var quarter = nCbS >> 2;
        bool mergeFlag;
        switch (partMode)
        {
            case HevcConstants.Part2NxN:
                mergeFlag = PredictionUnit(x0, y0, nCbS, half, 0, skip);
                PredictionUnit(x0, y0 + half, nCbS, half, 1, skip);
                _tools |= HevcTools.InterRectangularPartitions;
                break;

            case HevcConstants.PartNx2N:
                mergeFlag = PredictionUnit(x0, y0, half, nCbS, 0, skip);
                PredictionUnit(x0 + half, y0, half, nCbS, 1, skip);
                _tools |= HevcTools.InterRectangularPartitions;
                break;

            case HevcConstants.Part2NxnU:
                mergeFlag = PredictionUnit(x0, y0, nCbS, quarter, 0, skip);
                PredictionUnit(x0, y0 + quarter, nCbS, nCbS - quarter, 1, skip);
                _tools |= HevcTools.InterAsymmetricPartitions;
                break;

            case HevcConstants.Part2NxnD:
                mergeFlag = PredictionUnit(x0, y0, nCbS, nCbS - quarter, 0, skip);
                PredictionUnit(x0, y0 + nCbS - quarter, nCbS, quarter, 1, skip);
                _tools |= HevcTools.InterAsymmetricPartitions;
                break;

            case HevcConstants.PartnLx2N:
                mergeFlag = PredictionUnit(x0, y0, quarter, nCbS, 0, skip);
                PredictionUnit(x0 + quarter, y0, nCbS - quarter, nCbS, 1, skip);
                _tools |= HevcTools.InterAsymmetricPartitions;
                break;

            case HevcConstants.PartnRx2N:
                mergeFlag = PredictionUnit(x0, y0, nCbS - quarter, nCbS, 0, skip);
                PredictionUnit(x0 + nCbS - quarter, y0, quarter, nCbS, 1, skip);
                _tools |= HevcTools.InterAsymmetricPartitions;
                break;

            case HevcConstants.PartNxN:
                mergeFlag = PredictionUnit(x0, y0, half, half, 0, skip);
                PredictionUnit(x0 + half, y0, half, half, 1, skip);
                PredictionUnit(x0, y0 + half, half, half, 2, skip);
                PredictionUnit(x0 + half, y0 + half, half, half, 3, skip);
                _tools |= HevcTools.InterNxN;
                break;

            default:
                mergeFlag = PredictionUnit(x0, y0, nCbS, nCbS, 0, skip);
                break;
        }

        return mergeFlag;
    }

    // Clause 7.3.8.6 "Prediction unit syntax" and clause 8.5.3.1. Returns merge_flag.
    private bool PredictionUnit(int xPb, int yPb, int nPbW, int nPbH, int partIdx, bool skip)
    {
        var refs = _refs!;
        HevcMotion motion = default;
        var mergeFlag = skip || _cabac.DecodeDecision(HevcContextLayout.MergeFlag) is not 0;
        if (mergeFlag)
        {
            // merge_idx: a truncated Rice binarization with cMax equal to MaxNumMergeCand - 1, whose first bin only has a
            // context
            var mergeIdx = 0;
            var cMax = _header.MaxNumMergeCand - 1;
            if (cMax > 0 && _cabac.DecodeDecision(HevcContextLayout.MergeIdx) is not 0)
            {
                mergeIdx = 1;
                while (mergeIdx < cMax && _cabac.DecodeBypass() is not 0)
                {
                    mergeIdx++;
                }
            }

            motion = DeriveMergeMotion(xPb, yPb, nPbW, nPbH, partIdx, mergeIdx);
            _tools |= skip ? HevcTools.InterSkip : HevcTools.InterMerge;
        }
        else
        {
            var interPredIdc = PredL0;
            if (_header.SliceType is HevcConstants.SliceB)
            {
                // inter_pred_idc (Table 9-47): the prediction blocks of 8x4 and 4x8 samples have one reference picture
                if (nPbW + nPbH is not 12 && _cabac.DecodeDecision(HevcContextLayout.InterPredIdc + _ctDepth) is not 0)
                {
                    interPredIdc = PredBi;
                }
                else
                {
                    interPredIdc = _cabac.DecodeDecision(HevcContextLayout.InterPredIdc + 4) is not 0 ? PredL1 : PredL0;
                }
            }

            Span<int> refIdx = stackalloc int[2];
            Span<int> mvd = stackalloc int[4];
            Span<bool> mvpFlag = stackalloc bool[2];
            for (var list = 0; list < 2; list++)
            {
                if (interPredIdc == (list is 0 ? PredL1 : PredL0))
                    continue;

                // ref_idx_l0, ref_idx_l1: a truncated Rice binarization whose first two bins have a context
                var cMax = refs.GetCount(list) - 1;
                var value = 0;
                while (value < cMax && (value < 2 ? _cabac.DecodeDecision(HevcContextLayout.RefIdx + value) : _cabac.DecodeBypass()) is not 0)
                {
                    value++;
                }

                refIdx[list] = value;
                if (!(list is 1 && _header.MvdL1ZeroFlag && interPredIdc is PredBi))
                {
                    ReadMvd(out mvd[list * 2], out mvd[(list * 2) + 1]);
                }

                mvpFlag[list] = _cabac.DecodeDecision(HevcContextLayout.MvpFlag) is not 0;
            }

            for (var list = 0; list < 2; list++)
            {
                if (interPredIdc == (list is 0 ? PredL1 : PredL0))
                    continue;

                // Equations 8-94 to 8-101: the sum wraps around 16 bits
                DeriveMvp(xPb, yPb, nPbW, nPbH, partIdx, list, refIdx[list], mvpFlag[list], out var mvpX, out var mvpY);
                motion.Set(list, refIdx[list], (short)(mvpX + mvd[list * 2]), (short)(mvpY + mvd[(list * 2) + 1]));
            }

            _tools |= HevcTools.InterMvd;
        }

        if (motion.PredFlags is HevcMotion.Bi)
        {
            _tools |= HevcTools.InterBiPrediction;
        }

        StorePredictionUnit(xPb, yPb, nPbW, nPbH, motion);
        PredictInter(xPb, yPb, nPbW, nPbH, motion);
        return mergeFlag;
    }

    // The motion of the prediction block, for the blocks that follow it and for the deblocking filter
    private readonly void StorePredictionUnit(int xPb, int yPb, int nPbW, int nPbH, HevcMotion motion)
    {
        var count = nPbW >> 2;
        var index = ((yPb >> 2) * _widthIn4x4) + (xPb >> 2);
        for (var y = 0; y < nPbH >> 2; y++)
        {
            _motion.Slice(index + (y * _widthIn4x4), count).Fill(motion);
            _edgeFlags[index + (y * _widthIn4x4)] |= HevcPictureState.EdgeLeftPrediction;
        }

        for (var x = 0; x < count; x++)
        {
            _edgeFlags[index + x] |= HevcPictureState.EdgeTopPrediction;
        }
    }

    // Clause 7.3.8.9 "Motion vector difference syntax" and clause 7.4.9.9: lMvd[ 0 ] and lMvd[ 1 ]
    private void ReadMvd(out int mvdX, out int mvdY)
    {
        var greater0X = _cabac.DecodeDecision(HevcContextLayout.AbsMvdGreater0Flag) is not 0;
        var greater0Y = _cabac.DecodeDecision(HevcContextLayout.AbsMvdGreater0Flag) is not 0;
        var greater1X = greater0X && _cabac.DecodeDecision(HevcContextLayout.AbsMvdGreater1Flag) is not 0;
        var greater1Y = greater0Y && _cabac.DecodeDecision(HevcContextLayout.AbsMvdGreater1Flag) is not 0;
        mvdX = greater0X ? ReadMvdComponent(greater1X) : 0;
        mvdY = greater0Y ? ReadMvdComponent(greater1Y) : 0;
    }

    private int ReadMvdComponent(bool greater1)
    {
        var abs = 1;
        if (greater1)
        {
            // abs_mvd_minus2: a first order Exp-Golomb binarization of bypass bins
            var k = 1;
            var value = 0;
            while (_cabac.DecodeBypass() is not 0)
            {
                value += 1 << k;
                if (++k > 15)
                    throw HevcErrors.Invalid("abs_mvd_minus2 is out of range.");
            }

            abs = 2 + value + _cabac.DecodeBypassBits(k);
        }

        // mvd_sign_flag. Clause 7.4.9.9: a difference is in the range of -2^15 to 2^15 - 1
        var negative = _cabac.DecodeBypass() is not 0;
        if (abs > (negative ? 32768 : 32767))
            throw HevcErrors.Invalid("a motion vector difference is out of range.");

        return negative ? -abs : abs;
    }

    private readonly HevcMotion GetMotion(int x, int y) => _motion[((y >> 2) * _widthIn4x4) + (x >> 2)];

    // Clause 6.4.2 "Derivation process for prediction block availability"
    private readonly bool IsPredictionBlockAvailable(int xPb, int yPb, int nPbW, int nPbH, int partIdx, int xNbY, int yNbY)
    {
        var nCbS = 1 << _log2CbSize;
        bool available;
        if (xNbY >= _xCb && yNbY >= _yCb && xNbY < _xCb + nCbS && yNbY < _yCb + nCbS)
        {
            // In the coding unit: the second of four prediction blocks does not see the third one, which follows it
            available = !(nPbW << 1 == nCbS && nPbH << 1 == nCbS && partIdx is 1 && _yCb + nPbH <= yNbY && _xCb + nPbW > xNbY);
        }
        else
        {
            available = IsAvailable(xPb, yPb, xNbY, yNbY);
        }

        return available && (GetCuFlags(xNbY, yNbY) & HevcPictureState.CuIntra) is 0;
    }

    private static bool IsSameMergeRegion(int xPb, int yPb, int xNb, int yNb, int log2ParMrgLevel)
    {
        return xPb >> log2ParMrgLevel == xNb >> log2ParMrgLevel && yPb >> log2ParMrgLevel == yNb >> log2ParMrgLevel;
    }

    // Clause 8.5.3.2.2 "Derivation process for luma motion vectors for merge mode"
    private HevcMotion DeriveMergeMotion(int xPb, int yPb, int nPbW, int nPbH, int partIdx, int mergeIdx)
    {
        var refs = _refs!;
        var nCbS = 1 << _log2CbSize;
        var restrictedToOneList = nPbW + nPbH is 12;
        var parMrgLevel = _pps.Log2ParMrgLevel;
        if (parMrgLevel > 2 && nCbS is 8)
        {
            // singleMCLFlag: the prediction units of the coding unit share the candidates of the 2Nx2N prediction unit
            xPb = _xCb;
            yPb = _yCb;
            nPbW = nCbS;
            nPbH = nCbS;
            partIdx = 0;
        }

        var isB = _header.SliceType is HevcConstants.SliceB;
        var maxNumMergeCand = _header.MaxNumMergeCand;
        Span<HevcMotion> candidates = stackalloc HevcMotion[6];
        var count = 0;

        // Clause 8.5.3.2.3 "Derivation process for spatial merging candidates"
        var xA1 = xPb - 1;
        var yA1 = yPb + nPbH - 1;
        HevcMotion a1 = default;
        var availableA1 = !IsSameMergeRegion(xPb, yPb, xA1, yA1, parMrgLevel)
            && !(partIdx is 1 && _partMode is HevcConstants.PartNx2N or HevcConstants.PartnLx2N or HevcConstants.PartnRx2N)
            && IsPredictionBlockAvailable(xPb, yPb, nPbW, nPbH, partIdx, xA1, yA1);
        if (availableA1)
        {
            a1 = GetMotion(xA1, yA1);
            candidates[count++] = a1;
        }

        var xB1 = xPb + nPbW - 1;
        var yB1 = yPb - 1;
        HevcMotion b1 = default;
        var availableB1 = !IsSameMergeRegion(xPb, yPb, xB1, yB1, parMrgLevel)
            && !(partIdx is 1 && _partMode is HevcConstants.Part2NxN or HevcConstants.Part2NxnU or HevcConstants.Part2NxnD)
            && IsPredictionBlockAvailable(xPb, yPb, nPbW, nPbH, partIdx, xB1, yB1);
        var availableFlagB1 = false;
        if (availableB1)
        {
            b1 = GetMotion(xB1, yB1);
            availableFlagB1 = !(availableA1 && HevcMotion.AreSame(a1, b1));
            if (availableFlagB1)
            {
                candidates[count++] = b1;
            }
        }

        var xB0 = xPb + nPbW;
        var yB0 = yPb - 1;
        var availableFlagB0 = false;
        if (!IsSameMergeRegion(xPb, yPb, xB0, yB0, parMrgLevel) && IsPredictionBlockAvailable(xPb, yPb, nPbW, nPbH, partIdx, xB0, yB0))
        {
            var b0 = GetMotion(xB0, yB0);
            availableFlagB0 = !(availableB1 && HevcMotion.AreSame(b1, b0));
            if (availableFlagB0)
            {
                candidates[count++] = b0;
            }
        }

        var xA0 = xPb - 1;
        var yA0 = yPb + nPbH;
        var availableFlagA0 = false;
        if (!IsSameMergeRegion(xPb, yPb, xA0, yA0, parMrgLevel) && IsPredictionBlockAvailable(xPb, yPb, nPbW, nPbH, partIdx, xA0, yA0))
        {
            var a0 = GetMotion(xA0, yA0);
            availableFlagA0 = !(availableA1 && HevcMotion.AreSame(a1, a0));
            if (availableFlagA0)
            {
                candidates[count++] = a0;
            }
        }

        var xB2 = xPb - 1;
        var yB2 = yPb - 1;
        if (count < 4 && !IsSameMergeRegion(xPb, yPb, xB2, yB2, parMrgLevel) && IsPredictionBlockAvailable(xPb, yPb, nPbW, nPbH, partIdx, xB2, yB2))
        {
            var b2 = GetMotion(xB2, yB2);
            if (!(availableA1 && HevcMotion.AreSame(a1, b2)) && !(availableB1 && HevcMotion.AreSame(b1, b2)))
            {
                candidates[count++] = b2;
            }
        }

        // The temporal merging candidate: its reference indices are 0 (step 2), and it is not derived when the candidate
        // that is asked for is known already
        if (count <= mergeIdx && _header.SliceTemporalMvpEnabledFlag)
        {
            HevcMotion col = default;
            if (TryGetTemporalMv(xPb, yPb, nPbW, nPbH, 0, 0, out var mvX, out var mvY))
            {
                col.Set(0, 0, mvX, mvY);
            }

            if (isB && TryGetTemporalMv(xPb, yPb, nPbW, nPbH, 1, 0, out mvX, out mvY))
            {
                col.Set(1, 0, mvX, mvY);
            }

            if (col.PredFlags is not 0)
            {
                candidates[count++] = col;
                _tools |= HevcTools.TemporalMvp;
            }
        }

        // Clause 8.5.3.2.4 "Derivation process for combined bi-predictive merging candidates"
        var numOrigMergeCand = count;
        if (isB && count <= mergeIdx && numOrigMergeCand > 1 && numOrigMergeCand < maxNumMergeCand)
        {
            // Table 8-7 - Specification of l0CandIdx and l1CandIdx
            ReadOnlySpan<byte> l0CandIdx = [0, 1, 0, 2, 1, 2, 0, 3, 1, 3, 2, 3];
            ReadOnlySpan<byte> l1CandIdx = [1, 0, 2, 0, 2, 1, 3, 0, 3, 1, 3, 2];
            for (var combIdx = 0; combIdx < numOrigMergeCand * (numOrigMergeCand - 1) && count < maxNumMergeCand; combIdx++)
            {
                var l0Cand = candidates[l0CandIdx[combIdx]];
                var l1Cand = candidates[l1CandIdx[combIdx]];
                if (!l0Cand.Uses(0) || !l1Cand.Uses(1))
                    continue;

                if (refs.GetPoc(0, l0Cand.RefIdx0) == refs.GetPoc(1, l1Cand.RefIdx1) && l0Cand.Mv0X == l1Cand.Mv1X && l0Cand.Mv0Y == l1Cand.Mv1Y)
                    continue;

                HevcMotion combined = default;
                combined.Set(0, l0Cand.RefIdx0, l0Cand.Mv0X, l0Cand.Mv0Y);
                combined.Set(1, l1Cand.RefIdx1, l1Cand.Mv1X, l1Cand.Mv1Y);
                candidates[count++] = combined;
                _tools |= HevcTools.MergeCombinedCandidates;
            }
        }

        HevcMotion motion;
        if (mergeIdx < count)
        {
            motion = candidates[mergeIdx];
        }
        else
        {
            // Clause 8.5.3.2.5 "Derivation process for zero motion vector merging candidates"
            var numRefIdx = isB ? Math.Min(refs.GetCount(0), refs.GetCount(1)) : refs.GetCount(0);
            var zeroIdx = mergeIdx - count;
            var refIdx = zeroIdx < numRefIdx ? zeroIdx : 0;
            motion = default;
            motion.Set(0, refIdx, 0, 0);
            if (isB)
            {
                motion.Set(1, refIdx, 0, 0);
            }
        }

        if (restrictedToOneList && motion.PredFlags is HevcMotion.Bi)
        {
            // Step 10: a prediction block of 8x4 or 4x8 samples has one reference picture
            motion.Clear(1);
        }

        return motion;
    }

    // Clause 8.5.3.2.6 "Derivation process for luma motion vector prediction" and clause 8.5.3.2.7 "Derivation process for
    // motion vector predictor candidates"
    private void DeriveMvp(int xPb, int yPb, int nPbW, int nPbH, int partIdx, int list, int refIdx, bool mvpFlag, out int mvpX, out int mvpY)
    {
        var refs = _refs!;
        var targetPoc = refs.GetPoc(list, refIdx);
        var targetLongTerm = refs.IsLongTerm(list, refIdx);
        var currPoc = _picture.PicOrderCntVal;

        // The candidate A, from the blocks A0 and A1 to the left
        Span<int> xNbA = [xPb - 1, xPb - 1];
        Span<int> yNbA = [yPb + nPbH, yPb + nPbH - 1];
        Span<bool> availableNbA = stackalloc bool[2];
        availableNbA[0] = IsPredictionBlockAvailable(xPb, yPb, nPbW, nPbH, partIdx, xNbA[0], yNbA[0]);
        availableNbA[1] = IsPredictionBlockAvailable(xPb, yPb, nPbW, nPbH, partIdx, xNbA[1], yNbA[1]);
        var isScaledFlag = availableNbA[0] || availableNbA[1];
        var availableFlagA = false;
        var mvAx = 0;
        var mvAy = 0;
        for (var k = 0; k < 2 && !availableFlagA; k++)
        {
            if (availableNbA[k])
            {
                availableFlagA = TryGetSameReferenceMv(GetMotion(xNbA[k], yNbA[k]), list, targetPoc, out mvAx, out mvAy);
            }
        }

        for (var k = 0; k < 2 && !availableFlagA; k++)
        {
            if (availableNbA[k])
            {
                availableFlagA = TryGetScaledMv(GetMotion(xNbA[k], yNbA[k]), list, currPoc, targetPoc, targetLongTerm, out mvAx, out mvAy);
            }
        }

        // The candidate B, from the blocks B0, B1 and B2 above
        Span<int> xNbB = [xPb + nPbW, xPb + nPbW - 1, xPb - 1];
        Span<bool> availableNbB = stackalloc bool[3];
        var availableFlagB = false;
        var mvBx = 0;
        var mvBy = 0;
        for (var k = 0; k < 3; k++)
        {
            availableNbB[k] = IsPredictionBlockAvailable(xPb, yPb, nPbW, nPbH, partIdx, xNbB[k], yPb - 1);
            if (availableNbB[k] && !availableFlagB)
            {
                availableFlagB = TryGetSameReferenceMv(GetMotion(xNbB[k], yPb - 1), list, targetPoc, out mvBx, out mvBy);
            }
        }

        if (!isScaledFlag)
        {
            if (availableFlagB)
            {
                // Step 5: without a block to the left, the candidate B is also the candidate A
                availableFlagA = true;
                mvAx = mvBx;
                mvAy = mvBy;
            }

            // Steps 6 and 7: the candidate B is derived again, with a scaled motion vector when its reference picture is
            // not the one of the prediction block
            availableFlagB = false;
            for (var k = 0; k < 3 && !availableFlagB; k++)
            {
                if (availableNbB[k])
                {
                    availableFlagB = TryGetScaledMv(GetMotion(xNbB[k], yPb - 1), list, currPoc, targetPoc, targetLongTerm, out mvBx, out mvBy);
                }
            }
        }

        // The candidate list: A, B when it differs from A, the temporal candidate when one is missing, then zero
        Span<int> candidates = stackalloc int[4];
        var count = 0;
        if (availableFlagA)
        {
            candidates[0] = mvAx;
            candidates[1] = mvAy;
            count = 1;
        }

        if (availableFlagB && !(availableFlagA && mvAx == mvBx && mvAy == mvBy))
        {
            candidates[count * 2] = mvBx;
            candidates[(count * 2) + 1] = mvBy;
            count++;
        }

        var index = mvpFlag ? 1 : 0;
        if (count <= index && count < 2 && _header.SliceTemporalMvpEnabledFlag && TryGetTemporalMv(xPb, yPb, nPbW, nPbH, list, refIdx, out var mvColX, out var mvColY))
        {
            candidates[count * 2] = mvColX;
            candidates[(count * 2) + 1] = mvColY;
            count++;
            _tools |= HevcTools.TemporalMvp;
        }

        mvpX = candidates[index * 2];
        mvpY = candidates[(index * 2) + 1];
    }

    // The motion vector of a neighbouring block that refers to the reference picture of the prediction block, from the
    // list of the prediction block or else from the other one
    private readonly bool TryGetSameReferenceMv(HevcMotion neighbour, int list, int targetPoc, out int mvX, out int mvY)
    {
        var refs = _refs!;
        for (var i = 0; i < 2; i++)
        {
            var candidateList = i is 0 ? list : 1 - list;
            if (neighbour.Uses(candidateList) && refs.GetPoc(candidateList, neighbour.GetRefIdx(candidateList)) == targetPoc)
            {
                mvX = neighbour.GetMvX(candidateList);
                mvY = neighbour.GetMvY(candidateList);
                return true;
            }
        }

        mvX = 0;
        mvY = 0;
        return false;
    }

    // The motion vector of a neighbouring block whose reference picture is of the kind (short-term or long-term) of the
    // one of the prediction block, scaled by the distances of the two short-term reference pictures
    private bool TryGetScaledMv(HevcMotion neighbour, int list, int currPoc, int targetPoc, bool targetLongTerm, out int mvX, out int mvY)
    {
        var refs = _refs!;
        for (var i = 0; i < 2; i++)
        {
            var candidateList = i is 0 ? list : 1 - list;
            if (!neighbour.Uses(candidateList))
                continue;

            var candidateRefIdx = neighbour.GetRefIdx(candidateList);
            if (refs.IsLongTerm(candidateList, candidateRefIdx) != targetLongTerm)
                continue;

            mvX = neighbour.GetMvX(candidateList);
            mvY = neighbour.GetMvY(candidateList);
            if (!targetLongTerm)
            {
                var td = currPoc - refs.GetPoc(candidateList, candidateRefIdx);
                var tb = currPoc - targetPoc;
                if (td != tb)
                {
                    ScaleMv(ref mvX, ref mvY, td, tb);
                    _tools |= HevcTools.MvpScaling;
                }
            }

            return true;
        }

        mvX = 0;
        mvY = 0;
        return false;
    }

    // Equations 8-179 to 8-183 and 8-203 to 8-207
    private static void ScaleMv(ref int mvX, ref int mvY, int td, int tb)
    {
        td = Math.Clamp(td, -128, 127);
        tb = Math.Clamp(tb, -128, 127);
        if (td is 0)
            return;

        var tx = (16384 + (Math.Abs(td) >> 1)) / td;
        var distScaleFactor = Math.Clamp(((tb * tx) + 32) >> 6, -4096, 4095);
        mvX = ScaleMvComponent(mvX, distScaleFactor);
        mvY = ScaleMvComponent(mvY, distScaleFactor);
    }

    private static int ScaleMvComponent(int mv, int distScaleFactor)
    {
        var product = distScaleFactor * mv;
        return Math.Clamp(Math.Sign(product) * ((Math.Abs(product) + 127) >> 8), -32768, 32767);
    }

    // Clause 8.5.3.2.8 "Derivation process for temporal luma motion vector prediction"
    private readonly bool TryGetTemporalMv(int xPb, int yPb, int nPbW, int nPbH, int list, int refIdx, out short mvX, out short mvY)
    {
        mvX = 0;
        mvY = 0;
        var colPic = _refs!.CollocatedPicture;
        if (colPic is null)
            return false;

        var collocated = colPic.CollocatedMotion;
        if (collocated.IsEmpty)
            return false;

        // The bottom right candidate, in the row of coding tree blocks of the prediction block and in the picture
        var xColBr = xPb + nPbW;
        var yColBr = yPb + nPbH;
        if (yPb >> _ctbLog2SizeY == yColBr >> _ctbLog2SizeY && yColBr < _sps.PicHeightInLumaSamples && xColBr < _sps.PicWidthInLumaSamples
            && TryGetCollocatedMv(colPic, collocated[((yColBr >> 4) * colPic.CollocatedMotionStride) + (xColBr >> 4)], list, refIdx, out mvX, out mvY))
        {
            return true;
        }

        // The central candidate
        var xColCtr = xPb + (nPbW >> 1);
        var yColCtr = yPb + (nPbH >> 1);
        return TryGetCollocatedMv(colPic, collocated[((yColCtr >> 4) * colPic.CollocatedMotionStride) + (xColCtr >> 4)], list, refIdx, out mvX, out mvY);
    }

    // Clause 8.5.3.2.9 "Derivation process for collocated motion vectors"
    private readonly bool TryGetCollocatedMv(HevcPicture colPic, HevcCollocatedMotion colPb, int list, int refIdx, out short mvX, out short mvY)
    {
        mvX = 0;
        mvY = 0;
        var refs = _refs!;
        var predFlags = colPb.Flags & HevcMotion.Bi;
        if (predFlags is 0)
            return false;

        int listCol;
        if (predFlags is HevcMotion.L1)
        {
            listCol = 1;
        }
        else if (predFlags is HevcMotion.L0)
        {
            listCol = 0;
        }
        else
        {
            // Both lists: the one of the motion vector that is derived when no reference picture follows the picture,
            // else the list N, N being the value of collocated_from_l0_flag
            listCol = refs.NoBackwardPredFlag ? list : (_header.CollocatedFromL0Flag ? 1 : 0);
        }

        var targetLongTerm = refs.IsLongTerm(list, refIdx);
        var colLongTerm = ((colPb.Flags >> (HevcCollocatedMotion.LongTermShift + listCol)) & 1) is not 0;
        if (targetLongTerm != colLongTerm)
            return false;

        int x = listCol is 0 ? colPb.Mv0X : colPb.Mv1X;
        int y = listCol is 0 ? colPb.Mv0Y : colPb.Mv1Y;
        var colPocDiff = colPic.PicOrderCntVal - (listCol is 0 ? colPb.RefPoc0 : colPb.RefPoc1);
        var currPocDiff = _picture.PicOrderCntVal - refs.GetPoc(list, refIdx);
        if (!targetLongTerm && colPocDiff != currPocDiff)
        {
            ScaleMv(ref x, ref y, colPocDiff, currPocDiff);
        }

        mvX = (short)x;
        mvY = (short)y;
        return true;
    }

    // Clause 8.5.3.3 "Decoding process for inter prediction samples": the prediction samples of the prediction block are
    // written to the picture, where the residual of the transform units of the coding unit is added to them
    private void PredictInter(int xPb, int yPb, int nPbW, int nPbH, HevcMotion motion)
    {
        var refs = _refs!;
        var table = (_header.SliceType is HevcConstants.SliceP ? _pps.WeightedPredFlag : _pps.WeightedBipredFlag) ? _header.PredWeightTable : null;
        var shift1 = 14 - _bitDepth;
        var offsetShift = _sps.HighPrecisionOffsetsEnabledFlag ? 0 : _bitDepth - 8;
        var bi = motion.PredFlags is HevcMotion.Bi;
        Span<int> weight = stackalloc int[2];
        Span<int> offset = stackalloc int[2];
        for (var cIdx = 0; cIdx < _sps.ComponentCount; cIdx++)
        {
            var subWidth = cIdx is 0 ? 1 : _sps.SubWidthC;
            var subHeight = cIdx is 0 ? 1 : _sps.SubHeightC;
            var width = nPbW / subWidth;
            var height = nPbH / subHeight;
            var x = xPb / subWidth;
            var y = yPb / subHeight;
            for (var list = 0; list < 2; list++)
            {
                if (!motion.Uses(list))
                    continue;

                var reference = refs.GetPicture(list, motion.GetRefIdx(list)).GetPlane(cIdx);
                int mvX = motion.GetMvX(list);
                int mvY = motion.GetMvY(list);
                int xInt;
                int yInt;
                int xFrac;
                int yFrac;
                if (cIdx is 0)
                {
                    // Equations 8-214 to 8-217
                    xInt = x + (mvX >> 2);
                    yInt = y + (mvY >> 2);
                    xFrac = mvX & 3;
                    yFrac = mvY & 3;
                }
                else
                {
                    // Equations 8-210, 8-211 and 8-218 to 8-221: the chroma motion vector is in units of an eighth of a
                    // chroma sample
                    var mvCx = mvX * 2 / subWidth;
                    var mvCy = mvY * 2 / subHeight;
                    xInt = x + (mvCx >> 3);
                    yInt = y + (mvCy >> 3);
                    xFrac = mvCx & 7;
                    yFrac = mvCy & 7;
                }

                if ((xFrac | yFrac) is not 0)
                {
                    _tools |= HevcTools.FractionalInterpolation;
                }

                if (xInt < 3 || yInt < 3 || xInt + width + 4 > reference.Width || yInt + height + 4 > reference.Height)
                {
                    _tools |= HevcTools.ReferencePadding;
                }

                HevcInterPredictor.Predict(reference, xInt, yInt, xFrac, yFrac, width, height, cIdx is 0, _bitDepth, _scratch.GetPrediction(list), _scratch.Intermediate, _scratch.Window);
            }

            var plane = _picture.GetPlane(cIdx);
            var destination = plane.Samples[((y * plane.Stride) + x)..];
            if (table is null)
            {
                // Clause 8.5.3.3.4.2 "Default weighted sample prediction process"
                if (bi)
                {
                    HevcInterPredictor.WeightDefault(_scratch.GetPrediction(0), _scratch.GetPrediction(1), destination, plane.Stride, width, height, _bitDepth);
                }
                else
                {
                    HevcInterPredictor.WeightDefault(_scratch.GetPrediction(motion.Uses(0) ? 0 : 1), destination, plane.Stride, width, height, _bitDepth);
                }

                continue;
            }

            // Clause 8.5.3.3.4.3 "Explicit weighted sample prediction process"
            var log2Wd = (cIdx is 0 ? table.LumaLog2WeightDenom : table.ChromaLog2WeightDenom) + shift1;
            for (var list = 0; list < 2; list++)
            {
                if (!motion.Uses(list))
                    continue;

                var refIdx = motion.GetRefIdx(list);
                weight[list] = cIdx is 0 ? table.GetLumaWeight(list, refIdx) : table.GetChromaWeight(list, refIdx, cIdx - 1);
                offset[list] = (cIdx is 0 ? table.GetLumaOffset(list, refIdx) : table.GetChromaOffset(list, refIdx, cIdx - 1)) << offsetShift;
            }

            if (bi)
            {
                HevcInterPredictor.WeightExplicit(_scratch.GetPrediction(0), _scratch.GetPrediction(1), destination, plane.Stride, width, height, _bitDepth, weight[0], weight[1], offset[0], offset[1], log2Wd);
            }
            else
            {
                var list = motion.Uses(0) ? 0 : 1;
                HevcInterPredictor.WeightExplicit(_scratch.GetPrediction(list), destination, plane.Stride, width, height, _bitDepth, weight[list], offset[list], log2Wd);
            }

            _tools |= HevcTools.ExplicitWeightedPrediction;
        }
    }
}
