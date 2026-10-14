using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

// The search of the encoder: the coding quadtree of a coding tree unit, and the coding units coded in intra prediction
// mode. A candidate is encoded for real: its blocks are predicted, transformed and reconstructed with the processes of
// the decoder, in the picture being reconstructed, and its syntax elements are costed with the context variables that
// the arithmetic encoder will have. The winner of a coding unit is then written once in the order of the syntax, which
// records it, gives its exact size and leaves the state of the picture as a decoder has it after the coding unit.
internal ref partial struct HevcSliceCoder
{
    // intra_chroma_pred_mode in the order it is tried: the mode of the luma block, planar, DC, vertical and horizontal
    private static ReadOnlySpan<byte> ChromaModeOrder => [4, 0, 3, 1, 2];

    // Clause 7.3.8.4 "Coding quadtree syntax": the coding unit of the block against the coding units of its four
    // quadrants. Returns the cost of the best one, which is recorded and reconstructed.
    private long SearchCodingQuadtree(int x0, int y0, int log2CbSize, int cqtDepth)
    {
        var workspace = _workspace;
        var size = 1 << log2CbSize;
        var fits = x0 + size <= _sps.PicWidthInLumaSamples && y0 + size <= _sps.PicHeightInLumaSamples;
        var flagCoded = fits && log2CbSize > _minCbLog2SizeY;
        var maxCbLog2Size = _refs is null ? _decisions.MaxIntraCbLog2Size : Math.Max(_decisions.MaxIntraCbLog2Size, _decisions.MaxInterCbLog2Size);
        var canCode = fits && log2CbSize <= maxCbLog2Size;
        var canSplit = log2CbSize > _minCbLog2SizeY && (!canCode || log2CbSize > _decisions.MinCbLog2Size);
        var ctxIdx = HevcContextLayout.SplitCuFlag + (flagCoded ? GetSplitCuFlagCtxInc(x0, y0, cqtDepth) : 0);
        var start = workspace.RecordCount;
        if (flagCoded)
        {
            // split_cu_flag
            workspace.AppendRecord(canCode ? 0 : 1);
        }

        if (!canSplit || !canCode)
        {
            var rate = _sink.Rate;
            if (flagCoded)
            {
                _sink.EncodeDecision(ref _cabac.Contexts[ctxIdx], canCode ? 0 : 1);
            }

            var flagCost = _decisions.GetCost(0, _sink.Rate - rate);
            return flagCost + (canCode ? SearchCodingUnit(x0, y0, log2CbSize, cqtDepth) : SearchCodingQuadtreeSplit(x0, y0, log2CbSize, cqtDepth, long.MaxValue));
        }

        // The coding unit, then the four quadrants from the same context variables
        var slot = HevcEncoderWorkspace.ContextQuadtree + (2 * cqtDepth);
        SaveContexts(slot);
        var rateStart = _sink.Rate;
        _sink.EncodeDecision(ref _cabac.Contexts[ctxIdx], 0);
        var cost = _decisions.GetCost(0, _sink.Rate - rateStart) + SearchCodingUnit(x0, y0, log2CbSize, cqtDepth);
        var end = workspace.RecordCount;
        SaveCodingUnitSamples(HevcEncoderWorkspace.SamplesQuadtree + cqtDepth, x0, y0, size);
        SaveContexts(slot + 1);
        RestoreContexts(slot);
        rateStart = _sink.Rate;
        _sink.EncodeDecision(ref _cabac.Contexts[ctxIdx], 1);
        var splitFlagCost = _decisions.GetCost(0, _sink.Rate - rateStart);
        var splitCost = SearchCodingQuadtreeSplit(x0, y0, log2CbSize, cqtDepth, cost - splitFlagCost);
        if (splitCost < cost - splitFlagCost)
        {
            // The records of the quadrants replace the ones of the coding unit
            var records = workspace.Records;
            var length = workspace.RecordCount - end;
            records[start] = 1;
            records.Slice(end, length).CopyTo(records[(start + 1)..]);
            workspace.RecordCount = start + 1 + length;
            return splitCost + splitFlagCost;
        }

        // The coding unit wins: its samples, the state of the picture that its records give, and its context variables
        workspace.RecordCount = end;
        RestoreCodingUnitSamples(HevcEncoderWorkspace.SamplesQuadtree + cqtDepth, x0, y0, size);
        ReplayCodingUnit(start + 1, x0, y0, log2CbSize, cqtDepth);
        RestoreContexts(slot + 1);
        return cost;
    }

    // The four quadrants of a block; long.MaxValue as soon as they cost more than limit
    private long SearchCodingQuadtreeSplit(int x0, int y0, int log2CbSize, int cqtDepth, long limit)
    {
        var half = 1 << (log2CbSize - 1);
        var cost = 0L;
        for (var i = 0; i < 4; i++)
        {
            var x = x0 + ((i & 1) * half);
            var y = y0 + ((i >> 1) * half);
            if (x >= _sps.PicWidthInLumaSamples || y >= _sps.PicHeightInLumaSamples)
                continue;

            cost += SearchCodingQuadtree(x, y, log2CbSize - 1, cqtDepth + 1);
            if (cost >= limit)
                return long.MaxValue;
        }

        return cost;
    }

    // Gives back to the picture the state of a coding unit from its records, without writing it
    private void ReplayCodingUnit(int position, int x0, int y0, int log2CbSize, int cqtDepth)
    {
        var workspace = _workspace;
        var mode = _sink.Mode;
        var end = workspace.RecordPosition;
        workspace.RecordPosition = position;
        _sink.Mode = HevcBinSinkMode.None;
        WriteCodingUnit(x0, y0, log2CbSize, cqtDepth);
        _sink.Mode = mode;
        workspace.RecordPosition = end;
    }

    // The coding unit of a block: returns its cost. Its records are appended, its samples are those of the picture, and
    // the context variables are the ones that follow it.
    private long SearchCodingUnit(int x0, int y0, int log2CbSize, int cqtDepth)
    {
        if (_refs is not null)
            return SearchInterCodingUnit(x0, y0, log2CbSize, cqtDepth);

        SaveContexts(HevcEncoderWorkspace.ContextCuStart);
        SearchIntraCodingUnit(x0, y0, log2CbSize, cqtDepth);
        return StoreCodingUnit(x0, y0, log2CbSize, cqtDepth);
    }

    // Writes the coding unit being searched in the order of the syntax, from the context variables that precede it: the
    // coding unit is recorded, and its cost is the one of its real size
    private long StoreCodingUnit(int x0, int y0, int log2CbSize, int cqtDepth)
    {
        RestoreContexts(HevcEncoderWorkspace.ContextCuStart);
        var rate = _sink.Rate;
        _storing = true;
        WriteCodingUnit(x0, y0, log2CbSize, cqtDepth);
        _storing = false;
        return _decisions.GetCost(_decisions.Lossless ? 0 : GetCodingUnitDistortion(x0, y0, 1 << log2CbSize), _sink.Rate - rate);
    }

    // The coding unit being searched becomes a coding unit coded in intra prediction mode: its partitioning, its luma
    // modes with their transform trees, then its chroma modes
    private void SearchIntraCodingUnit(int x0, int y0, int log2CbSize, int cqtDepth)
    {
        var workspace = _workspace;
        var nCbS = 1 << log2CbSize;
        workspace.ClearCodingUnit();
        _log2CbSize = log2CbSize;
        _xCb = x0;
        _yCb = y0;
        _ctDepth = cqtDepth;
        _isIntra = true;
        _cuTransquantBypassFlag = _decisions.Lossless;
        _interSplitFlag = 0;

        // The coding unit is known to the derivations of its own blocks
        var flags = (byte)((cqtDepth << HevcPictureState.CuDepthShift) | HevcPictureState.CuIntra | (_cuTransquantBypassFlag ? HevcPictureState.CuTransquantBypass : 0));
        SetCodingUnit(x0, y0, log2CbSize, flags);
        UpdateQpY(x0, y0);
        var cost = SearchIntraLuma(x0, y0, log2CbSize, HevcConstants.Part2Nx2N);
        if (_decisions.IntraNxN && log2CbSize == _minCbLog2SizeY && log2CbSize > _minTbLog2SizeY)
        {
            // Four prediction blocks, from the context variables that precede the coding unit
            var lumaModes = workspace.LumaModes;
            SaveLuma(HevcEncoderWorkspace.DraftPartBest, x0, y0, nCbS);
            SaveContexts(HevcEncoderWorkspace.ContextPartStart);
            RestoreContexts(HevcEncoderWorkspace.ContextCuStart);
            if (SearchIntraLuma(x0, y0, log2CbSize, HevcConstants.PartNxN) >= cost)
            {
                RestoreLuma(HevcEncoderWorkspace.DraftPartBest, x0, y0, nCbS);
                RestoreContexts(HevcEncoderWorkspace.ContextPartStart);
                workspace.LumaModes = lumaModes;
                workspace.PartMode = HevcConstants.Part2Nx2N;
                _partMode = HevcConstants.Part2Nx2N;
                _intraSplitFlag = 0;
                _maxTrafoDepth = _sps.MaxTransformHierarchyDepthIntra;
                SetIntraPredModeY(x0, y0, nCbS, (int)(lumaModes & 0xFF));
            }
        }

        if (_chromaArrayType is not 0)
        {
            SearchIntraChroma(x0, y0, log2CbSize);
        }
    }

    // The luma prediction blocks of a partitioning of the coding unit being searched: returns their cost
    private long SearchIntraLuma(int x0, int y0, int log2CbSize, int partMode)
    {
        var workspace = _workspace;
        workspace.PartMode = partMode;
        workspace.LumaModes = 0;
        _partMode = partMode;
        _intraSplitFlag = partMode is HevcConstants.PartNxN ? 1 : 0;
        _maxTrafoDepth = _sps.MaxTransformHierarchyDepthIntra + _intraSplitFlag;
        var count = partMode is HevcConstants.PartNxN ? 4 : 1;
        var log2PbSize = log2CbSize - _intraSplitFlag;

        // part_mode
        var cost = log2CbSize == _minCbLog2SizeY
            ? _decisions.GetCost(0, HevcCost.GetBinCost(_cabac.Contexts[HevcContextLayout.PartMode], partMode is HevcConstants.Part2Nx2N ? 1 : 0))
            : 0;
        for (var i = 0; i < count; i++)
        {
            var xPb = x0 + ((i & 1) << log2PbSize);
            var yPb = y0 + ((i >> 1) << log2PbSize);
            cost += SearchIntraLumaMode(xPb, yPb, log2PbSize, i);
        }

        return cost;
    }

    // The intra prediction mode of a luma prediction block (clause 8.4.2) and its transform tree: the modes are sorted by
    // the difference between their prediction and the source, and the best ones are encoded
    private long SearchIntraLumaMode(int xPb, int yPb, int log2PbSize, int pbIdx)
    {
        var workspace = _workspace;
        var nPbS = 1 << log2PbSize;
        var plane = _picture.GetPlane(0);
        var source = workspace.GetSourcePlane(0);
        Span<int> candModeList = stackalloc int[3];
        DeriveCandModeList(xPb, yPb, candModeList);
        Span<long> costs = stackalloc long[35];
        costs.Fill(long.MaxValue);
        var candidates = Math.Min(_decisions.IntraCandidates, 35);
        if (candidates < 35)
        {
            // The neighbouring samples of the block, and their filtered version, are the same for every mode: they are
            // built once, and each mode is predicted into a scratch block
            var references = new IntraModeReferences(workspace.IntraScratch, nPbS);
            GetIntraReferences(plane, 0, xPb, yPb, xPb, yPb, nPbS, references.Unfiltered);
            // Planar, DC, the candidate modes, which are cheap to signal, and some of the angular modes
            EvaluateIntraLumaMode(HevcConstants.IntraPlanar, ref references, source, xPb, yPb, nPbS, candModeList, costs);
            EvaluateIntraLumaMode(HevcConstants.IntraDc, ref references, source, xPb, yPb, nPbS, candModeList, costs);
            foreach (var mode in candModeList)
            {
                EvaluateIntraLumaMode(mode, ref references, source, xPb, yPb, nPbS, candModeList, costs);
            }

            var step = _decisions.IntraModeStep;
            for (var mode = 2; mode <= 34; mode += step)
            {
                EvaluateIntraLumaMode(mode, ref references, source, xPb, yPb, nPbS, candModeList, costs);
            }

            if (_decisions.IntraModeRefinement)
            {
                // The angular modes around the best one
                var best = 2 + costs[2..].IndexOf(MinOf(costs[2..]));
                for (var delta = step >> 1; delta > 0; delta >>= 1)
                {
                    var center = best;
                    if (center - delta >= 2 && EvaluateIntraLumaMode(center - delta, ref references, source, xPb, yPb, nPbS, candModeList, costs) < costs[best])
                    {
                        best = center - delta;
                    }

                    if (center + delta <= 34 && EvaluateIntraLumaMode(center + delta, ref references, source, xPb, yPb, nPbS, candModeList, costs) < costs[best])
                    {
                        best = center + delta;
                    }
                }
            }
        }

        var trafoDepth = _intraSplitFlag;
        var bestCost = long.MaxValue;
        var bestMode = -1;
        var lastMode = -1;
        if (candidates > 1)
        {
            SaveContexts(HevcEncoderWorkspace.ContextLumaStart);
        }

        for (var candidate = 0; candidate < candidates; candidate++)
        {
            // The next mode: all of them in their order, or the best one that is left
            var mode = candidate;
            if (candidates < 35)
            {
                var minimum = MinOf(costs);
                if (minimum is long.MaxValue)
                    break;

                mode = costs.IndexOf(minimum);
                costs[mode] = long.MaxValue;
            }

            if (candidate > 0)
            {
                RestoreContexts(HevcEncoderWorkspace.ContextLumaStart);
            }

            SetIntraPredModeY(xPb, yPb, nPbS, mode);
            lastMode = mode;
            var cost = _decisions.GetCost(0, GetIntraLumaPredModeRate(mode, candModeList)) + SearchLumaTree(xPb, yPb, log2PbSize, trafoDepth, long.MaxValue);
            if (cost >= bestCost)
                continue;

            bestCost = cost;
            bestMode = mode;
            if (candidate < candidates - 1)
            {
                SaveLuma(HevcEncoderWorkspace.DraftLumaBest, xPb, yPb, nPbS);
                SaveContexts(HevcEncoderWorkspace.ContextLumaBest);
            }
        }

        if (bestMode != lastMode)
        {
            RestoreLuma(HevcEncoderWorkspace.DraftLumaBest, xPb, yPb, nPbS);
            RestoreContexts(HevcEncoderWorkspace.ContextLumaBest);
            SetIntraPredModeY(xPb, yPb, nPbS, bestMode);
        }

        workspace.LumaModes |= (uint)bestMode << (pbIdx * 8);
        return bestCost;
    }

    private static long MinOf(ReadOnlySpan<long> values)
    {
        var minimum = long.MaxValue;
        foreach (var value in values)
        {
            minimum = Math.Min(minimum, value);
        }

        return minimum;
    }

    // The neighbouring samples of a luma prediction block whose modes are evaluated, their filtered version once a mode
    // needs it, and the prediction of the mode being evaluated
    private ref struct IntraModeReferences
    {
        public Span<ushort> Unfiltered;
        public Span<ushort> Filtered;
        public Span<ushort> Prediction;
        public bool HasFiltered;

        public IntraModeReferences(Span<ushort> scratch, int nPbS)
        {
            var count = (nPbS * 4) + 1;
            Unfiltered = scratch[..count];
            Filtered = scratch.Slice(HevcIntraPredictor.MaxNeighbourCount, count);
            Prediction = scratch.Slice(2 * HevcIntraPredictor.MaxNeighbourCount, nPbS * nPbS);
        }
    }

    // The cost of a mode from its prediction as a single block: the sum of the absolute differences with the source, and
    // the size of its syntax elements
    private long EvaluateIntraLumaMode(int mode, scoped ref IntraModeReferences references, Av1Plane source, int xPb, int yPb, int nPbS, scoped ReadOnlySpan<int> candModeList, scoped Span<long> costs)
    {
        if (costs[mode] is not long.MaxValue)
            return costs[mode];

        // The intra sample prediction of PredictIntra, into the scratch block
        ReadOnlySpan<ushort> p = references.Unfiltered;
        if (IsIntraReferenceFiltered(0, nPbS, mode))
        {
            if (!references.HasFiltered)
            {
                FilterIntraReferences(0, nPbS, references.Unfiltered, references.Filtered);
                references.HasFiltered = true;
            }

            p = references.Filtered;
        }

        PredictIntraSamples(0, nPbS, mode, p, references.Prediction, nPbS);
        var sad = GetSad(references.Prediction, nPbS, source.Samples[((yPb * source.Stride) + xPb)..], source.Stride, nPbS);
        costs[mode] = _decisions.GetMotionCost(sad, GetIntraLumaPredModeRate(mode, candModeList));
        return costs[mode];
    }

    /// <summary>The sum of the absolute differences between two square blocks of samples: 8 samples at a time.</summary>
    internal static long GetSad(ReadOnlySpan<ushort> first, int firstStride, ReadOnlySpan<ushort> second, int secondStride, int size)
        => GetSad(first, firstStride, second, secondStride, size, size);

    /// <summary>The sum of the absolute differences between two blocks of samples of at most 64x64 samples: 8 samples at a time.</summary>
    internal static long GetSad(ReadOnlySpan<ushort> first, int firstStride, ReadOnlySpan<ushort> second, int secondStride, int width, int height)
    {
        if (!Vector128.IsHardwareAccelerated || width < 8 || width * height > 64 * 64)
            return GetSadScalar(first, firstStride, second, secondStride, width, height);

        // At most 64 * 64 differences of 16 bits: the sums fit in 32 bits
        var sum = Vector128<uint>.Zero;
        var sad = 0L;
        var vectorWidth = width & ~7;
        for (var y = 0; y < height; y++)
        {
            var firstRow = first.Slice(y * firstStride, width);
            var secondRow = second.Slice(y * secondStride, width);
            for (var x = 0; x < vectorWidth; x += 8)
            {
                var a = Vector128.Create(firstRow[x..]);
                var b = Vector128.Create(secondRow[x..]);
                var (low, high) = Vector128.Widen(Vector128.Max(a, b) - Vector128.Min(a, b));
                sum += low + high;
            }

            for (var x = vectorWidth; x < width; x++)
            {
                sad += Math.Abs(secondRow[x] - firstRow[x]);
            }
        }

        return sad + Vector128.Sum(sum);
    }

    /// <summary>The sum of <see cref="GetSad(ReadOnlySpan{ushort}, int, ReadOnlySpan{ushort}, int, int, int)"/>, one sample at a time: the reference of the vector path.</summary>
    internal static long GetSadScalar(ReadOnlySpan<ushort> first, int firstStride, ReadOnlySpan<ushort> second, int secondStride, int width, int height)
    {
        var sad = 0L;
        for (var y = 0; y < height; y++)
        {
            var firstRow = first.Slice(y * firstStride, width);
            var secondRow = second.Slice(y * secondStride, width);
            for (var x = 0; x < width; x++)
            {
                sad += Math.Abs(secondRow[x] - firstRow[x]);
            }
        }

        return sad;
    }

    /// <summary>
    /// The sum of the squared differences between two blocks of samples of at most 64 samples across: 8 samples at a time
    /// for samples of at most 12 bits.
    /// </summary>
    internal static long GetSse(ReadOnlySpan<ushort> first, int firstStride, ReadOnlySpan<ushort> second, int secondStride, int width, int height, int bitDepth)
    {
        if (!Vector128.IsHardwareAccelerated || width < 8 || width > 64 || bitDepth > 12)
            return GetSseScalar(first, firstStride, second, secondStride, width, height);

        // A squared difference has at most 24 bits: the 16 of a row of 64 samples that a lane adds fit in 32 bits, and so
        // do the sums of the 4 lanes
        var sse = 0L;
        var vectorWidth = width & ~7;
        for (var y = 0; y < height; y++)
        {
            var firstRow = first.Slice(y * firstStride, width);
            var secondRow = second.Slice(y * secondStride, width);
            var sum = Vector128<int>.Zero;
            for (var x = 0; x < vectorWidth; x += 8)
            {
                var (firstLow, firstHigh) = Vector128.Widen(Vector128.Create(firstRow[x..]));
                var (secondLow, secondHigh) = Vector128.Widen(Vector128.Create(secondRow[x..]));
                var low = secondLow.AsInt32() - firstLow.AsInt32();
                var high = secondHigh.AsInt32() - firstHigh.AsInt32();
                sum += (low * low) + (high * high);
            }

            sse += Vector128.Sum(sum);
            for (var x = vectorWidth; x < width; x++)
            {
                var difference = secondRow[x] - firstRow[x];
                sse += difference * difference;
            }
        }

        return sse;
    }

    /// <summary>The sum of <see cref="GetSse"/>, one sample at a time: the reference of the vector path.</summary>
    internal static long GetSseScalar(ReadOnlySpan<ushort> first, int firstStride, ReadOnlySpan<ushort> second, int secondStride, int width, int height)
    {
        var sse = 0L;
        for (var y = 0; y < height; y++)
        {
            var firstRow = first.Slice(y * firstStride, width);
            var secondRow = second.Slice(y * secondStride, width);
            for (var x = 0; x < width; x++)
            {
                var difference = secondRow[x] - firstRow[x];
                sse += difference * difference;
            }
        }

        return sse;
    }

    /// <summary>
    /// The residual of a square block of at most 32x32 samples, the source minus its prediction, and the sum of its squares:
    /// 8 samples at a time for samples of at most 12 bits.
    /// </summary>
    internal static long GetResidual(ReadOnlySpan<ushort> original, int originalStride, ReadOnlySpan<ushort> predicted, int predictedStride, Span<int> residual, int size, int bitDepth)
    {
        if (!Vector128.IsHardwareAccelerated || size < 8 || size > 32 || bitDepth > 12)
            return GetResidualScalar(original, originalStride, predicted, predictedStride, residual, size);

        var energy = 0L;
        for (var y = 0; y < size; y++)
        {
            var originalRow = original.Slice(y * originalStride, size);
            var predictedRow = predicted.Slice(y * predictedStride, size);
            var row = residual.Slice(y * size, size);
            var sum = Vector128<int>.Zero;
            for (var x = 0; x < size; x += 8)
            {
                var (originalLow, originalHigh) = Vector128.Widen(Vector128.Create(originalRow[x..]));
                var (predictedLow, predictedHigh) = Vector128.Widen(Vector128.Create(predictedRow[x..]));
                var low = originalLow.AsInt32() - predictedLow.AsInt32();
                var high = originalHigh.AsInt32() - predictedHigh.AsInt32();
                low.CopyTo(row[x..]);
                high.CopyTo(row[(x + 4)..]);
                sum += (low * low) + (high * high);
            }

            energy += Vector128.Sum(sum);
        }

        return energy;
    }

    /// <summary>The residual of <see cref="GetResidual"/>, one sample at a time: the reference of the vector path.</summary>
    internal static long GetResidualScalar(ReadOnlySpan<ushort> original, int originalStride, ReadOnlySpan<ushort> predicted, int predictedStride, Span<int> residual, int size)
    {
        var energy = 0L;
        for (var y = 0; y < size; y++)
        {
            var originalRow = original.Slice(y * originalStride, size);
            var predictedRow = predicted.Slice(y * predictedStride, size);
            var row = residual.Slice(y * size, size);
            for (var x = 0; x < size; x++)
            {
                var difference = originalRow[x] - predictedRow[x];
                row[x] = difference;
                energy += difference * difference;
            }
        }

        return energy;
    }

    /// <summary>
    /// The sum of the squared differences between the source and a square block of at most 32x32 samples reconstructed from
    /// its prediction and a residual (clause 8.6.7): 8 samples at a time for samples of at most 12 bits.
    /// </summary>
    internal static long GetReconstructionSse(ReadOnlySpan<ushort> original, int originalStride, ReadOnlySpan<ushort> predicted, int predictedStride, ReadOnlySpan<int> residual, int size, int bitDepth)
    {
        if (!Vector128.IsHardwareAccelerated || size < 8 || size > 32 || bitDepth > 12)
            return GetReconstructionSseScalar(original, originalStride, predicted, predictedStride, residual, size, bitDepth);

        var max = Vector128.Create((1 << bitDepth) - 1);
        var distortion = 0L;
        for (var y = 0; y < size; y++)
        {
            var originalRow = original.Slice(y * originalStride, size);
            var predictedRow = predicted.Slice(y * predictedStride, size);
            var row = residual.Slice(y * size, size);
            var sum = Vector128<int>.Zero;
            for (var x = 0; x < size; x += 8)
            {
                var (originalLow, originalHigh) = Vector128.Widen(Vector128.Create(originalRow[x..]));
                var (predictedLow, predictedHigh) = Vector128.Widen(Vector128.Create(predictedRow[x..]));
                var low = originalLow.AsInt32() - Vector128.Min(Vector128.Max(predictedLow.AsInt32() + Vector128.Create(row[x..]), Vector128<int>.Zero), max);
                var high = originalHigh.AsInt32() - Vector128.Min(Vector128.Max(predictedHigh.AsInt32() + Vector128.Create(row[(x + 4)..]), Vector128<int>.Zero), max);
                sum += (low * low) + (high * high);
            }

            distortion += Vector128.Sum(sum);
        }

        return distortion;
    }

    /// <summary>The sum of <see cref="GetReconstructionSse"/>, one sample at a time: the reference of the vector path.</summary>
    internal static long GetReconstructionSseScalar(ReadOnlySpan<ushort> original, int originalStride, ReadOnlySpan<ushort> predicted, int predictedStride, ReadOnlySpan<int> residual, int size, int bitDepth)
    {
        var maxValue = (1 << bitDepth) - 1;
        var distortion = 0L;
        for (var y = 0; y < size; y++)
        {
            var originalRow = original.Slice(y * originalStride, size);
            var predictedRow = predicted.Slice(y * predictedStride, size);
            var row = residual.Slice(y * size, size);
            for (var x = 0; x < size; x++)
            {
                var difference = originalRow[x] - Math.Clamp(predictedRow[x] + row[x], 0, maxValue);
                distortion += difference * difference;
            }
        }

        return distortion;
    }

    // The size of prev_intra_luma_pred_flag and of mpm_idx or rem_intra_luma_pred_mode
    private readonly int GetIntraLumaPredModeRate(int mode, ReadOnlySpan<int> candModeList)
    {
        var mpmIdx = candModeList.IndexOf(mode);
        var context = _cabac.Contexts[HevcContextLayout.PrevIntraLumaPredFlag];
        if (mpmIdx < 0)
            return HevcCost.GetBinCost(context, 0) + HevcCost.GetBypassCost(5);

        return HevcCost.GetBinCost(context, 1) + HevcCost.GetBypassCost(mpmIdx is 0 ? 1 : 2);
    }

    // Clause 7.3.8.8 "Transform tree syntax", for the luma blocks: a transform block against its four quadrants. Returns
    // the cost of the best one, or long.MaxValue as soon as the tree costs more than limit.
    private long SearchLumaTree(int x0, int y0, int log2TrafoSize, int trafoDepth, long limit)
    {
        if (log2TrafoSize > _sps.MaxTbLog2SizeY || (_interSplitFlag is 1 && trafoDepth is 0))
            return SearchLumaTreeSplit(x0, y0, log2TrafoSize, trafoDepth, limit);

        if (log2TrafoSize <= _minTbLog2SizeY || trafoDepth >= _maxTrafoDepth)
            return SearchLumaBlock(x0, y0, log2TrafoSize, trafoDepth);

        // split_transform_flag
        var ctxIdx = HevcContextLayout.SplitTransformFlag + 5 - log2TrafoSize;
        var slot = HevcEncoderWorkspace.ContextTransform + (2 * trafoDepth);
        SaveContexts(slot);
        var rate = _sink.Rate;
        _sink.EncodeDecision(ref _cabac.Contexts[ctxIdx], 0);
        var cost = _decisions.GetCost(0, _sink.Rate - rate) + SearchLumaBlock(x0, y0, log2TrafoSize, trafoDepth);

        // A block that has no residual is not split
        if (_workspace.GetMap(HevcEncoderWorkspace.MapCbfLuma)[GetCell(x0, y0)] is 0)
            return cost;

        var size = 1 << log2TrafoSize;
        SaveLuma(HevcEncoderWorkspace.DraftTransform + trafoDepth, x0, y0, size);
        SaveContexts(slot + 1);
        RestoreContexts(slot);
        if (!_isIntra)
        {
            // The quadrants are the residual of the same prediction, which the block has replaced by its samples
            RestorePrediction(x0, y0, size);
        }

        rate = _sink.Rate;
        _sink.EncodeDecision(ref _cabac.Contexts[ctxIdx], 1);
        var splitFlagCost = _decisions.GetCost(0, _sink.Rate - rate);
        var splitCost = SearchLumaTreeSplit(x0, y0, log2TrafoSize, trafoDepth, cost - splitFlagCost);
        if (splitCost < cost - splitFlagCost)
            return splitCost + splitFlagCost;

        RestoreLuma(HevcEncoderWorkspace.DraftTransform + trafoDepth, x0, y0, size);
        RestoreContexts(slot + 1);
        return cost;
    }

    private long SearchLumaTreeSplit(int x0, int y0, int log2TrafoSize, int trafoDepth, long limit)
    {
        var half = 1 << (log2TrafoSize - 1);
        var cost = 0L;
        for (var i = 0; i < 4; i++)
        {
            cost += SearchLumaTree(x0 + ((i & 1) * half), y0 + ((i >> 1) * half), log2TrafoSize - 1, trafoDepth + 1, long.MaxValue);
            if (cost >= limit)
                return long.MaxValue;
        }

        return cost;
    }

    // The luma prediction samples of a block of a coding unit coded in inter prediction mode, from the snapshot of the
    // prediction of the coding unit
    private readonly void RestorePrediction(int x0, int y0, int size)
    {
        var plane = _picture.GetPlane(0);
        var prediction = _workspace.GetSavedSamples(HevcEncoderWorkspace.SamplesPrediction, 0);
        var nCbS = 1 << _log2CbSize;
        for (var y = 0; y < size; y++)
        {
            prediction.Slice(((y0 - _yCb + y) * nCbS) + x0 - _xCb, size).CopyTo(plane.Samples[(((y0 + y) * plane.Stride) + x0)..]);
        }
    }

    // The luma block of a transform unit: predicted, when the coding unit is coded in intra prediction mode, then encoded
    private long SearchLumaBlock(int x0, int y0, int log2TrafoSize, int trafoDepth)
    {
        var workspace = _workspace;
        var nTbS = 1 << log2TrafoSize;
        var cell = GetCell(x0, y0);
        var predModeIntra = _isIntra ? _intraPredModeY[((y0 >> 2) * _widthIn4x4) + (x0 >> 2)] : -1;
        if (predModeIntra >= 0)
        {
            PredictIntra(_picture.GetPlane(0), 0, x0, y0, x0, y0, nTbS, predModeIntra);
        }

        // The depth of the transform tree at every block of the transform unit
        var depths = workspace.GetMap(HevcEncoderWorkspace.MapTrafoDepth);
        var count = nTbS >> 2;
        for (var y = 0; y < count; y++)
        {
            depths.Slice(cell + (y * HevcEncoderWorkspace.CellStride), count).Fill((byte)trafoDepth);
        }

        var cost = SearchBlock(0, x0, y0, log2TrafoSize, predModeIntra, out var coded, out var transformSkipFlag);
        workspace.GetMap(HevcEncoderWorkspace.MapCbfLuma)[cell] = (byte)(coded ? 1 : 0);
        workspace.GetMap(HevcEncoderWorkspace.MapTransformSkipLuma)[cell] = (byte)(transformSkipFlag ? 1 : 0);

        // cbf_luma
        return cost + _decisions.GetCost(0, HevcCost.GetBinCost(_cabac.Contexts[HevcContextLayout.CbfLuma + (trafoDepth is 0 ? 1 : 0)], coded ? 1 : 0));
    }

    // intra_chroma_pred_mode of the coding unit being searched (clause 8.4.3), and the chroma blocks of its transform tree
    private void SearchIntraChroma(int x0, int y0, int log2CbSize)
    {
        var workspace = _workspace;
        var count = _chromaArrayType is 3 && _partMode is HevcConstants.PartNxN ? 4 : 1;
        var log2PbSize = count is 4 ? log2CbSize - 1 : log2CbSize;
        var pbSize = 1 << log2PbSize;
        var candidates = _decisions.ChromaCandidates;
        var chromaModes = 0u;
        _intraChromaPredMode = 0;
        _intraPredModeC = 0;
        for (var i = 0; i < count; i++)
        {
            var xPb = x0 + ((i & 1) * pbSize);
            var yPb = y0 + ((i >> 1) * pbSize);
            var bestCost = long.MaxValue;
            var bestMode = -1;
            var lastMode = -1;
            if (candidates > 1)
            {
                SaveContexts(HevcEncoderWorkspace.ContextChromaStart);
            }

            for (var candidate = 0; candidate < candidates; candidate++)
            {
                int mode = ChromaModeOrder[candidate];
                if (candidate > 0)
                {
                    RestoreContexts(HevcEncoderWorkspace.ContextChromaStart);
                }

                SetIntraPredModeC(x0, y0, pbSize, i, mode, count);
                lastMode = mode;
                var rate = HevcCost.GetBinCost(_cabac.Contexts[HevcContextLayout.IntraChromaPredMode], mode is 4 ? 0 : 1) + (mode is 4 ? 0 : HevcCost.GetBypassCost(2));
                var cost = _decisions.GetCost(0, rate) + (count is 4
                    ? SearchChromaTree(xPb, yPb, x0, y0, log2PbSize, 1, i)
                    : SearchChromaTree(x0, y0, x0, y0, log2CbSize, 0, 0));
                if (cost >= bestCost)
                    continue;

                bestCost = cost;
                bestMode = mode;
                if (candidate < candidates - 1)
                {
                    SaveChroma(HevcEncoderWorkspace.DraftChromaBest, xPb, yPb, pbSize);
                    SaveContexts(HevcEncoderWorkspace.ContextChromaBest);
                }
            }

            if (bestMode != lastMode)
            {
                RestoreChroma(HevcEncoderWorkspace.DraftChromaBest, xPb, yPb, pbSize);
                RestoreContexts(HevcEncoderWorkspace.ContextChromaBest);
                SetIntraPredModeC(x0, y0, pbSize, i, bestMode, count);
            }

            chromaModes |= (uint)bestMode << (i * 8);
        }

        workspace.ChromaModes = chromaModes;
    }

    private void SetIntraPredModeC(int x0, int y0, int pbSize, int i, int intraChromaPredMode, int chromaCount)
    {
        SetIntraPredModeC(x0, y0, pbSize, i, intraChromaPredMode);
        if (chromaCount is 1)
        {
            // One mode for the coding unit
            _intraChromaPredMode = (_intraChromaPredMode & 0xFF) * 0x01010101;
            _intraPredModeC = (_intraPredModeC & 0xFF) * 0x01010101;
        }
    }

    // The chroma blocks of the transform units of a node of the transform tree, whose shape is the one that the luma
    // blocks decided (clauses 7.3.8.8 and 7.3.8.10). Returns their cost.
    private long SearchChromaTree(int x0, int y0, int xBase, int yBase, int log2TrafoSize, int trafoDepth, int blkIdx)
    {
        if (_workspace.GetMap(HevcEncoderWorkspace.MapTrafoDepth)[GetCell(x0, y0)] > trafoDepth)
        {
            var half = 1 << (log2TrafoSize - 1);
            var cost = 0L;
            for (var i = 0; i < 4; i++)
            {
                cost += SearchChromaTree(x0 + ((i & 1) * half), y0 + ((i >> 1) * half), x0, y0, log2TrafoSize - 1, trafoDepth + 1, i);
            }

            return cost;
        }

        if (log2TrafoSize > 2 || _chromaArrayType is 3)
        {
            // The prediction block of the transform unit: one of four when the coding unit is split
            var pbIdx = _intraSplitFlag is 0 ? 0 : (((y0 >> (_log2CbSize - 1)) & 1) << 1) | ((x0 >> (_log2CbSize - 1)) & 1);
            var predModeIntraC = _isIntra ? (int)(_intraPredModeC >> (pbIdx * 8)) & 0xFF : -1;
            return SearchChromaBlocks(x0, y0, Math.Max(2, log2TrafoSize - (_chromaArrayType is 3 ? 0 : 1)), predModeIntraC, trafoDepth);
        }

        if (blkIdx is 3)
        {
            // The chroma blocks of the four 4x4 luma blocks, at the location of the block they are split from
            return SearchChromaBlocks(xBase, yBase, 2, _isIntra ? (int)_intraPredModeC & 0xFF : -1, trafoDepth - 1);
        }

        return 0;
    }

    // The chroma blocks of a transform unit, whose first luma sample is ( x0, y0 ): two blocks, one above the other, for
    // each component of the 4:2:2 chroma format
    private long SearchChromaBlocks(int x0, int y0, int log2TrafoSizeC, int predModeIntraC, int trafoDepth)
    {
        var workspace = _workspace;
        var subWidthC = _sps.SubWidthC;
        var subHeightC = _sps.SubHeightC;
        var blockCount = _chromaArrayType is 2 ? 2 : 1;
        var cell = GetCell(x0, y0);
        var cbf = 0;
        var transformSkip = 0;
        var cost = 0L;
        for (var cIdx = 1; cIdx <= 2; cIdx++)
        {
            var plane = _picture.GetPlane(cIdx);
            for (var tIdx = 0; tIdx < blockCount; tIdx++)
            {
                var yOffset = tIdx << log2TrafoSizeC;
                var xTb = x0 / subWidthC;
                var yTb = (y0 / subHeightC) + yOffset;
                if (predModeIntraC >= 0)
                {
                    PredictIntra(plane, cIdx, xTb, yTb, x0, y0 + (yOffset * subHeightC), 1 << log2TrafoSizeC, predModeIntraC);
                }

                cost += SearchBlock(cIdx, xTb, yTb, log2TrafoSizeC, predModeIntraC, out var coded, out var transformSkipFlag);

                // cbf_cb, cbf_cr
                cost += _decisions.GetCost(0, HevcCost.GetBinCost(_cabac.Contexts[HevcContextLayout.CbfChroma + trafoDepth], coded ? 1 : 0));
                var bit = ((cIdx - 1) * 2) + tIdx;
                cbf |= (coded ? 1 : 0) << bit;
                transformSkip |= (transformSkipFlag ? 1 : 0) << bit;
            }
        }

        workspace.GetMap(HevcEncoderWorkspace.MapCbfChroma)[cell] = (byte)cbf;
        workspace.GetMap(HevcEncoderWorkspace.MapTransformSkipChroma)[cell] = (byte)transformSkip;
        return cost;
    }

    /// <summary>
    /// Encodes a block whose prediction is in the picture: its residual is transformed and quantized, or kept as it is in a
    /// coding unit that bypasses both, and the block is reconstructed by the processes of the decoder (clauses 8.6.2 and
    /// 8.6.7). The levels are those of the coding unit being searched.
    /// </summary>
    /// <param name="cIdx">The colour component of the block.</param>
    /// <param name="xTb">The horizontal position of the block, in samples of the component.</param>
    /// <param name="yTb">The vertical position of the block, in samples of the component.</param>
    /// <param name="log2TrafoSize">The base 2 logarithm of the size of the block.</param>
    /// <param name="predModeIntra">The intra prediction mode of the block; or -1.</param>
    /// <param name="coded">Whether the block has levels.</param>
    /// <param name="transformSkipFlag"><c>transform_skip_flag</c>.</param>
    /// <returns>The cost of the block, without its coded block flag.</returns>
    private long SearchBlock(int cIdx, int xTb, int yTb, int log2TrafoSize, int predModeIntra, out bool coded, out bool transformSkipFlag)
    {
        var workspace = _workspace;
        var nTbS = 1 << log2TrafoSize;
        var length = nTbS * nTbS;
        var plane = _picture.GetPlane(cIdx);
        var source = workspace.GetSourcePlane(cIdx);
        var residual = workspace.GetScratch(0)[..length];
        var energy = GetResidual(source.Samples[((yTb * source.Stride) + xTb)..], source.Stride, plane.Samples[((yTb * plane.Stride) + xTb)..], plane.Stride, residual, nTbS, _bitDepth);

        coded = false;
        transformSkipFlag = false;
        if (energy is 0)
            return 0;

        long rate;
        if (_cuTransquantBypassFlag)
        {
            // Equation 8-297: the levels are the residual
            coded = true;
            rate = _sink.Rate;
            WriteResidualCoding(residual, log2TrafoSize, cIdx, predModeIntra, transformSkipFlag: false);
            StoreLevels(cIdx, xTb, yTb, nTbS, residual);
            AddResidual(plane, xTb, yTb, nTbS, residual);
            return _decisions.GetCost(0, _sink.Rate - rate);
        }

        // Without levels, the block is its prediction
        var bestCost = _decisions.GetCost(energy, 0);
        var qP = cIdx is 0 ? _qpY + _sps.QpBdOffset : GetChromaQp(cIdx);
        var rounding = _isIntra ? _decisions.QuantizerRoundingIntra : _decisions.QuantizerRoundingInter;
        var scanIdx = GetScanIdx(log2TrafoSize, cIdx, predModeIntra);
        var coefficients = workspace.GetScratch(1)[..length];
        var levels = workspace.GetScratch(2)[..length];
        var block = workspace.GetScratch(3)[..length];
        var trySkip = _pps.TransformSkipEnabledFlag && log2TrafoSize <= _pps.Log2MaxTransformSkipSize;
        if (trySkip || _decisions.CodedBlockDecision)
        {
            SaveContexts(HevcEncoderWorkspace.ContextBlock);
        }

        // Clause 8.6.4.1: the transform type of the 4x4 luma blocks of an intra coding unit is the discrete sine transform
        HevcForwardTransform.Forward(residual, coefficients, log2TrafoSize, nTbS is 4 && cIdx is 0 && _isIntra ? 1 : 0, _bitDepth);
        if (Quantize(coefficients, levels, log2TrafoSize, scanIdx, qP, rounding))
        {
            rate = _sink.Rate;
            WriteResidualCoding(levels, log2TrafoSize, cIdx, predModeIntra, transformSkipFlag: false);
            rate = _sink.Rate - rate;
            levels.CopyTo(block);
            ScaleAndTransform(block, log2TrafoSize, cIdx, transformSkipFlag: false);
            var cost = _decisions.GetCost(GetDistortion(plane, source, xTb, yTb, nTbS, block), rate);
            if (cost < bestCost || !_decisions.CodedBlockDecision)
            {
                coded = true;
                bestCost = cost;
            }
        }

        if (trySkip)
        {
            // The residual without transform, from the context variables that precede the block
            var skipLevels = workspace.GetScratch(5)[..length];
            var skipBlock = workspace.GetScratch(6)[..length];
            if (coded)
            {
                SaveContexts(HevcEncoderWorkspace.ContextBlockBest);
            }

            RestoreContexts(HevcEncoderWorkspace.ContextBlock);
            HevcForwardTransform.ForwardTransformSkip(residual, coefficients, log2TrafoSize, _bitDepth, rotate: false);
            var skipCoded = false;
            if (Quantize(coefficients, skipLevels, log2TrafoSize, scanIdx, qP, rounding))
            {
                rate = _sink.Rate;
                WriteResidualCoding(skipLevels, log2TrafoSize, cIdx, predModeIntra, transformSkipFlag: true);
                rate = _sink.Rate - rate;
                skipLevels.CopyTo(skipBlock);
                ScaleAndTransform(skipBlock, log2TrafoSize, cIdx, transformSkipFlag: true);
                var cost = _decisions.GetCost(GetDistortion(plane, source, xTb, yTb, nTbS, skipBlock), rate);
                if (cost < bestCost)
                {
                    skipCoded = true;
                    coded = true;
                    transformSkipFlag = true;
                    bestCost = cost;
                    skipLevels.CopyTo(levels);
                    skipBlock.CopyTo(block);
                }
            }

            if (!skipCoded)
            {
                RestoreContexts(coded ? HevcEncoderWorkspace.ContextBlockBest : HevcEncoderWorkspace.ContextBlock);
            }
        }
        else if (!coded && _decisions.CodedBlockDecision)
        {
            RestoreContexts(HevcEncoderWorkspace.ContextBlock);
        }

        if (!coded)
            return bestCost;

        StoreLevels(cIdx, xTb, yTb, nTbS, levels);
        AddResidual(plane, xTb, yTb, nTbS, block);
        return bestCost;
    }

    // Quantizes the coefficients of a block, and hides the signs that the syntax does not write (clause 7.3.8.11):
    // returns whether a level is not zero
    private readonly bool Quantize(ReadOnlySpan<int> coefficients, Span<int> levels, int log2TrafoSize, int scanIdx, int qP, int rounding)
    {
        if (HevcQuantizer.Quantize(coefficients, levels, log2TrafoSize, qP, _bitDepth, scalingFactors: default, rounding) is 0)
            return false;

        if (!_pps.SignDataHidingEnabledFlag)
            return true;

        var subBlocks = 1 << (log2TrafoSize - 2);
        var changed = false;
        for (var yS = 0; yS < subBlocks; yS++)
        {
            for (var xS = 0; xS < subBlocks; xS++)
            {
                changed |= HevcQuantizer.HideSign(coefficients, levels, log2TrafoSize, scanIdx, xS, yS, qP, _bitDepth, scalingFactors: default);
            }
        }

        return !changed || levels.ContainsAnyExcept(0);
    }

    // The levels of a block, in the coding unit being searched
    private readonly void StoreLevels(int cIdx, int xTb, int yTb, int nTbS, ReadOnlySpan<int> levels)
    {
        var subWidth = cIdx is 0 ? 1 : _sps.SubWidthC;
        var subHeight = cIdx is 0 ? 1 : _sps.SubHeightC;
        var destination = _workspace.GetLevels(cIdx);
        var offset = ((yTb - (_yCb / subHeight)) * HevcEncoderWorkspace.BlockStride) + xTb - (_xCb / subWidth);
        for (var y = 0; y < nTbS; y++)
        {
            var row = destination.Slice(offset + (y * HevcEncoderWorkspace.BlockStride), nTbS);
            var levelRow = levels.Slice(y * nTbS, nTbS);
            for (var x = 0; x < nTbS; x++)
            {
                row[x] = (short)levelRow[x];
            }
        }
    }

    // The sum of the squared differences between the source and a block whose prediction is in the picture, with the
    // residual that a decoder adds to it (clause 8.6.7)
    private readonly long GetDistortion(Av1Plane plane, Av1Plane source, int xTb, int yTb, int nTbS, ReadOnlySpan<int> residual)
        => GetReconstructionSse(source.Samples[((yTb * source.Stride) + xTb)..], source.Stride, plane.Samples[((yTb * plane.Stride) + xTb)..], plane.Stride, residual, nTbS, _bitDepth);

    // The sum of the squared differences between the source and the samples of a coding unit
    private readonly long GetCodingUnitDistortion(int x0, int y0, int nCbS)
    {
        var distortion = 0L;
        for (var cIdx = 0; cIdx < _sps.ComponentCount; cIdx++)
        {
            var plane = _picture.GetPlane(cIdx);
            var source = _workspace.GetSourcePlane(cIdx);
            GetComponentBlock(cIdx, x0, y0, nCbS, out var x, out var y, out var width, out var height);
            distortion += GetSse(plane.Samples[((y * plane.Stride) + x)..], plane.Stride, source.Samples[((y * source.Stride) + x)..], source.Stride, width, height, _bitDepth);
        }

        return distortion;
    }

    // The samples of a component of a square block of luma samples
    private readonly void GetComponentBlock(int cIdx, int x0, int y0, int size, out int x, out int y, out int width, out int height)
    {
        var subWidth = cIdx is 0 ? 1 : _sps.SubWidthC;
        var subHeight = cIdx is 0 ? 1 : _sps.SubHeightC;
        x = x0 / subWidth;
        y = y0 / subHeight;
        width = size / subWidth;
        height = size / subHeight;
    }

    private readonly void SaveCodingUnitSamples(int slot, int x0, int y0, int size)
    {
        for (var cIdx = 0; cIdx < _sps.ComponentCount; cIdx++)
        {
            CopySamples(cIdx, x0, y0, size, _workspace.GetSavedSamples(slot, cIdx), save: true);
        }
    }

    private readonly void RestoreCodingUnitSamples(int slot, int x0, int y0, int size)
    {
        for (var cIdx = 0; cIdx < _sps.ComponentCount; cIdx++)
        {
            CopySamples(cIdx, x0, y0, size, _workspace.GetSavedSamples(slot, cIdx), save: false);
        }
    }

    // Copies the samples of a component of a block between the picture and a snapshot, which has them row after row
    private readonly void CopySamples(int cIdx, int x0, int y0, int size, Span<ushort> snapshot, bool save)
    {
        var plane = _picture.GetPlane(cIdx);
        GetComponentBlock(cIdx, x0, y0, size, out var x, out var y, out var width, out var height);
        for (var row = 0; row < height; row++)
        {
            var samples = plane.Samples.Slice(((y + row) * plane.Stride) + x, width);
            var saved = snapshot.Slice(row * width, width);
            if (save)
            {
                samples.CopyTo(saved);
            }
            else
            {
                saved.CopyTo(samples);
            }
        }
    }

    // Copies the levels of a component of a block of the coding unit being searched to or from a snapshot
    private readonly void CopyLevels(int slot, int cIdx, int x0, int y0, int size, bool save)
    {
        var levels = _workspace.GetLevels(cIdx);
        var saved = _workspace.GetSavedLevels(slot, cIdx);
        GetComponentBlock(cIdx, x0 - _xCb, y0 - _yCb, size, out var x, out var y, out var width, out var height);
        for (var row = 0; row < height; row++)
        {
            var offset = ((y + row) * HevcEncoderWorkspace.BlockStride) + x;
            if (save)
            {
                levels.Slice(offset, width).CopyTo(saved[offset..]);
            }
            else
            {
                saved.Slice(offset, width).CopyTo(levels[offset..]);
            }
        }
    }

    // Copies a map of a block of the coding unit being searched to or from a snapshot
    private readonly void CopyMap(int slot, int map, int x0, int y0, int size, bool save)
    {
        var values = _workspace.GetMap(map);
        var saved = _workspace.GetSavedMap(slot, map);
        var cell = GetCell(x0, y0);
        var count = size >> 2;
        for (var row = 0; row < count; row++)
        {
            var offset = cell + (row * HevcEncoderWorkspace.CellStride);
            if (save)
            {
                values.Slice(offset, count).CopyTo(saved[offset..]);
            }
            else
            {
                saved.Slice(offset, count).CopyTo(values[offset..]);
            }
        }
    }

    // The luma samples, levels and maps of a block of the coding unit being searched
    private readonly void SaveLuma(int slot, int x0, int y0, int size) => CopyLuma(slot, x0, y0, size, save: true);

    private readonly void RestoreLuma(int slot, int x0, int y0, int size) => CopyLuma(slot, x0, y0, size, save: false);

    private readonly void CopyLuma(int slot, int x0, int y0, int size, bool save)
    {
        CopySamples(0, x0, y0, size, _workspace.GetDraftSamples(slot, 0), save);
        CopyLevels(slot, 0, x0, y0, size, save);
        CopyMap(slot, HevcEncoderWorkspace.MapTrafoDepth, x0, y0, size, save);
        CopyMap(slot, HevcEncoderWorkspace.MapCbfLuma, x0, y0, size, save);
        CopyMap(slot, HevcEncoderWorkspace.MapTransformSkipLuma, x0, y0, size, save);
    }

    // The chroma samples, levels and maps of a block of the coding unit being searched
    private readonly void SaveChroma(int slot, int x0, int y0, int size) => CopyChroma(slot, x0, y0, size, save: true);

    private readonly void RestoreChroma(int slot, int x0, int y0, int size) => CopyChroma(slot, x0, y0, size, save: false);

    private readonly void CopyChroma(int slot, int x0, int y0, int size, bool save)
    {
        for (var cIdx = 1; cIdx <= 2; cIdx++)
        {
            CopySamples(cIdx, x0, y0, size, _workspace.GetDraftSamples(slot, cIdx), save);
            CopyLevels(slot, cIdx, x0, y0, size, save);
        }

        CopyMap(slot, HevcEncoderWorkspace.MapCbfChroma, x0, y0, size, save);
        CopyMap(slot, HevcEncoderWorkspace.MapTransformSkipChroma, x0, y0, size, save);
    }
}
