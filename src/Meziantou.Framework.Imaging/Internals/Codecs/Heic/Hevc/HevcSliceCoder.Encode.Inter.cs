using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

// The coding units of the pictures that are predicted from the previous one (P slices with one reference picture): the
// candidates of a coding unit are its merge candidates, with and without residual, the motion vectors that a search
// around the predictors finds, for one or two prediction units, and the intra prediction. Each candidate is encoded for
// real, and the motion data are derived by the methods of the decoder (clause 8.5.3.2).
internal ref partial struct HevcSliceCoder
{
    // The candidates of a coding unit: the records of the best one start the records of the coding unit
    [StructLayout(LayoutKind.Auto)]
    private struct CodingUnitCandidates
    {
        public int Start;
        public int BestLength;
        public long BestCost;
        public bool BestIsSkip;
        public bool LastIsBest;
        public int Count;
    }

    // The largest component of a motion vector that the search returns, in quarters of a sample
    private const int MaxMotionVector = 1 << 13;

    // The coding unit of a block of a P slice: returns its cost
    private long SearchInterCodingUnit(int x0, int y0, int log2CbSize, int cqtDepth)
    {
        SaveContexts(HevcEncoderWorkspace.ContextCuStart);
        var candidates = new CodingUnitCandidates { Start = _workspace.RecordCount, BestCost = long.MaxValue };
        if (log2CbSize <= _decisions.MaxInterCbLog2Size)
        {
            SearchMergeCandidates(ref candidates, x0, y0, log2CbSize, cqtDepth);

            // A coding unit that is skipped is predicted well: the lowest efforts look no further
            if (!(candidates.BestIsSkip && _decisions.Effort <= 4))
            {
                SearchMotionCandidate(ref candidates, x0, y0, log2CbSize, cqtDepth, HevcConstants.Part2Nx2N);
                if (_decisions.RectangularPartitions && !candidates.BestIsSkip)
                {
                    SearchMotionCandidate(ref candidates, x0, y0, log2CbSize, cqtDepth, HevcConstants.Part2NxN);
                    SearchMotionCandidate(ref candidates, x0, y0, log2CbSize, cqtDepth, HevcConstants.PartNx2N);
                    if (_sps.AmpEnabledFlag && log2CbSize > _minCbLog2SizeY)
                    {
                        SearchMotionCandidate(ref candidates, x0, y0, log2CbSize, cqtDepth, HevcConstants.Part2NxnU);
                        SearchMotionCandidate(ref candidates, x0, y0, log2CbSize, cqtDepth, HevcConstants.Part2NxnD);
                        SearchMotionCandidate(ref candidates, x0, y0, log2CbSize, cqtDepth, HevcConstants.PartnLx2N);
                        SearchMotionCandidate(ref candidates, x0, y0, log2CbSize, cqtDepth, HevcConstants.PartnRx2N);
                    }
                }
            }
        }

        // The intra prediction, for what the previous picture does not have
        var tryIntra = _decisions.IntraInInterPictures switch
        {
            1 => !candidates.BestIsSkip && log2CbSize <= 4,
            2 => !candidates.BestIsSkip,
            _ => true,
        };
        if (log2CbSize <= _decisions.MaxIntraCbLog2Size && (tryIntra || candidates.Count is 0))
        {
            candidates.LastIsBest = false;
            RestoreContexts(HevcEncoderWorkspace.ContextCuStart);
            SearchIntraCodingUnit(x0, y0, log2CbSize, cqtDepth);
            CommitCandidate(ref candidates, x0, y0, log2CbSize, cqtDepth);
        }

        Debug.Assert(candidates.Count > 0, "A coding unit has a candidate.");
        if (!candidates.LastIsBest)
        {
            // The best candidate: its samples, and the state of the picture that its records give
            RestoreCodingUnitSamples(HevcEncoderWorkspace.SamplesCuBest, x0, y0, 1 << log2CbSize);
            ReplayCodingUnit(candidates.Start, x0, y0, log2CbSize, cqtDepth);
        }

        // The context variables that follow the best candidate: a search that found nothing better has changed them
        RestoreContexts(HevcEncoderWorkspace.ContextCuBest);
        return candidates.BestCost;
    }

    // Writes the coding unit being searched, and keeps it when it costs less than the candidates before it
    private void CommitCandidate(ref CodingUnitCandidates candidates, int x0, int y0, int log2CbSize, int cqtDepth)
    {
        var workspace = _workspace;
        var position = workspace.RecordCount;
        var cost = StoreCodingUnit(x0, y0, log2CbSize, cqtDepth);
        candidates.Count++;
        candidates.LastIsBest = cost < candidates.BestCost;
        if (candidates.LastIsBest)
        {
            var records = workspace.Records;
            var length = workspace.RecordCount - position;
            records.Slice(position, length).CopyTo(records[candidates.Start..]);
            candidates.BestLength = length;
            candidates.BestCost = cost;
            candidates.BestIsSkip = workspace.Skip;
            SaveCodingUnitSamples(HevcEncoderWorkspace.SamplesCuBest, x0, y0, 1 << log2CbSize);
            SaveContexts(HevcEncoderWorkspace.ContextCuBest);
        }

        workspace.RecordCount = candidates.Start + candidates.BestLength;
    }

    // The coding unit being searched becomes a coding unit coded in inter prediction mode, without motion yet
    private void BeginInterCodingUnit(int x0, int y0, int log2CbSize, int cqtDepth, int partMode)
    {
        var workspace = _workspace;
        workspace.ClearCodingUnit();
        workspace.PartMode = partMode;
        _log2CbSize = log2CbSize;
        _xCb = x0;
        _yCb = y0;
        _ctDepth = cqtDepth;
        _isIntra = false;
        _cuTransquantBypassFlag = _decisions.Lossless;
        _partMode = partMode;
        _intraSplitFlag = 0;
        _interSplitFlag = 0;
        _intraChromaPredMode = 0;
        _intraPredModeC = 0;
        RestoreContexts(HevcEncoderWorkspace.ContextCuStart);

        // The coding unit is known to the derivations of its own prediction units
        var flags = (byte)((cqtDepth << HevcPictureState.CuDepthShift) | (_cuTransquantBypassFlag ? HevcPictureState.CuTransquantBypass : 0));
        SetCodingUnit(x0, y0, log2CbSize, flags);
        UpdateQpY(x0, y0);
    }

    // The merge candidates of the coding unit as one prediction unit (clause 8.5.3.2.2): the ones whose prediction is the
    // closest to the source are encoded, without residual (cu_skip_flag) and with one
    private void SearchMergeCandidates(ref CodingUnitCandidates candidates, int x0, int y0, int log2CbSize, int cqtDepth)
    {
        var workspace = _workspace;
        var nCbS = 1 << log2CbSize;
        var maxNumMergeCand = _header.MaxNumMergeCand;
        Span<HevcMotion> motions = stackalloc HevcMotion[5];
        Span<long> costs = stackalloc long[5];
        candidates.LastIsBest = false;
        BeginInterCodingUnit(x0, y0, log2CbSize, cqtDepth, HevcConstants.Part2Nx2N);
        for (var mergeIdx = 0; mergeIdx < maxNumMergeCand; mergeIdx++)
        {
            var motion = DeriveMergeMotion(x0, y0, nCbS, nCbS, 0, mergeIdx);
            motions[mergeIdx] = motion;
            costs[mergeIdx] = long.MaxValue;
            var duplicate = false;
            for (var previous = 0; previous < mergeIdx; previous++)
            {
                duplicate |= HevcMotion.AreSame(motions[previous], motion);
            }

            if (!duplicate && motion.PredFlags is HevcMotion.L0)
            {
                costs[mergeIdx] = _decisions.GetMotionCost(GetMotionSad(x0, y0, nCbS, nCbS, motion.Mv0X, motion.Mv0Y), HevcCost.GetBypassCost(1 + mergeIdx));
            }
        }

        var tries = _decisions.Effort <= 2 ? 1 : _decisions.Effort <= 6 ? 2 : maxNumMergeCand;
        for (var i = 0; i < tries; i++)
        {
            var mergeIdx = costs[..maxNumMergeCand].IndexOf(MinOf(costs[..maxNumMergeCand]));
            if (costs[mergeIdx] is long.MaxValue)
                break;

            costs[mergeIdx] = long.MaxValue;
            BeginInterCodingUnit(x0, y0, log2CbSize, cqtDepth, HevcConstants.Part2Nx2N);
            workspace.Motion[0] = new HevcMotionChoice(MergeFlag: true, mergeIdx, 0, 0, 0);
            StorePredictionUnit(x0, y0, nCbS, nCbS, motions[mergeIdx]);
            PredictInter(x0, y0, nCbS, nCbS, motions[mergeIdx]);
            SearchPredictedCodingUnit(ref candidates, x0, y0, log2CbSize, cqtDepth);
        }
    }

    // A partitioning of the coding unit whose prediction units each have the motion that a search finds, or a merge
    // candidate when it costs less
    private void SearchMotionCandidate(ref CodingUnitCandidates candidates, int x0, int y0, int log2CbSize, int cqtDepth, int partMode)
    {
        var workspace = _workspace;
        var nCbS = 1 << log2CbSize;
        candidates.LastIsBest = false;
        BeginInterCodingUnit(x0, y0, log2CbSize, cqtDepth, partMode);
        var count = partMode is HevcConstants.Part2Nx2N ? 1 : 2;
        for (var partIdx = 0; partIdx < count; partIdx++)
        {
            GetPredictionBlock(partMode, nCbS, partIdx, out var x, out var y, out var nPbW, out var nPbH);
            var xPb = x0 + x;
            var yPb = y0 + y;
            var cost = SearchMotionVector(xPb, yPb, nPbW, nPbH, partIdx, out var motion, out var choice);

            // The merge candidates of the prediction unit: those of the whole coding unit are candidates of their own
            var maxNumMergeCand = _header.MaxNumMergeCand;
            for (var mergeIdx = 0; mergeIdx < maxNumMergeCand; mergeIdx++)
            {
                var merged = DeriveMergeMotion(xPb, yPb, nPbW, nPbH, partIdx, mergeIdx);
                if (merged.PredFlags is not HevcMotion.L0)
                    continue;

                if (count is 1)
                {
                    // The motion of a merge candidate is the candidate that was encoded already
                    if (HevcMotion.AreSame(merged, motion))
                        return;

                    continue;
                }

                var mergeCost = _decisions.GetMotionCost(GetMotionSad(xPb, yPb, nPbW, nPbH, merged.Mv0X, merged.Mv0Y), HevcCost.GetBypassCost(2 + mergeIdx));
                if (mergeCost < cost)
                {
                    cost = mergeCost;
                    motion = merged;
                    choice = new HevcMotionChoice(MergeFlag: true, mergeIdx, 0, 0, 0);
                }
            }

            workspace.Motion[partIdx] = choice;
            StorePredictionUnit(xPb, yPb, nPbW, nPbH, motion);
            PredictInter(xPb, yPb, nPbW, nPbH, motion);
        }

        SearchPredictedCodingUnit(ref candidates, x0, y0, log2CbSize, cqtDepth);
    }

    // The prediction block partIdx of a coding unit (clause 7.4.9.5, Table 7-10): its position in the coding unit and
    // its size
    private static void GetPredictionBlock(int partMode, int nCbS, int partIdx, out int x, out int y, out int nPbW, out int nPbH)
    {
        var half = nCbS >> 1;
        var quarter = nCbS >> 2;
        x = 0;
        y = 0;
        nPbW = nCbS;
        nPbH = nCbS;
        switch (partMode)
        {
            case HevcConstants.Part2NxN:
                nPbH = half;
                y = partIdx * half;
                break;

            case HevcConstants.PartNx2N:
                nPbW = half;
                x = partIdx * half;
                break;

            case HevcConstants.Part2NxnU:
                nPbH = partIdx is 0 ? quarter : nCbS - quarter;
                y = partIdx * quarter;
                break;

            case HevcConstants.Part2NxnD:
                nPbH = partIdx is 0 ? nCbS - quarter : quarter;
                y = partIdx * (nCbS - quarter);
                break;

            case HevcConstants.PartnLx2N:
                nPbW = partIdx is 0 ? quarter : nCbS - quarter;
                x = partIdx * quarter;
                break;

            case HevcConstants.PartnRx2N:
                nPbW = partIdx is 0 ? nCbS - quarter : quarter;
                x = partIdx * (nCbS - quarter);
                break;
        }
    }

    // The coding unit being searched has its motion and its prediction, which is in the picture: it is encoded without
    // residual, then with the residual that the transform trees find (clause 7.3.8.5, rqt_root_cbf)
    private void SearchPredictedCodingUnit(ref CodingUnitCandidates candidates, int x0, int y0, int log2CbSize, int cqtDepth)
    {
        var workspace = _workspace;
        var merged = workspace.PartMode is HevcConstants.Part2Nx2N && workspace.Motion[0].MergeFlag;
        var committed = false;
        SaveCodingUnitSamples(HevcEncoderWorkspace.SamplesPrediction, x0, y0, 1 << log2CbSize);
        if (!_decisions.Lossless)
        {
            // Without residual, the coding unit is its prediction: a merged coding unit is skipped
            workspace.Skip = merged;
            workspace.RqtRootCbf = false;
            CommitCandidate(ref candidates, x0, y0, log2CbSize, cqtDepth);
            committed = true;
            RestoreContexts(HevcEncoderWorkspace.ContextCuStart);
        }

        _maxTrafoDepth = _sps.MaxTransformHierarchyDepthInter;
        _interSplitFlag = _maxTrafoDepth is 0 && workspace.PartMode is not HevcConstants.Part2Nx2N ? 1 : 0;
        SearchLumaTree(x0, y0, log2CbSize, 0, long.MaxValue);
        if (_chromaArrayType is not 0)
        {
            SearchChromaTree(x0, y0, x0, y0, log2CbSize, 0, 0);
        }

        var coded = workspace.GetMap(HevcEncoderWorkspace.MapCbfLuma).ContainsAnyExcept((byte)0) || workspace.GetMap(HevcEncoderWorkspace.MapCbfChroma).ContainsAnyExcept((byte)0);
        if (!coded && committed)
            return;

        workspace.Skip = merged && !coded;
        workspace.RqtRootCbf = coded;
        CommitCandidate(ref candidates, x0, y0, log2CbSize, cqtDepth);
    }

    /// <summary>
    /// Searches the motion vector of a prediction block in the previous picture: from the predictors (clause 8.5.3.2.6),
    /// the motion of the merge candidates and no motion, by steps of whole samples that get smaller, then of a half and of
    /// a quarter of a sample, whose predictions are those of the interpolation of the decoder.
    /// </summary>
    /// <returns>The cost of the motion vector: the difference between its prediction and the source, and the size of its syntax elements.</returns>
    private long SearchMotionVector(int xPb, int yPb, int nPbW, int nPbH, int partIdx, out HevcMotion motion, out HevcMotionChoice choice)
    {
        Span<int> mvp = stackalloc int[4];
        DeriveMvp(xPb, yPb, nPbW, nPbH, partIdx, 0, 0, mvpFlag: false, out mvp[0], out mvp[1]);
        DeriveMvp(xPb, yPb, nPbW, nPbH, partIdx, 0, 0, mvpFlag: true, out mvp[2], out mvp[3]);

        // The motion vectors keep the block close to the reference picture: the samples beyond it repeat its edge. They
        // also keep the motion vector differences in their range of 16 bits (clause 7.4.9.9).
        var minX = Math.Max(-(xPb + nPbW + 8) * 4, -MaxMotionVector);
        var maxX = Math.Min((_sps.PicWidthInLumaSamples - xPb + 8) * 4, MaxMotionVector);
        var minY = Math.Max(-(yPb + nPbH + 8) * 4, -MaxMotionVector);
        var maxY = Math.Min((_sps.PicHeightInLumaSamples - yPb + 8) * 4, MaxMotionVector);
        var bestX = 0;
        var bestY = 0;
        var bestCost = GetMotionVectorCost(xPb, yPb, nPbW, nPbH, 0, 0, mvp);
        for (var i = 0; i < 2; i++)
        {
            TryMotionVector(xPb, yPb, nPbW, nPbH, (mvp[i * 2] + 2) & ~3, (mvp[(i * 2) + 1] + 2) & ~3, minX, maxX, minY, maxY, mvp, ref bestX, ref bestY, ref bestCost);
        }

        var maxNumMergeCand = _header.MaxNumMergeCand;
        for (var mergeIdx = 0; mergeIdx < maxNumMergeCand; mergeIdx++)
        {
            var merged = DeriveMergeMotion(xPb, yPb, nPbW, nPbH, partIdx, mergeIdx);
            if (merged.PredFlags is HevcMotion.L0)
            {
                TryMotionVector(xPb, yPb, nPbW, nPbH, (merged.Mv0X + 2) & ~3, (merged.Mv0Y + 2) & ~3, minX, maxX, minY, maxY, mvp, ref bestX, ref bestY, ref bestCost);
            }
        }

        // Whole samples, then halves and quarters: the eight neighbours of the best vector, as long as one is better
        var range = _decisions.MotionSearchRange;
        var originX = bestX;
        var originY = bestY;
        var lastStep = 4 >> _decisions.MotionSearchRefinement;
        for (var step = Math.Min(range, 8) * 4; step >= lastStep; step >>= 1)
        {
            for (var iteration = 0; iteration < 16; iteration++)
            {
                var centerX = bestX;
                var centerY = bestY;
                for (var direction = 0; direction < 9; direction++)
                {
                    if (direction is 4)
                        continue;

                    var x = centerX + (((direction % 3) - 1) * step);
                    var y = centerY + (((direction / 3) - 1) * step);
                    if (Math.Abs(x - originX) > range * 4 || Math.Abs(y - originY) > range * 4)
                        continue;

                    TryMotionVector(xPb, yPb, nPbW, nPbH, x, y, minX, maxX, minY, maxY, mvp, ref bestX, ref bestY, ref bestCost);
                }

                // The steps of a part of a sample refine the vector once
                if ((bestX == centerX && bestY == centerY) || step < 4)
                    break;
            }
        }

        // mvp_l0_flag: the predictor that makes the smallest difference
        var mvpFlag = GetMvdRate(bestX - mvp[2], bestY - mvp[3]) < GetMvdRate(bestX - mvp[0], bestY - mvp[1]) ? 1 : 0;
        motion = default;
        motion.Set(0, 0, (short)bestX, (short)bestY);
        choice = new HevcMotionChoice(MergeFlag: false, 0, bestX - mvp[mvpFlag * 2], bestY - mvp[(mvpFlag * 2) + 1], mvpFlag);
        return bestCost;
    }

    private readonly void TryMotionVector(int xPb, int yPb, int nPbW, int nPbH, int mvX, int mvY, int minX, int maxX, int minY, int maxY, scoped ReadOnlySpan<int> mvp, ref int bestX, ref int bestY, ref long bestCost)
    {
        if (mvX < minX || mvX > maxX || mvY < minY || mvY > maxY || (mvX == bestX && mvY == bestY))
            return;

        var cost = GetMotionVectorCost(xPb, yPb, nPbW, nPbH, mvX, mvY, mvp);
        if (cost < bestCost)
        {
            bestCost = cost;
            bestX = mvX;
            bestY = mvY;
        }
    }

    private readonly long GetMotionVectorCost(int xPb, int yPb, int nPbW, int nPbH, int mvX, int mvY, scoped ReadOnlySpan<int> mvp)
    {
        var rate = Math.Min(GetMvdRate(mvX - mvp[0], mvY - mvp[1]), GetMvdRate(mvX - mvp[2], mvY - mvp[3]));
        return _decisions.GetMotionCost(GetMotionSad(xPb, yPb, nPbW, nPbH, mvX, mvY), rate + HevcCost.GetBypassCost(2));
    }

    // The size of a motion vector difference (clause 7.3.8.9): two flags, a first order Exp-Golomb code and a sign for
    // each component
    private static int GetMvdRate(int mvdX, int mvdY) => GetMvdComponentRate(mvdX) + GetMvdComponentRate(mvdY);

    private static int GetMvdComponentRate(int mvd)
    {
        var abs = Math.Abs(mvd);
        if (abs is 0)
            return HevcCost.GetBypassCost(1);

        return abs is 1 ? HevcCost.GetBypassCost(3) : HevcCost.GetBypassCost(3) + HevcCost.GetExpGolombCost((uint)(abs - 2), 1);
    }

    // The sum of the absolute differences between the luma samples of the source and the prediction of a motion vector
    // (clause 8.5.3.3.3.1)
    private readonly long GetMotionSad(int xPb, int yPb, int nPbW, int nPbH, int mvX, int mvY)
    {
        var reference = _refs!.GetPicture(0, 0).GetPlane(0);
        var source = _workspace.GetSourcePlane(0);
        var xInt = xPb + (mvX >> 2);
        var yInt = yPb + (mvY >> 2);
        var original = source.Samples[((yPb * source.Stride) + xPb)..];
        if (((mvX | mvY) & 3) is 0 && xInt >= 0 && yInt >= 0 && xInt + nPbW <= reference.Width && yInt + nPbH <= reference.Height)
        {
            // Whole samples of the reference picture
            return GetSad(reference.Samples[((yInt * reference.Stride) + xInt)..], reference.Stride, original, source.Stride, nPbW, nPbH);
        }

        var prediction = _workspace.GetDraftSamples(HevcEncoderWorkspace.DraftMotionSearch, 0);
        HevcInterPredictor.Predict(reference, xInt, yInt, mvX & 3, mvY & 3, nPbW, nPbH, luma: true, _bitDepth, _scratch.GetPrediction(0), _scratch.Intermediate, _scratch.Window);
        HevcInterPredictor.WeightDefault(_scratch.GetPrediction(0), prediction, nPbW, nPbW, nPbH, _bitDepth);
        return GetSad(prediction, nPbW, original, source.Stride, nPbW, nPbH);
    }

    // part_mode of a coding unit coded in inter prediction mode (Table 9-45)
    private readonly void WritePartModeInter(int log2CbSize, int partMode)
    {
        var contexts = _cabac.Contexts;
        _sink.EncodeDecision(ref contexts[HevcContextLayout.PartMode], partMode is HevcConstants.Part2Nx2N ? 1 : 0);
        if (partMode is HevcConstants.Part2Nx2N)
            return;

        var horizontal = partMode is HevcConstants.Part2NxN or HevcConstants.Part2NxnU or HevcConstants.Part2NxnD;
        _sink.EncodeDecision(ref contexts[HevcContextLayout.PartMode + 1], horizontal ? 1 : 0);
        if (log2CbSize == _minCbLog2SizeY)
        {
            // A coding unit of 8 samples has no partitioning in four, and the encoder writes none for the larger ones
            Debug.Assert(partMode is HevcConstants.Part2NxN or HevcConstants.PartNx2N);
            if (!horizontal && log2CbSize > 3)
            {
                _sink.EncodeDecision(ref contexts[HevcContextLayout.PartMode + 2], 1);
            }

            return;
        }

        if (!_sps.AmpEnabledFlag)
            return;

        var symmetric = partMode is HevcConstants.Part2NxN or HevcConstants.PartNx2N;
        _sink.EncodeDecision(ref contexts[HevcContextLayout.PartMode + 3], symmetric ? 1 : 0);
        if (!symmetric)
        {
            _sink.EncodeBypass(partMode is HevcConstants.Part2NxnD or HevcConstants.PartnRx2N ? 1 : 0);
        }
    }

    // The prediction units of a coding unit (clause 7.3.8.5): returns merge_flag of the first one
    private bool WritePredictionUnits(int x0, int y0, int nCbS, int partMode, bool skip)
    {
        var mergeFlag = false;
        var count = partMode is HevcConstants.Part2Nx2N ? 1 : 2;
        for (var partIdx = 0; partIdx < count; partIdx++)
        {
            GetPredictionBlock(partMode, nCbS, partIdx, out var x, out var y, out var nPbW, out var nPbH);
            var merged = WritePredictionUnit(x0 + x, y0 + y, nPbW, nPbH, partIdx, skip);
            if (partIdx is 0)
            {
                mergeFlag = merged;
            }
        }

        return mergeFlag;
    }

    // Clause 7.3.8.6 "Prediction unit syntax", for the only reference picture of the list 0. The motion of the prediction
    // unit is derived as a decoder derives it (clause 8.5.3.1), and becomes the one of its blocks.
    private bool WritePredictionUnit(int xPb, int yPb, int nPbW, int nPbH, int partIdx, bool skip)
    {
        var contexts = _cabac.Contexts;
        var choice = _storing ? _workspace.Motion[partIdx] : default;
        var head = Record((choice.MergeFlag ? 1 : 0) | (choice.MergeIdx << 1) | (choice.MvpFlag << 4));
        var mvdX = Record(choice.MvdX);
        var mvdY = Record(choice.MvdY);
        var mergeFlag = (head & 1) is not 0;
        var mergeIdx = (head >> 1) & 7;
        var mvpFlag = (head >> 4) & 1;
        Debug.Assert(mergeFlag || !skip);
        if (!skip)
        {
            _sink.EncodeDecision(ref contexts[HevcContextLayout.MergeFlag], mergeFlag ? 1 : 0);
        }

        HevcMotion motion = default;
        if (mergeFlag)
        {
            // merge_idx: a truncated Rice binarization with cMax equal to MaxNumMergeCand - 1, whose first bin only has a
            // context
            var cMax = _header.MaxNumMergeCand - 1;
            if (cMax > 0)
            {
                _sink.EncodeDecision(ref contexts[HevcContextLayout.MergeIdx], mergeIdx is 0 ? 0 : 1);
                for (var k = 1; k < cMax && k <= mergeIdx; k++)
                {
                    _sink.EncodeBypass(mergeIdx > k ? 1 : 0);
                }
            }

            motion = DeriveMergeMotion(xPb, yPb, nPbW, nPbH, partIdx, mergeIdx);
        }
        else
        {
            // ref_idx_l0 is not present for one reference picture. Clause 7.3.8.9 "Motion vector difference syntax"
            Debug.Assert(_header.SliceType is HevcConstants.SliceP && _refs!.GetCount(0) is 1);
            var absX = Math.Abs(mvdX);
            var absY = Math.Abs(mvdY);
            _sink.EncodeDecision(ref contexts[HevcContextLayout.AbsMvdGreater0Flag], absX > 0 ? 1 : 0);
            _sink.EncodeDecision(ref contexts[HevcContextLayout.AbsMvdGreater0Flag], absY > 0 ? 1 : 0);
            if (absX > 0)
            {
                _sink.EncodeDecision(ref contexts[HevcContextLayout.AbsMvdGreater1Flag], absX > 1 ? 1 : 0);
            }

            if (absY > 0)
            {
                _sink.EncodeDecision(ref contexts[HevcContextLayout.AbsMvdGreater1Flag], absY > 1 ? 1 : 0);
            }

            WriteMvdComponent(mvdX);
            WriteMvdComponent(mvdY);
            _sink.EncodeDecision(ref contexts[HevcContextLayout.MvpFlag], mvpFlag);

            // Equations 8-94 to 8-101
            DeriveMvp(xPb, yPb, nPbW, nPbH, partIdx, 0, 0, mvpFlag is not 0, out var mvpX, out var mvpY);
            motion.Set(0, 0, (short)(mvpX + mvdX), (short)(mvpY + mvdY));
        }

        StorePredictionUnit(xPb, yPb, nPbW, nPbH, motion);
        return mergeFlag;
    }

    // abs_mvd_minus2, a first order Exp-Golomb code, and mvd_sign_flag
    private readonly void WriteMvdComponent(int mvd)
    {
        var abs = Math.Abs(mvd);
        if (abs is 0)
            return;

        if (abs > 1)
        {
            _sink.EncodeExpGolomb((uint)(abs - 2), 1);
        }

        _sink.EncodeBypass(mvd < 0 ? 1 : 0);
    }

    // The blocks of a coding unit coded in intra prediction mode have no motion
    private readonly void ClearMotion(int x0, int y0, int log2Size)
    {
        var count = 1 << (log2Size - 2);
        var index = ((y0 >> 2) * _widthIn4x4) + (x0 >> 2);
        for (var y = 0; y < count; y++)
        {
            _motion.Slice(index + (y * _widthIn4x4), count).Clear();
        }
    }
}
