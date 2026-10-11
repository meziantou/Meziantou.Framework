using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>
/// The search of the encoder: the choice of the partition of a superblock and of the modes of its blocks, and the writing
/// of the choices that were kept. See <see cref="Av1EncoderWorkspace"/> for the records and the saved state it relies on.
/// </content>
/// <remarks>
/// <para>
/// A candidate is evaluated by encoding it for real: its blocks are predicted from the reconstructed samples of the frame,
/// their residual is transformed and quantized, their coefficients are costed with the contexts a decoder would have, and
/// the blocks are reconstructed with the reconstruction process of the decoder. The state of the frame after a candidate is
/// therefore the state after the same blocks in a decoder.
/// </para>
/// <para>
/// The distributions are not adapted during the search of a superblock: every candidate is costed with the distributions
/// the superblock starts with. Once the search is over, the contexts are put back as they were at the start of the
/// superblock and the records are replayed to the symbol encoder, which adapts the distributions like a decoder.
/// </para>
/// </remarks>
internal ref partial struct Av1TileCoder
{
    // A block record: the header, then the records of the transform blocks of each plane. A transform block record is its
    // luma transform type, its end of block and as many levels in scan order
    private const int RecordLength = 0;
    private const int RecordLumaOffset = 1;
    private const int RecordUOffset = 2;
    private const int RecordVOffset = 3;
    private const int RecordFlags = 4;
    private const int RecordYMode = 5;
    private const int RecordUvMode = 6;
    private const int RecordAngleDeltaY = 7;
    private const int RecordAngleDeltaUv = 8;
    private const int RecordFilterIntraMode = 9;
    private const int RecordCflAlphaU = 10;
    private const int RecordCflAlphaV = 11;
    private const int RecordTxSize = 12;
    private const int RecordHeaderLength = 13;
    private const int RecordSkipFlag = 1;
    private const int RecordFilterIntraFlag = 2;

    private const int IntraModeCount = Av1Constants.PaethPred + 1;
    private const long MaxCost = long.MaxValue / 4;

    /// <summary>Sets the limits of the search.</summary>
    public void SetDecisions(in Av1EncoderDecisions decisions) => _decisions = decisions;

    /// <summary>
    /// Encodes the row of superblocks that starts at the row <paramref name="r"/> of 4x4 blocks of a tile: the counterpart of
    /// an iteration of <c>decode_tile</c> (section 5.11.2). The rows of a tile are encoded in order, with the same
    /// symbol encoder and the same distributions.
    /// </summary>
    public void EncodeSuperblockRow(int tileRow, int tileCol, int r)
    {
        var workspace = _workspace!;
        _miRowStart = _header.MiRowStarts[tileRow];
        _miRowEnd = _header.MiRowStarts[tileRow + 1];
        _miColStart = _header.MiColStarts[tileCol];
        _miColEnd = _header.MiColStarts[tileCol + 1];
        if (r == _miRowStart)
        {
            workspace.CurrentQIndex = _header.BaseQIdx;
            ClearAboveContext();
        }

        _currentQIndex = workspace.CurrentQIndex;
        _deltaLf = default;
        var sbSize = _sequence.Use128x128Superblock ? Av1Constants.Block128x128 : Av1Constants.Block64x64;
        var sbSize4 = Av1Tables.Num4x4BlocksWide[sbSize];
        ClearLeftContext();
        for (var c = _miColStart; c < _miColEnd; c += sbSize4)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            _readDeltas = _header.DeltaQPresent;
            ClearCdef(r, c);
            ClearBlockDecodedFlags(r, c, sbSize4);
            EncodeSuperblock(r, c, sbSize, sbSize4);
        }

        workspace.CurrentQIndex = _currentQIndex;
    }

    private void EncodeSuperblock(int r, int c, int sbSize, int sbSize4)
    {
        var workspace = _workspace!;
        CopyContexts(workspace.SuperblockContexts, r, c, sbSize4, sbSize4, save: true);
        _recordPosition = 0;
        _sink = SymbolSink.Cost;
        SearchPartition(r, c, sbSize);

        // The stream: the same blocks, coded from the contexts that the superblock started with
        CopyContexts(workspace.SuperblockContexts, r, c, sbSize4, sbSize4, save: false);
        _recordPosition = 0;
        _sink = SymbolSink.Range;
        ReplayPartition(r, c, sbSize);
    }

    // The search of section 5.11.4 "Decode partition syntax": the partition of the block at (r, c), and what it holds. On
    // return the frame holds the blocks of the best partition, whose records end at _recordPosition
    private long SearchPartition(int r, int c, int bSize)
    {
        if (r >= _miRows || c >= _miCols)
            return 0;

        var workspace = _workspace!;
        var level = Av1Tables.MiWidthLog2[bSize];
        var num4x4 = Av1Tables.Num4x4BlocksWide[bSize];
        var halfBlock4x4 = num4x4 >> 1;
        var hasRows = (r + halfBlock4x4) < _miRows;
        var hasCols = (c + halfBlock4x4) < _miCols;

        // The partitions that the position of the block allows, then the ones that the search considers
        Span<int> candidates = stackalloc int[Av1Constants.PartitionVert4 + 1];
        var count = 0;
        if (bSize < Av1Constants.Block8x8)
        {
            candidates[count++] = Av1Constants.PartitionNone;
        }
        else if (hasRows && hasCols)
        {
            if (level <= _decisions.MaxBlockLevel)
                candidates[count++] = Av1Constants.PartitionNone;

            if (level > _decisions.MinBlockLevel || count is 0)
            {
                candidates[count++] = Av1Constants.PartitionSplit;
                if (_decisions.RectangularPartitions && level <= _decisions.MaxBlockLevel)
                {
                    candidates[count++] = Av1Constants.PartitionHorz;
                    candidates[count++] = Av1Constants.PartitionVert;
                }
            }
        }
        else if (hasCols)
        {
            candidates[count++] = Av1Constants.PartitionSplit;
            if (level <= _decisions.MaxBlockLevel)
                candidates[count++] = Av1Constants.PartitionHorz;
        }
        else if (hasRows)
        {
            candidates[count++] = Av1Constants.PartitionSplit;
            if (level <= _decisions.MaxBlockLevel)
                candidates[count++] = Av1Constants.PartitionVert;
        }
        else
        {
            candidates[count++] = Av1Constants.PartitionSplit;
        }

        var valid = 0;
        for (var i = 0; i < count; i++)
        {
            if (IsPartitionAllowed(bSize, candidates[i]))
                candidates[valid++] = candidates[i];
        }

        Debug.Assert(valid > 0);
        count = valid;

        var start = _recordPosition;
        var contexts = workspace.GetContextSnapshot(level, block: false);
        var pixels = workspace.GetPixelSnapshot(level, block: false);
        if (count > 1)
            CopyContexts(contexts, r, c, num4x4, num4x4, save: true);

        var best = MaxCost;
        var bestLength = 0;
        var lastIsBest = false;
        for (var i = 0; i < count; i++)
        {
            var partition = candidates[i];
            if (i > 0)
                CopyContexts(contexts, r, c, num4x4, num4x4, save: false);

            var candidateStart = start + bestLength;
            _recordPosition = candidateStart;
            workspace.EnsureRecords(_recordPosition, 1);
            workspace.Records[_recordPosition++] = partition;
            _rate = 0;
            WritePartition(r, c, bSize, partition);
            var cost = _decisions.GetRateCost(_rate) + VisitPartition(r, c, bSize, partition, search: true);
            if (cost < best)
            {
                best = cost;
                bestLength = _recordPosition - candidateStart;
                if (candidateStart != start)
                    workspace.Records.Slice(candidateStart, bestLength).CopyTo(workspace.Records[start..]);

                lastIsBest = true;
                if (i < count - 1)
                    CopyPixels(pixels, r, c, num4x4, num4x4, planeMask: 0b111, save: true);
            }
            else
            {
                lastIsBest = false;
            }
        }

        if (!lastIsBest)
        {
            // The frame holds the last candidate: put the best one back
            CopyContexts(contexts, r, c, num4x4, num4x4, save: false);
            var sink = _sink;
            _sink = SymbolSink.None;
            _recordPosition = start;
            ReplayPartition(r, c, bSize);
            _sink = sink;
            CopyPixels(pixels, r, c, num4x4, num4x4, planeMask: 0b111, save: false);
        }

        _recordPosition = start + bestLength;
        return best;
    }

    private readonly bool IsPartitionAllowed(int bSize, int partition)
    {
        var subSize = Av1Tables.PartitionSubsize[(partition * Av1Constants.BlockSizes) + bSize];
        return subSize is not Av1Constants.BlockInvalid && (_planeCount is 1 || GetPlaneResidualSize(subSize, 1) is not Av1Constants.BlockInvalid);
    }

    // Writes the recorded partition of the block at (r, c) and what it holds
    private void ReplayPartition(int r, int c, int bSize)
    {
        if (r >= _miRows || c >= _miCols)
            return;

        var partition = _workspace!.Records[_recordPosition++];
        WritePartition(r, c, bSize, partition);
        VisitPartition(r, c, bSize, partition, search: false);
    }

    // The blocks of a partition, in the order of section 5.11.4 "Decode partition syntax": searched, or replayed
    private long VisitPartition(int r, int c, int bSize, int partition, bool search)
    {
        var num4x4 = Av1Tables.Num4x4BlocksWide[bSize];
        var halfBlock4x4 = num4x4 >> 1;
        var quarterBlock4x4 = halfBlock4x4 >> 1;
        var hasRows = (r + halfBlock4x4) < _miRows;
        var hasCols = (c + halfBlock4x4) < _miCols;
        var subSize = Av1Tables.PartitionSubsize[(partition * Av1Constants.BlockSizes) + bSize];
        var splitSize = Av1Tables.PartitionSubsize[(Av1Constants.PartitionSplit * Av1Constants.BlockSizes) + bSize];
        var level = Av1Tables.MiWidthLog2[bSize];
        long cost = 0;
        switch (partition)
        {
            case Av1Constants.PartitionNone:
                cost += VisitBlock(r, c, subSize, level, search);
                break;
            case Av1Constants.PartitionHorz:
                cost += VisitBlock(r, c, subSize, level, search);
                if (hasRows)
                    cost += VisitBlock(r + halfBlock4x4, c, subSize, level, search);

                break;
            case Av1Constants.PartitionVert:
                cost += VisitBlock(r, c, subSize, level, search);
                if (hasCols)
                    cost += VisitBlock(r, c + halfBlock4x4, subSize, level, search);

                break;
            case Av1Constants.PartitionSplit:
                if (search)
                {
                    cost += SearchPartition(r, c, subSize);
                    cost += SearchPartition(r, c + halfBlock4x4, subSize);
                    cost += SearchPartition(r + halfBlock4x4, c, subSize);
                    cost += SearchPartition(r + halfBlock4x4, c + halfBlock4x4, subSize);
                }
                else
                {
                    ReplayPartition(r, c, subSize);
                    ReplayPartition(r, c + halfBlock4x4, subSize);
                    ReplayPartition(r + halfBlock4x4, c, subSize);
                    ReplayPartition(r + halfBlock4x4, c + halfBlock4x4, subSize);
                }

                break;
            case Av1Constants.PartitionHorzA:
                cost += VisitBlock(r, c, splitSize, level, search);
                cost += VisitBlock(r, c + halfBlock4x4, splitSize, level, search);
                cost += VisitBlock(r + halfBlock4x4, c, subSize, level, search);
                break;
            case Av1Constants.PartitionHorzB:
                cost += VisitBlock(r, c, subSize, level, search);
                cost += VisitBlock(r + halfBlock4x4, c, splitSize, level, search);
                cost += VisitBlock(r + halfBlock4x4, c + halfBlock4x4, splitSize, level, search);
                break;
            case Av1Constants.PartitionVertA:
                cost += VisitBlock(r, c, splitSize, level, search);
                cost += VisitBlock(r + halfBlock4x4, c, splitSize, level, search);
                cost += VisitBlock(r, c + halfBlock4x4, subSize, level, search);
                break;
            case Av1Constants.PartitionVertB:
                cost += VisitBlock(r, c, subSize, level, search);
                cost += VisitBlock(r, c + halfBlock4x4, splitSize, level, search);
                cost += VisitBlock(r + halfBlock4x4, c + halfBlock4x4, splitSize, level, search);
                break;
            case Av1Constants.PartitionHorz4:
                cost += VisitBlock(r, c, subSize, level, search);
                cost += VisitBlock(r + quarterBlock4x4, c, subSize, level, search);
                cost += VisitBlock(r + (quarterBlock4x4 * 2), c, subSize, level, search);
                if (r + (quarterBlock4x4 * 3) < _miRows)
                    cost += VisitBlock(r + (quarterBlock4x4 * 3), c, subSize, level, search);

                break;
            default:
                cost += VisitBlock(r, c, subSize, level, search);
                cost += VisitBlock(r, c + quarterBlock4x4, subSize, level, search);
                cost += VisitBlock(r, c + (quarterBlock4x4 * 2), subSize, level, search);
                if (c + (quarterBlock4x4 * 3) < _miCols)
                    cost += VisitBlock(r, c + (quarterBlock4x4 * 3), subSize, level, search);

                break;
        }

        return cost;
    }

    private long VisitBlock(int r, int c, int subSize, int level, bool search)
    {
        if (search)
            return SearchBlock(r, c, subSize, level);

        ReplayBlock(r, c, subSize);
        return 0;
    }

    // The search of section 5.11.5 "Decode block syntax" for a block of an intra frame: its luma mode, then its chroma mode.
    // On return the frame holds the block, whose record ends at _recordPosition
    private long SearchBlock(int r, int c, int subSize, int level)
    {
        var workspace = _workspace!;
        BeginBlock(r, c, subSize);
        SetIntraBlockDefaults();
        _txSize = Av1Constants.Tx4x4;
        var bw4 = Av1Tables.Num4x4BlocksWide[subSize];
        var bh4 = Av1Tables.Num4x4BlocksHigh[subSize];
        var start = _recordPosition;
        workspace.EnsureRecords(start, RecordHeaderLength);
        var contexts = workspace.GetContextSnapshot(level, block: true);
        var pixels = workspace.GetPixelSnapshot(level, block: true);
        CopyContexts(contexts, r, c, bw4, bh4, save: true);

        // Luma: every mode is ranked by its prediction error, the best ones are encoded
        Span<long> estimates = stackalloc long[IntraModeCount];
        Span<int> modes = stackalloc int[IntraModeCount];
        var lumaStart = start + RecordHeaderLength;
        var lumaCount = RankIntraModes(contexts, r, c, bw4, bh4, planeMask: 0b001, estimates, modes, _decisions.LumaModeCandidates);
        var luma = new LumaSearch { R = r, C = c, Bw4 = bw4, Bh4 = bh4, Level = level, Start = lumaStart, BestCost = MaxCost, BestMode = Av1Constants.DcPred, BestFilterIntra = -1 };
        for (var i = 0; i < lumaCount; i++)
        {
            TryLumaMode(ref luma, modes[i], angleDelta: 0, filterIntraMode: -1);
        }

        if (_decisions.AngleDeltas && subSize >= Av1Constants.Block8x8 && IsDirectionalMode(luma.BestMode))
        {
            var mode = luma.BestMode;
            for (var delta = 1; delta <= Av1Constants.MaxAngleDelta; delta++)
            {
                TryLumaMode(ref luma, mode, delta, filterIntraMode: -1);
                TryLumaMode(ref luma, mode, -delta, filterIntraMode: -1);
            }
        }

        if (_decisions.FilterIntra)
        {
            _yMode = Av1Constants.DcPred;
            if (IsFilterIntraAllowed())
            {
                for (var mode = 0; mode < Av1Constants.IntraFilterModes; mode++)
                {
                    TryLumaMode(ref luma, Av1Constants.DcPred, angleDelta: 0, mode);
                }
            }
        }

        _yMode = luma.BestMode;
        _angleDeltaY = luma.BestAngle;
        _useFilterIntra = luma.BestFilterIntra >= 0;
        _filterIntraMode = Math.Max(luma.BestFilterIntra, 0);
        var hasCoefficients = luma.BestHasCoefficients;
        if (!luma.LastIsBest)
            CopyPixels(pixels, r, c, bw4, bh4, planeMask: 0b001, save: false);

        var bestLength = luma.BestLength;
        long bestCost;
        bool bestHasCoefficients;
        bool lastIsBest;
        var chromaStart = lumaStart + bestLength;
        var uLength = 0;
        var chromaLength = 0;
        if (_hasChroma)
        {
            // The luma contexts are stale from here on: the replay of the record that ends the search rebuilds them
            var chromaCount = RankIntraModes(contexts, r, c, bw4, bh4, planeMask: 0b110, estimates, modes, _decisions.ChromaModeCandidates);
            bestCost = MaxCost;
            var bestUvMode = Av1Constants.DcPred;
            bestHasCoefficients = false;
            lastIsBest = false;
            _uvMode = Av1Constants.DcPred;
            for (var i = 0; i < chromaCount; i++)
            {
                CopyContexts(contexts, r, c, bw4, bh4, save: false);
                var candidateStart = chromaStart + chromaLength;
                _recordPosition = candidateStart;
                _uvMode = modes[i];
                _rate = 0;
                _hasCoefficients = false;
                WriteUvModeInfo();
                Residual(ResidualOperation.Search, planeMask: 0b010);
                var candidateULength = _recordPosition - candidateStart;
                Residual(ResidualOperation.Search, planeMask: 0b100);
                var cost = _decisions.GetRateCost(_rate);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestUvMode = _uvMode;
                    bestHasCoefficients = _hasCoefficients;
                    uLength = candidateULength;
                    chromaLength = _recordPosition - candidateStart;
                    if (candidateStart != chromaStart)
                        workspace.Records.Slice(candidateStart, chromaLength).CopyTo(workspace.Records[chromaStart..]);

                    lastIsBest = true;
                    if (i < chromaCount - 1)
                        CopyPixels(pixels, r, c, bw4, bh4, planeMask: 0b110, save: true);
                }
                else
                {
                    lastIsBest = false;
                }
            }

            _uvMode = bestUvMode;
            hasCoefficients |= bestHasCoefficients;
            if (!lastIsBest)
                CopyPixels(pixels, r, c, bw4, bh4, planeMask: 0b110, save: false);
        }

        // A block without coefficient is a skipped block, which codes no transform block
        _skip = !hasCoefficients;
        var end = chromaStart + chromaLength;
        var record = workspace.Records.Slice(start, RecordHeaderLength);
        record[RecordLength] = end - start;
        record[RecordLumaOffset] = RecordHeaderLength;
        record[RecordUOffset] = chromaStart - start;
        record[RecordVOffset] = chromaStart + uLength - start;
        record[RecordFlags] = (_skip ? RecordSkipFlag : 0) | (_useFilterIntra ? RecordFilterIntraFlag : 0);
        record[RecordYMode] = _yMode;
        record[RecordUvMode] = _uvMode;
        record[RecordAngleDeltaY] = _angleDeltaY;
        record[RecordAngleDeltaUv] = _angleDeltaUv;
        record[RecordFilterIntraMode] = _filterIntraMode;
        record[RecordCflAlphaU] = _cflAlphaU;
        record[RecordCflAlphaV] = _cflAlphaV;
        record[RecordTxSize] = _txSize;

        // The cost of the block, and the state that a decoder has after it
        CopyContexts(contexts, r, c, bw4, bh4, save: false);
        _recordPosition = start;
        var sink = _sink;
        _sink = SymbolSink.Cost;
        _rate = 0;
        ReplayBlock(r, c, subSize);
        _sink = sink;
        Debug.Assert(_recordPosition == end);
        return _decisions.GetRateCost(_rate);
    }

    [StructLayout(LayoutKind.Auto)]
    private struct LumaSearch
    {
        public int R;
        public int C;
        public int Bw4;
        public int Bh4;
        public int Level;
        public int Start;
        public long BestCost;
        public int BestMode;
        public int BestAngle;
        public int BestFilterIntra;
        public int BestLength;
        public bool BestHasCoefficients;
        public bool LastIsBest;
    }

    // Encodes the luma transform blocks of the block with a mode, and keeps their records and their samples when the mode
    // is the best one so far
    private void TryLumaMode(ref LumaSearch search, int mode, int angleDelta, int filterIntraMode)
    {
        var workspace = _workspace!;
        CopyContexts(workspace.GetContextSnapshot(search.Level, block: true), search.R, search.C, search.Bw4, search.Bh4, save: false);
        var candidateStart = search.Start + search.BestLength;
        _recordPosition = candidateStart;
        _yMode = mode;
        _angleDeltaY = angleDelta;
        _useFilterIntra = filterIntraMode >= 0;
        _filterIntraMode = Math.Max(filterIntraMode, 0);
        _rate = 0;
        _hasCoefficients = false;
        WriteSymbol(GetIntraFrameYModeCdf(), Av1CdfLayout.IntraFrameYModeSize, _yMode);
        WriteIntraAngleInfoY();
        WriteFilterIntraModeInfo();
        Residual(ResidualOperation.Search, planeMask: 0b001);
        var cost = _decisions.GetRateCost(_rate);
        if (cost >= search.BestCost)
        {
            search.LastIsBest = false;
            return;
        }

        search.BestCost = cost;
        search.BestMode = mode;
        search.BestAngle = angleDelta;
        search.BestFilterIntra = filterIntraMode;
        search.BestHasCoefficients = _hasCoefficients;
        search.BestLength = _recordPosition - candidateStart;
        if (candidateStart != search.Start)
            workspace.Records.Slice(candidateStart, search.BestLength).CopyTo(workspace.Records[search.Start..]);

        search.LastIsBest = true;
        CopyPixels(workspace.GetPixelSnapshot(search.Level, block: true), search.R, search.C, search.Bw4, search.Bh4, planeMask: 0b001, save: true);
    }

    // Orders the intra modes of the planes of planeMask by the error of their prediction, and returns the number of modes
    // to encode. The error is measured against the samples of the frame to encode, which also stand for the reconstructed
    // samples of the transform blocks that precede a transform block
    private int RankIntraModes(Span<byte> contexts, int r, int c, int bw4, int bh4, int planeMask, scoped Span<long> estimates, scoped Span<int> modes, int count)
    {
        for (var mode = 0; mode < IntraModeCount; mode++)
        {
            modes[mode] = mode;
        }

        if (count >= IntraModeCount)
            return IntraModeCount;

        var (yMode, uvMode) = (_yMode, _uvMode);
        _estimating = true;
        for (var mode = 0; mode < IntraModeCount; mode++)
        {
            CopyContexts(contexts, r, c, bw4, bh4, save: false);
            if (planeMask is 0b001)
            {
                _yMode = mode;
            }
            else
            {
                _uvMode = mode;
            }

            _estimate = 0;
            Residual(ResidualOperation.Search, planeMask);
            estimates[mode] = _estimate;
        }

        _estimating = false;
        (_yMode, _uvMode) = (yMode, uvMode);

        // An insertion sort keeps the order of the modes that have the same error, so the result does not depend on the
        // implementation of a sort
        for (var i = 1; i < IntraModeCount; i++)
        {
            var mode = modes[i];
            var j = i;
            while (j > 0 && estimates[modes[j - 1]] > estimates[mode])
            {
                modes[j] = modes[j - 1];
                j--;
            }

            modes[j] = mode;
        }

        return count;
    }

    // Writes the recorded block at (r, c): section 5.11.5 "Decode block syntax" from the variables of the record. The
    // prediction and the reconstruction are not done again
    private void ReplayBlock(int r, int c, int subSize)
    {
        var start = _recordPosition;
        var record = _workspace!.Records.Slice(start, RecordHeaderLength);
        BeginBlock(r, c, subSize);
        SetIntraBlockDefaults();
        var flags = record[RecordFlags];
        _skip = (flags & RecordSkipFlag) is not 0;
        _useFilterIntra = (flags & RecordFilterIntraFlag) is not 0;
        _yMode = record[RecordYMode];
        _uvMode = record[RecordUvMode];
        _angleDeltaY = record[RecordAngleDeltaY];
        _angleDeltaUv = record[RecordAngleDeltaUv];
        _filterIntraMode = record[RecordFilterIntraMode];
        _cflAlphaU = record[RecordCflAlphaU];
        _cflAlphaV = record[RecordCflAlphaV];
        _txSize = record[RecordTxSize];
        _transformBlockCursor[0] = start + record[RecordLumaOffset];
        _transformBlockCursor[1] = start + record[RecordUOffset];
        _transformBlockCursor[2] = start + record[RecordVOffset];
        var length = record[RecordLength];

        var bw4 = Av1Tables.Num4x4BlocksWide[subSize];
        var bh4 = Av1Tables.Num4x4BlocksHigh[subSize];
        WriteIntraFrameModeInfo();
        WriteBlockTxSize();
        if (_skip)
            ResetBlockContext(bw4, bh4);

        StoreBlock(bw4, bh4);
        Residual(ResidualOperation.Replay, planeMask: 0b111);
        _recordPosition = start + length;
    }

    // The counterpart of section 5.11.35 "Transform block syntax" in the search: predicts the transform block, transforms
    // and quantizes its residual, records and costs its coefficients, and reconstructs it
    private void SearchTransformBlock(int plane, int baseX, int baseY, int txSz, int x, int y)
    {
        var startX = baseX + (4 * x);
        var startY = baseY + (4 * y);
        if (!IsTransformBlockInFrame(plane, startX, startY))
            return;

        PredictTransformBlock(plane, txSz, startX, startY, x, y);
        var workspace = _workspace!;
        var planeBuffer = _frame.GetPlane(plane);
        var samples = planeBuffer.Samples;
        var stride = planeBuffer.Stride;
        var sourcePlane = workspace.GetSource(plane);
        var source = sourcePlane.Samples;
        var sourceStride = sourcePlane.Stride;
        var w = Av1Tables.TxWidth[txSz];
        var h = Av1Tables.TxHeight[txSz];
        if (_estimating)
        {
            long error = 0;
            for (var i = 0; i < h; i++)
            {
                var predicted = samples.Slice(((startY + i) * stride) + startX, w);
                var original = source.Slice(((startY + i) * sourceStride) + startX, w);
                for (var j = 0; j < w; j++)
                {
                    error += Math.Abs(original[j] - predicted[j]);
                }

                original.CopyTo(predicted);
            }

            _estimate += error;
            FinishTransformBlock(plane, txSz, startX, startY);
            return;
        }

        if (!_lossless)
            throw new NotSupportedException("Lossy transform blocks are not encoded yet.");

        var residual = workspace.Residual[..(w * h)];
        for (var i = 0; i < h; i++)
        {
            var predicted = samples.Slice(((startY + i) * stride) + startX, w);
            var original = source.Slice(((startY + i) * sourceStride) + startX, w);
            for (var j = 0; j < w; j++)
            {
                residual[(i * w) + j] = original[j] - predicted[j];
            }
        }

        var coefficients = workspace.Coefficients;
        Av1ForwardTransform.ForwardWalshHadamard4x4(residual, coefficients);

        // The record: the levels in scan order, up to the last one that is not zero
        const int LumaTxType = Av1Constants.DctDct;
        var planeTxType = plane is 0 ? GetLumaPlaneTxType(txSz, LumaTxType) : ComputeTxType(plane, txSz, startX >> 2, startY >> 2);
        var scan = GetScan(txSz, planeTxType);
        var count = w * h;
        workspace.EnsureRecords(_recordPosition, 2 + count);
        var record = workspace.Records.Slice(_recordPosition, 2 + count);
        var eob = 0;
        for (var i = 0; i < count; i++)
        {
            var level = coefficients[scan[i]];
            record[2 + i] = level;
            if (level is not 0)
                eob = i + 1;
        }

        record[0] = LumaTxType;
        record[1] = eob;
        _recordPosition += 2 + eob;
        WriteCoeffs(plane, startX, startY, txSz, LumaTxType, record.Slice(2, eob), eob);
        if (eob > 0)
        {
            _hasCoefficients = true;
            Reconstruct(samples, stride, plane, startX, startY, txSz);
        }

        FinishTransformBlock(plane, txSz, startX, startY);
    }

    // The counterpart of section 5.11.35 "Transform block syntax" for a recorded block: writes the coefficients
    private void ReplayTransformBlock(int plane, int baseX, int baseY, int txSz, int x, int y)
    {
        var startX = baseX + (4 * x);
        var startY = baseY + (4 * y);
        if (!IsTransformBlockInFrame(plane, startX, startY))
            return;

        if (!_skip)
        {
            var records = _workspace!.Records;
            var position = _transformBlockCursor[plane];
            var eob = records[position + 1];
            WriteCoeffs(plane, startX, startY, txSz, records[position], records.Slice(position + 2, eob), eob);
            _transformBlockCursor[plane] = position + 2 + eob;
        }

        FinishTransformBlock(plane, txSz, startX, startY);
    }

    // Saves or restores the contexts that the blocks of a region overwrite: the level and DC contexts above and to the left
    // of the transform blocks, and the flags of the blocks that the intra prediction may read. The region is bw4 x bh4
    // blocks of 4x4 luma samples at (r, c); the chroma blocks that share a 4x4 chroma block with it are included
    private readonly void CopyContexts(Span<byte> snapshot, int r, int c, int bw4, int bh4, bool save)
    {
        var position = 0;
        for (var plane = 0; plane < _planeCount; plane++)
        {
            var subX = plane > 0 ? _subX : 0;
            var subY = plane > 0 ? _subY : 0;
            var x0 = c >> subX;
            var nx = ((c + bw4 + subX) >> subX) - x0;
            var y0 = r >> subY;
            var ny = ((r + bh4 + subY) >> subY) - y0;
            Copy(_state.GetAboveLevelContext(plane).Slice(x0, nx), snapshot.Slice(position, nx), save);
            position += nx;
            Copy(_state.GetAboveDcContext(plane).Slice(x0, nx), snapshot.Slice(position, nx), save);
            position += nx;
            var leftLevel = _scratch.GetLeftLevelContext(plane);
            var leftDc = _scratch.GetLeftDcContext(plane);
            var blockDecoded = _scratch.GetBlockDecoded(plane);
            var flagRow = (r & _sbMask) >> subY;
            var flagColumn = (c & _sbMask) >> subX;
            for (var i = 0; i < ny; i++)
            {
                var index = (y0 + i) & 31;
                if (save)
                {
                    snapshot[position] = leftLevel[index];
                    snapshot[position + 1] = leftDc[index];
                }
                else
                {
                    leftLevel[index] = snapshot[position];
                    leftDc[index] = snapshot[position + 1];
                }

                position += 2;
                Copy(blockDecoded.Slice(BlockDecodedIndex(flagRow + i, flagColumn), nx), snapshot.Slice(position, nx), save);
                position += nx;
            }
        }

        static void Copy(Span<byte> state, Span<byte> saved, bool save)
        {
            if (save)
            {
                state.CopyTo(saved);
            }
            else
            {
                saved.CopyTo(state);
            }
        }
    }

    // Saves or restores the reconstructed samples of a region, for the planes of planeMask: each plane has a third of the
    // snapshot
    private readonly void CopyPixels(Span<ushort> snapshot, int r, int c, int bw4, int bh4, int planeMask, bool save)
    {
        var planeLength = snapshot.Length / 3;
        for (var plane = 0; plane < _planeCount; plane++)
        {
            if (((planeMask >> plane) & 1) is 0)
                continue;

            var subX = plane > 0 ? _subX : 0;
            var subY = plane > 0 ? _subY : 0;
            var x0 = (c >> subX) * Av1Constants.MiSize;
            var width = (((c + bw4 + subX) >> subX) * Av1Constants.MiSize) - x0;
            var y0 = (r >> subY) * Av1Constants.MiSize;
            var height = (((r + bh4 + subY) >> subY) * Av1Constants.MiSize) - y0;
            var planeBuffer = _frame.GetPlane(plane);
            var samples = planeBuffer.Samples;
            var stride = planeBuffer.Stride;
            var saved = snapshot.Slice(plane * planeLength, planeLength);
            for (var i = 0; i < height; i++)
            {
                var row = samples.Slice(((y0 + i) * stride) + x0, width);
                var savedRow = saved.Slice(i * width, width);
                if (save)
                {
                    row.CopyTo(savedRow);
                }
                else
                {
                    savedRow.CopyTo(row);
                }
            }
        }
    }
}
