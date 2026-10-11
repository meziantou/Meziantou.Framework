using System.Diagnostics;
using System.Numerics;
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
    // A block record: the header, then the records of the transform blocks of each plane, then the palettes of the block.
    // A transform block record is its luma transform type, its end of block and as many levels in scan order
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
    private const int RecordRefMvIdx = 13;
    private const int RecordMvRow = 14;
    private const int RecordMvCol = 15;
    private const int RecordPaletteSizeY = 16;
    private const int RecordPaletteSizeUv = 17;
    private const int RecordPaletteOffset = 18;
    private const int RecordHeaderLength = 19;
    private const int RecordSkipFlag = 1;
    private const int RecordFilterIntraFlag = 2;
    private const int RecordInterFlag = 4;

    private const int IntraModeCount = Av1Constants.PaethPred + 1;

    // The largest RefMvIdx of the NEARMV mode
    private const int MaxNearMvIndex = 3;

    // The largest difference that the MV syntax codes, in eighths of a sample
    private const int MaxMvDifference = Av1Constants.Class0Size << (Av1Constants.MvClasses + 2);

    // The limits of the motion search: the samples beyond an edge of the reference frame that a prediction may start at,
    // the largest motion vector, and the number of moves at a step
    private const int MotionMargin = 8;
    private const int MaxMotionSamples = 1023;
    private const int MaxMotionIterations = 8;
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

                if (_decisions.ExtendedPartitions && level <= _decisions.MaxBlockLevel && bSize > Av1Constants.Block8x8)
                {
                    // A half and two quarters, and four strips: a block of 8x8 samples has neither, and the block sizes
                    // that have no strips are filtered below
                    candidates[count++] = Av1Constants.PartitionHorzA;
                    candidates[count++] = Av1Constants.PartitionHorzB;
                    candidates[count++] = Av1Constants.PartitionVertA;
                    candidates[count++] = Av1Constants.PartitionVertB;
                    candidates[count++] = Av1Constants.PartitionHorz4;
                    candidates[count++] = Av1Constants.PartitionVert4;
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
        var noneCost = MaxCost;
        var splitCost = MaxCost;
        for (var i = 0; i < count; i++)
        {
            var partition = candidates[i];
            if (partition is Av1Constants.PartitionHorz or Av1Constants.PartitionVert && !_decisions.ExhaustiveSearch && noneCost is not MaxCost && splitCost is not MaxCost)
            {
                // Two rectangles are between the block as a whole and the block split in four: they are tried when
                // these two cost about the same, within a quarter
                if (Math.Min(noneCost, splitCost) * 4 < Math.Max(noneCost, splitCost) * 3)
                    continue;
            }

            if (i > 0)
                CopyContexts(contexts, r, c, num4x4, num4x4, save: false);

            var candidateStart = start + bestLength;
            _recordPosition = candidateStart;
            workspace.EnsureRecords(_recordPosition, 1);
            workspace.Records[_recordPosition++] = partition;
            _rate = 0;
            WritePartition(r, c, bSize, partition);
            _isPredictedExactly = false;
            var cost = _decisions.GetCost(0, _rate) + VisitPartition(r, c, bSize, partition, search: true);

            // A block that is predicted without residual is not split: its parts would be predicted the same
            var isFinal = partition is Av1Constants.PartitionNone && _isPredictedExactly;
            _isPredictedExactly = false;
            if (partition is Av1Constants.PartitionNone)
            {
                noneCost = cost;
            }
            else if (partition is Av1Constants.PartitionSplit)
            {
                splitCost = cost;
            }
            if (cost < best)
            {
                best = cost;
                bestLength = _recordPosition - candidateStart;
                if (candidateStart != start)
                    workspace.Records.Slice(candidateStart, bestLength).CopyTo(workspace.Records[start..]);

                lastIsBest = true;
                if (i < count - 1 && !isFinal)
                    CopyPixels(pixels, r, c, num4x4, num4x4, planeMask: 0b111, save: true);
            }
            else
            {
                lastIsBest = false;
            }

            if (isFinal)
                break;
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

    // The search of section 5.11.5 "Decode block syntax". On return the frame holds the block, whose record ends at
    // _recordPosition. A block of an inter frame is predicted from the previous frame, unless the intra prediction costs less
    private long SearchBlock(int r, int c, int subSize, int level)
    {
        if (_header.FrameIsIntra)
            return SearchIntraBlock(r, c, subSize, level);

        var workspace = _workspace!;
        var bw4 = Av1Tables.Num4x4BlocksWide[subSize];
        var bh4 = Av1Tables.Num4x4BlocksHigh[subSize];
        var start = _recordPosition;
        var contexts = workspace.GetContextSnapshot(level, block: true);
        BeginBlock(r, c, subSize);
        CopyContexts(contexts, r, c, bw4, bh4, save: true);
        var search = new InterSearch { R = r, C = c, SubSize = subSize, Bw4 = bw4, Bh4 = bh4, Level = level, Start = start, BestCost = MaxCost };
        SearchInterModes(ref search);
        _isPredictedExactly = search.BestHasNoResidual;

        // A block that the previous frame predicts without coefficient is not compared with the intra prediction
        if (!search.BestIsSkipped)
        {
            CopyContexts(contexts, r, c, bw4, bh4, save: false);
            var intraStart = start + search.BestLength;
            _recordPosition = intraStart;
            var intraCost = SearchIntraBlock(r, c, subSize, level);
            if (intraCost < search.BestCost)
            {
                var length = _recordPosition - intraStart;
                workspace.Records.Slice(intraStart, length).CopyTo(workspace.Records[start..]);
                _recordPosition = start + length;
                return intraCost;
            }

            _isPredictedExactly = false;
            search.LastIsBest = false;
        }

        if (!search.LastIsBest)
        {
            // The frame holds another candidate: put the best one back
            CopyContexts(contexts, r, c, bw4, bh4, save: false);
            var sink = _sink;
            _sink = SymbolSink.None;
            _recordPosition = start;
            ReplayBlock(r, c, subSize);
            _sink = sink;
            CopyPixels(workspace.GetCandidatePixels(level), r, c, bw4, bh4, planeMask: 0b111, save: false);
        }

        _recordPosition = start + search.BestLength;
        return search.BestCost;
    }

    [StructLayout(LayoutKind.Auto)]
    private struct InterSearch
    {
        public int R;
        public int C;
        public int SubSize;
        public int Bw4;
        public int Bh4;
        public int Level;
        public int Start;
        public long BestCost;
        public int BestLength;
        public bool BestIsSkipped;
        public bool BestHasNoResidual;
        public bool LastIsBest;
        public int BestMode;
        public int BestRefMvIdx;
        public int BestMvRow;
        public int BestMvCol;
    }

    // The search of the inter modes of a block: the motion vectors of the stack that a decoder builds for the block, then
    // the motion vector of a motion search. The contexts of the start of the block are in the block snapshot of the level
    private void SearchInterModes(ref InterSearch search)
    {
        SetInterBlockDefaults();
        FindMvStack(isCompound: false);
        var numMvFound = _numMvFound;
        Span<int> stack = stackalloc int[2 * (MaxNearMvIndex + 1)];
        for (var i = 0; i <= MaxNearMvIndex; i++)
        {
            stack[2 * i] = _refStackMv[i][0];
            stack[(2 * i) + 1] = _refStackMv[i][1];
        }

        // A motion vector is encoded once, with the first mode that has it: GLOBALMV, NEARESTMV, NEARMV with each
        // RefMvIdx, then NEWMV. The search stops at a motion vector whose prediction leaves no residual: no other one
        // predicts the block better
        Span<int> tried = stackalloc int[2 * (MaxNearMvIndex + 3)];
        var triedCount = 0;
        TryInterMode(ref search, Av1Constants.Globalmv, refMvIdx: 0, _globalMvs[0], _globalMvs[1], tried, ref triedCount);
        if (!search.BestHasNoResidual)
            TryInterMode(ref search, Av1Constants.Nearestmv, refMvIdx: 0, stack[0], stack[1], tried, ref triedCount);

        for (var idx = 1; idx <= Math.Min(_decisions.NearMvCandidates, MaxNearMvIndex) && !search.BestHasNoResidual; idx++)
        {
            // RefMvIdx is 1 unless the stack has more motion vectors
            if (idx > 1 && numMvFound <= idx)
                break;

            TryInterMode(ref search, Av1Constants.Nearmv, idx, stack[2 * idx], stack[(2 * idx) + 1], tried, ref triedCount);
        }

        if (_decisions.MotionStep is 0 || search.BestHasNoResidual)
        {
            RefineInterMode(ref search);
            return;
        }

        // The motion search predicts the block with motion vectors that are not the ones of the best candidate
        SearchMotion(search.Bw4, search.Bh4, stack, tried[..(2 * triedCount)], out var mvRow, out var mvCol);
        search.LastIsBest = false;

        // NEWMV codes the difference with the motion vector of the stack that RefMvIdx selects: the nearest one
        var refMvIdx = -1;
        var smallest = int.MaxValue;
        for (var idx = 0; idx < 3; idx++)
        {
            if (idx > 0 && numMvFound <= idx)
                break;

            var diffRow = Math.Abs(mvRow - stack[2 * idx]);
            var diffCol = Math.Abs(mvCol - stack[(2 * idx) + 1]);
            if (Math.Max(diffRow, diffCol) > MaxMvDifference || diffRow + diffCol >= smallest)
                continue;

            smallest = diffRow + diffCol;
            refMvIdx = idx;
        }

        if (refMvIdx >= 0)
            TryInterMode(ref search, Av1Constants.Newmv, refMvIdx, mvRow, mvCol, tried, ref triedCount);

        RefineInterMode(ref search);
    }

    // The transform types of the transform blocks of the best inter mode, when it has coefficients
    private void RefineInterMode(ref InterSearch search)
    {
        if (_decisions.TransformTypeCandidates <= 1 || _lossless || search.BestIsSkipped || search.BestCost is MaxCost)
            return;

        var triedCount = 0;
        _searchesTransformTypes = true;
        TryInterMode(ref search, search.BestMode, search.BestRefMvIdx, search.BestMvRow, search.BestMvCol, tried: default, ref triedCount);
        _searchesTransformTypes = false;
    }

    // Encodes the block with an inter mode, and keeps its record and its samples when it is the best candidate so far
    private void TryInterMode(ref InterSearch search, int mode, int refMvIdx, int mvRow, int mvCol, scoped Span<int> tried, ref int triedCount)
    {
        if (!tried.IsEmpty)
        {
            for (var i = 0; i < triedCount; i++)
            {
                if (tried[2 * i] == mvRow && tried[(2 * i) + 1] == mvCol)
                    return;
            }

            tried[2 * triedCount] = mvRow;
            tried[(2 * triedCount) + 1] = mvCol;
            triedCount++;
        }

        var workspace = _workspace!;
        var (r, c, bw4, bh4) = (search.R, search.C, search.Bw4, search.Bh4);
        var contexts = workspace.GetContextSnapshot(search.Level, block: true);
        var candidateStart = search.Start + search.BestLength;
        workspace.EnsureRecords(candidateStart, RecordHeaderLength);
        CopyContexts(contexts, r, c, bw4, bh4, save: false);
        SetInterBlockDefaults();
        _yMode = mode;
        _refMvIdx = refMvIdx;
        _mv[0] = mvRow;
        _mv[1] = mvCol;
        Debug.Assert(IsMvValid(isCompound: false));
        _recordPosition = candidateStart + RecordHeaderLength;
        WriteBlockTxSize();
        StoreBlock(bw4, bh4);
        ComputePrediction();
        var predictionError = _lossless ? 0 : GetBlockError();
        _rate = 0;
        _distortion = 0;
        _hasCoefficients = false;
        Residual(ResidualOperation.Search, planeMask: 0b001);
        var uOffset = _recordPosition - candidateStart;
        Residual(ResidualOperation.Search, planeMask: 0b010);
        var vOffset = _recordPosition - candidateStart;
        Residual(ResidualOperation.Search, planeMask: 0b100);
        var coefficientRate = _rate;
        var distortion = _distortion;
        var isSkipped = !_hasCoefficients;
        var hasNoResidual = isSkipped;

        // A block without coefficient is a skipped block, which codes no transform block
        var length = isSkipped ? RecordHeaderLength : _recordPosition - candidateStart;
        var record = workspace.Records.Slice(candidateStart, RecordHeaderLength);
        record.Clear();
        record[RecordLength] = length;
        record[RecordLumaOffset] = RecordHeaderLength;
        record[RecordUOffset] = uOffset;
        record[RecordVOffset] = vOffset;
        record[RecordFlags] = RecordInterFlag | (isSkipped ? RecordSkipFlag : 0);
        record[RecordYMode] = mode;
        record[RecordTxSize] = _txSize;
        record[RecordRefMvIdx] = refMvIdx;
        record[RecordMvRow] = mvRow;
        record[RecordMvCol] = mvCol;

        // The cost of the block, and the state that a decoder has after it
        CopyContexts(contexts, r, c, bw4, bh4, save: false);
        _recordPosition = candidateStart;
        var sink = _sink;
        _sink = SymbolSink.Cost;
        _rate = 0;
        ReplayBlock(r, c, search.SubSize);
        var cost = _decisions.GetCost(distortion, _rate);
        if (!isSkipped && !_lossless)
        {
            // The same block without its coefficients: the syntax elements of its mode info are the same, apart from skip,
            // and it does not code the size of its transform blocks
            var skipCdf = _cdf.Slice(Av1CdfLayout.Skip + (GetSkipContext() * Av1CdfLayout.SkipSize), Av1CdfLayout.SkipSize);
            var skipRate = _rate - coefficientRate - _transformSizeRate - Av1Cost.GetSymbolCost(skipCdf, 0) + Av1Cost.GetSymbolCost(skipCdf, 1);
            var skipCost = _decisions.GetCost(predictionError, skipRate);
            if (skipCost < cost)
            {
                isSkipped = true;
                cost = skipCost;
                length = RecordHeaderLength;
                record[RecordLength] = length;
                record[RecordFlags] = RecordInterFlag | RecordSkipFlag;
                CopyContexts(contexts, r, c, bw4, bh4, save: false);
                _recordPosition = candidateStart;
                _sink = SymbolSink.None;
                ReplayBlock(r, c, search.SubSize);
                ComputePrediction();
            }
        }

        _sink = sink;
        if (cost >= search.BestCost)
        {
            search.LastIsBest = false;
            return;
        }

        search.BestCost = cost;
        search.BestLength = length;
        search.BestIsSkipped = isSkipped;
        search.BestHasNoResidual = hasNoResidual;
        search.LastIsBest = true;
        search.BestMode = mode;
        search.BestRefMvIdx = refMvIdx;
        search.BestMvRow = mvRow;
        search.BestMvCol = mvCol;
        if (candidateStart != search.Start)
            workspace.Records.Slice(candidateStart, length).CopyTo(workspace.Records[search.Start..]);

        CopyPixels(workspace.GetCandidatePixels(search.Level), r, c, bw4, bh4, planeMask: 0b111, save: true);
    }

    // The squared error of the samples of the block that are part of the frame, for the planes that the block codes
    private readonly long GetBlockError()
    {
        var workspace = _workspace!;
        long error = 0;
        for (var plane = 0; plane < 1 + (_hasChroma ? 2 : 0); plane++)
        {
            var subX = plane > 0 ? _subX : 0;
            var subY = plane > 0 ? _subY : 0;
            var planeSz = GetPlaneResidualSize(_miSize, plane);
            var planeBuffer = _frame.GetPlane(plane);
            var samples = planeBuffer.Samples;
            var stride = planeBuffer.Stride;
            var sourcePlane = workspace.GetSource(plane);
            var source = sourcePlane.Samples;
            var sourceStride = sourcePlane.Stride;
            var x0 = (_miCol >> subX) * Av1Constants.MiSize;
            var y0 = (_miRow >> subY) * Av1Constants.MiSize;
            var width = Math.Max(0, Math.Min(Av1Tables.Num4x4BlocksWide[planeSz] * Av1Constants.MiSize, planeBuffer.Width - x0));
            var height = Math.Max(0, Math.Min(Av1Tables.Num4x4BlocksHigh[planeSz] * Av1Constants.MiSize, planeBuffer.Height - y0));
            for (var i = 0; i < height; i++)
            {
                var predicted = samples.Slice(((y0 + i) * stride) + x0, width);
                var original = source.Slice(((y0 + i) * sourceStride) + x0, width);
                for (var j = 0; j < width; j++)
                {
                    var difference = original[j] - predicted[j];
                    error += difference * difference;
                }
            }
        }

        return error;
    }

    // The search of section 5.11.5 "Decode block syntax" for a block that is not inter: its luma mode, then its chroma
    // mode. On return the frame holds the block, whose record ends at _recordPosition
    private long SearchIntraBlock(int r, int c, int subSize, int level)
    {
        var workspace = _workspace!;
        BeginBlock(r, c, subSize);
        SetIntraBlockDefaults();
        _txSize = _lossless ? Av1Constants.Tx4x4 : Av1Tables.MaxTxSizeRect[subSize];
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
        var maxTxSize = _txSize;
        var luma = new LumaSearch { R = r, C = c, Bw4 = bw4, Bh4 = bh4, Level = level, Start = lumaStart, BestCost = MaxCost, BestMode = Av1Constants.DcPred, BestFilterIntra = -1, BestTxSize = maxTxSize };
        for (var i = 0; i < lumaCount; i++)
        {
            TryLumaMode(ref luma, modes[i], angleDelta: 0, filterIntraMode: -1, maxTxSize);
        }

        if (IsPaletteAllowed())
        {
            // The palette of a block of few colors predicts it exactly
            var paletteSize = FindLumaPalette();
            if (paletteSize > 0)
                TryLumaMode(ref luma, Av1Constants.DcPred, angleDelta: 0, filterIntraMode: -1, maxTxSize, paletteSize);
        }

        if (_decisions.AngleDeltas && subSize >= Av1Constants.Block8x8 && IsDirectionalMode(luma.BestMode))
        {
            var mode = luma.BestMode;
            if (_decisions.ExhaustiveSearch)
            {
                for (var delta = 1; delta <= Av1Constants.MaxAngleDelta; delta++)
                {
                    TryLumaMode(ref luma, mode, delta, filterIntraMode: -1, maxTxSize);
                    TryLumaMode(ref luma, mode, -delta, filterIntraMode: -1, maxTxSize);
                }
            }
            else
            {
                // The angle delta whose prediction is the closest to the block, when it is closer than the one of the mode
                var bestDelta = 0;
                var bestError = EstimateLumaMode(contexts, r, c, bw4, bh4, mode, angleDelta: 0, filterIntraMode: -1);
                for (var delta = -Av1Constants.MaxAngleDelta; delta <= Av1Constants.MaxAngleDelta; delta++)
                {
                    if (delta is 0)
                        continue;

                    var error = EstimateLumaMode(contexts, r, c, bw4, bh4, mode, delta, filterIntraMode: -1);
                    if (error < bestError)
                        (bestError, bestDelta) = (error, delta);
                }

                // The estimates predicted the block: the frame does not hold the best mode any more
                luma.LastIsBest = false;
                if (bestDelta is not 0)
                    TryLumaMode(ref luma, mode, bestDelta, filterIntraMode: -1, maxTxSize);
            }
        }

        if (_decisions.FilterIntra)
        {
            _yMode = Av1Constants.DcPred;
            if (IsFilterIntraAllowed())
            {
                if (_decisions.ExhaustiveSearch)
                {
                    for (var mode = 0; mode < Av1Constants.IntraFilterModes; mode++)
                    {
                        TryLumaMode(ref luma, Av1Constants.DcPred, angleDelta: 0, mode, maxTxSize);
                    }
                }
                else
                {
                    // The recursive filter whose prediction is the closest to the block
                    var bestFilter = 0;
                    var bestError = long.MaxValue;
                    for (var mode = 0; mode < Av1Constants.IntraFilterModes; mode++)
                    {
                        var error = EstimateLumaMode(contexts, r, c, bw4, bh4, Av1Constants.DcPred, angleDelta: 0, mode);
                        if (error < bestError)
                            (bestError, bestFilter) = (error, mode);
                    }

                    TryLumaMode(ref luma, Av1Constants.DcPred, angleDelta: 0, bestFilter, maxTxSize);
                }
            }
        }

        if (_header.TxMode is Av1Constants.TxModeSelect && !_lossless)
        {
            // The best mode with smaller transform blocks, as long as they cost less: each one is predicted from the
            // samples of the ones before it
            var (mode, angleDelta, filterIntraMode, paletteSize) = (luma.BestMode, luma.BestAngle, luma.BestFilterIntra, luma.BestPaletteSize);
            var maxDepth = Math.Min(_decisions.TransformSizeDepth, Math.Min(MaxCodedTxDepth, (int)Av1Tables.MaxTxDepth[subSize]));
            var txSize = maxTxSize;
            for (var depth = 1; depth <= maxDepth; depth++)
            {
                txSize = Av1Tables.SplitTxSize[txSize];
                TryLumaMode(ref luma, mode, angleDelta, filterIntraMode, txSize, paletteSize);
                if (!luma.LastIsBest)
                    break;
            }
        }

        if (_decisions.TransformTypeCandidates > 1 && !_lossless)
        {
            // The transform types of the transform blocks of the best mode
            _searchesTransformTypes = true;
            TryLumaMode(ref luma, luma.BestMode, luma.BestAngle, luma.BestFilterIntra, luma.BestTxSize, luma.BestPaletteSize);
            _searchesTransformTypes = false;
        }

        _yMode = luma.BestMode;
        _angleDeltaY = luma.BestAngle;
        _useFilterIntra = luma.BestFilterIntra >= 0;
        _filterIntraMode = Math.Max(luma.BestFilterIntra, 0);
        _txSize = luma.BestTxSize;
        _paletteSizeY = luma.BestPaletteSize;
        var hasCoefficients = luma.BestHasCoefficients;
        if (!luma.LastIsBest)
            CopyPixels(pixels, r, c, bw4, bh4, planeMask: 0b001, save: false);

        var bestLength = luma.BestLength;
        var distortion = luma.BestDistortion;
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
            var bestAlphaU = 0;
            var bestAlphaV = 0;
            var bestPaletteSize = 0;
            long bestDistortion = 0;
            bestHasCoefficients = false;
            lastIsBest = false;
            _uvMode = Av1Constants.DcPred;

            // Two more modes: the palette of a block of few colors, and the prediction from the luma samples of the block,
            // which are the ones of its best luma mode
            var chromaPaletteSize = IsPaletteAllowed() ? FindChromaPalette() : 0;
            var paletteIndex = chromaPaletteSize > 0 ? chromaCount : -1;
            var candidateCount = chromaCount + (chromaPaletteSize > 0 ? 1 : 0) + (_decisions.ChromaFromLuma && IsCflAllowed() ? 1 : 0);
            for (var i = 0; i < candidateCount; i++)
            {
                _paletteSizeUv = 0;
                if (i < chromaCount)
                {
                    _uvMode = modes[i];
                    _cflAlphaU = 0;
                    _cflAlphaV = 0;
                }
                else if (i == paletteIndex)
                {
                    _uvMode = Av1Constants.DcPred;
                    _cflAlphaU = 0;
                    _cflAlphaV = 0;
                    _paletteSizeUv = chromaPaletteSize;
                }
                else if (!SearchCflAlphas(contexts, r, c, bw4, bh4))
                {
                    // The search of the alphas predicted the chroma samples of the block: the frame does not hold the
                    // best mode any more
                    lastIsBest = false;
                    break;
                }

                CopyContexts(contexts, r, c, bw4, bh4, save: false);
                var candidateStart = chromaStart + chromaLength;
                _recordPosition = candidateStart;
                _rate = 0;
                _distortion = 0;
                _hasCoefficients = false;
                WriteUvModeInfo();
                if (IsPaletteAllowed())
                {
                    WritePaletteModeInfoUv();
                    WritePaletteTokens(ChromaPalette);
                }

                Residual(ResidualOperation.Search, planeMask: 0b010);
                var candidateULength = _recordPosition - candidateStart;
                Residual(ResidualOperation.Search, planeMask: 0b100);
                var cost = _decisions.GetCost(_distortion, _rate);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestUvMode = _uvMode;
                    bestAlphaU = _cflAlphaU;
                    bestAlphaV = _cflAlphaV;
                    bestPaletteSize = _paletteSizeUv;
                    bestDistortion = _distortion;
                    bestHasCoefficients = _hasCoefficients;
                    uLength = candidateULength;
                    chromaLength = _recordPosition - candidateStart;
                    if (candidateStart != chromaStart)
                        workspace.Records.Slice(candidateStart, chromaLength).CopyTo(workspace.Records[chromaStart..]);

                    lastIsBest = true;
                    if (i < candidateCount - 1)
                        CopyPixels(pixels, r, c, bw4, bh4, planeMask: 0b110, save: true);
                }
                else
                {
                    lastIsBest = false;
                }
            }

            _uvMode = bestUvMode;
            _cflAlphaU = bestAlphaU;
            _cflAlphaV = bestAlphaV;
            _paletteSizeUv = bestPaletteSize;
            distortion += bestDistortion;
            hasCoefficients |= bestHasCoefficients;
            if (!lastIsBest)
                CopyPixels(pixels, r, c, bw4, bh4, planeMask: 0b110, save: false);
        }

        // A block without coefficient is a skipped block, which codes no transform block
        _skip = !hasCoefficients;
        var paletteStart = chromaStart + chromaLength;
        var end = paletteStart + RecordPalettes(paletteStart);
        var record = workspace.Records.Slice(start, RecordHeaderLength);
        record[RecordLength] = end - start;
        record[RecordPaletteSizeY] = _paletteSizeY;
        record[RecordPaletteSizeUv] = _paletteSizeUv;
        record[RecordPaletteOffset] = paletteStart - start;
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
        record[RecordRefMvIdx] = 0;
        record[RecordMvRow] = 0;
        record[RecordMvCol] = 0;

        // The cost of the block, and the state that a decoder has after it
        CopyContexts(contexts, r, c, bw4, bh4, save: false);
        _recordPosition = start;
        var sink = _sink;
        _sink = SymbolSink.Cost;
        _rate = 0;
        ReplayBlock(r, c, subSize);
        _sink = sink;
        Debug.Assert(_recordPosition == end);

        // The prediction of a block without coefficient is the block, exactly for a lossless frame: splitting it is
        // only tried by the exhaustive search of a lossy frame
        _isPredictedExactly = _skip && (_lossless || !_decisions.ExhaustiveSearch);
        return _decisions.GetCost(distortion, _rate);
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
        public int BestTxSize;
        public int BestPaletteSize;
        public int BestLength;
        public long BestDistortion;
        public bool BestHasCoefficients;
        public bool LastIsBest;
    }

    // Encodes the luma transform blocks of the block with a mode, and keeps their records and their samples when the mode
    // is the best one so far
    private void TryLumaMode(ref LumaSearch search, int mode, int angleDelta, int filterIntraMode, int txSize, int paletteSize = 0)
    {
        var workspace = _workspace!;
        CopyContexts(workspace.GetContextSnapshot(search.Level, block: true), search.R, search.C, search.Bw4, search.Bh4, save: false);
        var candidateStart = search.Start + search.BestLength;
        _recordPosition = candidateStart;
        _yMode = mode;
        _angleDeltaY = angleDelta;
        _useFilterIntra = filterIntraMode >= 0;
        _filterIntraMode = Math.Max(filterIntraMode, 0);
        _txSize = txSize;
        _paletteSizeY = paletteSize;
        _rate = 0;
        _distortion = 0;
        _hasCoefficients = false;
        WriteYMode();
        WriteIntraAngleInfoY();
        if (IsPaletteAllowed())
        {
            WritePaletteModeInfoY();
            WritePaletteTokens(LumaPalette);
        }

        WriteFilterIntraModeInfo();
        WriteBlockTxSize();
        Residual(ResidualOperation.Search, planeMask: 0b001);
        var cost = _decisions.GetCost(_distortion, _rate);
        if (cost >= search.BestCost)
        {
            search.LastIsBest = false;
            return;
        }

        search.BestCost = cost;
        search.BestMode = mode;
        search.BestAngle = angleDelta;
        search.BestDistortion = _distortion;
        search.BestFilterIntra = filterIntraMode;
        search.BestTxSize = txSize;
        search.BestPaletteSize = paletteSize;
        search.BestHasCoefficients = _hasCoefficients;
        search.BestLength = _recordPosition - candidateStart;
        if (candidateStart != search.Start)
            workspace.Records.Slice(candidateStart, search.BestLength).CopyTo(workspace.Records[search.Start..]);

        search.LastIsBest = true;
        CopyPixels(workspace.GetPixelSnapshot(search.Level, block: true), search.R, search.C, search.Bw4, search.Bh4, planeMask: 0b001, save: true);
    }

    // The search of the alphas of the chroma from luma prediction (UV_CFL_PRED): for each chroma plane, the alpha whose
    // prediction is the closest to the samples of the block, found by halving a step from 0. Returns whether an alpha is
    // not zero: the prediction is DC_PRED otherwise
    private bool SearchCflAlphas(Span<byte> contexts, int r, int c, int bw4, int bh4)
    {
        const int MaxAlpha = 16;
        _uvMode = Av1Constants.UvCflPred;
        _cflAlphaU = 0;
        _cflAlphaV = 0;
        _estimating = true;
        for (var plane = 1; plane <= 2; plane++)
        {
            var best = 0;
            var bestError = Estimate(contexts, r, c, bw4, bh4, plane, alpha: 0);
            for (var step = MaxAlpha / 2; step >= 1; step >>= 1)
            {
                var center = best;
                for (var sign = -1; sign <= 1; sign += 2)
                {
                    var alpha = center + (sign * step);
                    if (Math.Abs(alpha) > MaxAlpha)
                        continue;

                    var error = Estimate(contexts, r, c, bw4, bh4, plane, alpha);
                    if (error < bestError)
                        (bestError, best) = (error, alpha);
                }
            }

            if (plane is 1)
            {
                _cflAlphaU = best;
            }
            else
            {
                _cflAlphaV = best;
            }
        }

        _estimating = false;
        return _cflAlphaU is not 0 || _cflAlphaV is not 0;
    }

    // The error of the prediction of a chroma plane from the luma samples with an alpha
    private long Estimate(Span<byte> contexts, int r, int c, int bw4, int bh4, int plane, int alpha)
    {
        CopyContexts(contexts, r, c, bw4, bh4, save: false);
        if (plane is 1)
        {
            _cflAlphaU = alpha;
        }
        else
        {
            _cflAlphaV = alpha;
        }

        _estimate = 0;
        Residual(ResidualOperation.Search, planeMask: 1 << plane);
        return _estimate;
    }

    // The error of the prediction of the luma samples of the block with a mode: see RankIntraModes
    private long EstimateLumaMode(Span<byte> contexts, int r, int c, int bw4, int bh4, int mode, int angleDelta, int filterIntraMode)
    {
        CopyContexts(contexts, r, c, bw4, bh4, save: false);
        _yMode = mode;
        _angleDeltaY = angleDelta;
        _useFilterIntra = filterIntraMode >= 0;
        _filterIntraMode = Math.Max(filterIntraMode, 0);
        _paletteSizeY = 0;
        _estimating = true;
        _estimate = 0;
        Residual(ResidualOperation.Search, planeMask: 0b001);
        _estimating = false;
        return _estimate;
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
        var flags = record[RecordFlags];
        if ((flags & RecordInterFlag) is not 0)
        {
            SetInterBlockDefaults();
            _refMvIdx = record[RecordRefMvIdx];
            _mv[0] = record[RecordMvRow];
            _mv[1] = record[RecordMvCol];
        }
        else
        {
            SetIntraBlockDefaults();
        }

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
        _paletteSizeY = record[RecordPaletteSizeY];
        _paletteSizeUv = record[RecordPaletteSizeUv];
        if (_paletteSizeY is not 0 || _paletteSizeUv is not 0)
            LoadPalettes(start + record[RecordPaletteOffset]);

        _transformBlockCursor[0] = start + record[RecordLumaOffset];
        _transformBlockCursor[1] = start + record[RecordUOffset];
        _transformBlockCursor[2] = start + record[RecordVOffset];
        var length = record[RecordLength];

        var bw4 = Av1Tables.Num4x4BlocksWide[subSize];
        var bh4 = Av1Tables.Num4x4BlocksHigh[subSize];
        WriteModeInfo();
        WritePaletteTokens(LumaPalette | ChromaPalette);
        var rate = _rate;
        WriteBlockTxSize();
        _transformSizeRate = _rate - rate;
        if (_skip)
            ResetBlockContext(bw4, bh4);

        StoreBlock(bw4, bh4);
        Residual(ResidualOperation.Replay, planeMask: 0b111);
        _recordPosition = start + length;
    }

    // The motion search of the block: the motion vector, in eighths of a sample, whose prediction from LAST_FRAME is the
    // closest to the luma samples of the block, with a penalty for the bits of its difference with the first motion vector
    // of the stack. The search starts from the best motion vector of the stack and of the modes that are already encoded
    private void SearchMotion(int bw4, int bh4, scoped ReadOnlySpan<int> stack, scoped ReadOnlySpan<int> seeds, out int mvRow, out int mvCol)
    {
        var workspace = _workspace!;
        var sourcePlane = workspace.GetSource(0);
        var referenceFrame = _references.Get(Av1Constants.LastFrame);
        var reference = referenceFrame.GetPlane(0);
        var x0 = _miCol * Av1Constants.MiSize;
        var y0 = _miRow * Av1Constants.MiSize;
        var motion = new MotionSearch
        {
            Source = sourcePlane.Samples,
            SourceStride = sourcePlane.Stride,
            Reference = reference.Samples,
            ReferenceStride = reference.Stride,
            LastX = referenceFrame.Header.UpscaledWidth - 1,
            LastY = referenceFrame.Header.FrameHeight - 1,
            X = x0,
            Y = y0,

            // The samples of the block that are part of the frame: none for a block that only holds the samples that
            // round the size of the frame up to 8
            Width = Math.Max(0, Math.Min(bw4 * Av1Constants.MiSize, sourcePlane.Width - x0)),
            Height = Math.Max(0, Math.Min(bh4 * Av1Constants.MiSize, sourcePlane.Height - y0)),
            PredictedRow = stack[0],
            PredictedCol = stack[1],
            Lambda = _decisions.MotionLambda,
        };

        // Whole samples first. A prediction that is entirely beyond an edge of the reference frame repeats the edge
        var minX = Math.Max(-MaxMotionSamples, -(x0 + motion.Width + MotionMargin));
        var maxX = Math.Min(MaxMotionSamples, motion.LastX + MotionMargin - x0);
        var minY = Math.Max(-MaxMotionSamples, -(y0 + motion.Height + MotionMargin));
        var maxY = Math.Min(MaxMotionSamples, motion.LastY + MotionMargin - y0);
        var bestX = 0;
        var bestY = 0;
        var best = motion.GetCost(0, 0, MaxCost);
        for (var i = 0; i < seeds.Length; i += 2)
        {
            var seedY = Math.Clamp(RoundToSample(seeds[i]), minY, maxY);
            var seedX = Math.Clamp(RoundToSample(seeds[i + 1]), minX, maxX);
            var cost = motion.GetCost(seedX, seedY, best);
            if (cost < best)
                (best, bestX, bestY) = (cost, seedX, seedY);
        }

        ReadOnlySpan<sbyte> pattern = [0, -1, -1, 0, 1, 0, 0, 1, -1, -1, 1, -1, -1, 1, 1, 1];
        for (var step = _decisions.MotionStep; step >= 1; step >>= 1)
        {
            for (var iteration = 0; iteration < MaxMotionIterations; iteration++)
            {
                var (centerX, centerY) = (bestX, bestY);
                for (var i = 0; i < pattern.Length; i += 2)
                {
                    var x = centerX + (pattern[i] * step);
                    var y = centerY + (pattern[i + 1] * step);
                    if (x < minX || x > maxX || y < minY || y > maxY)
                        continue;

                    var cost = motion.GetCost(x, y, best);
                    if (cost < best)
                        (best, bestX, bestY) = (cost, x, y);
                }

                if (bestX == centerX && bestY == centerY)
                    break;
            }
        }

        mvRow = bestY * 8;
        mvCol = bestX * 8;
        if (!_decisions.SubpelMotion || _lossless)
            return;

        // Then half samples and quarter samples, with the prediction process of the decoder
        var candIndex = MiIndex(_miRow, _miCol);
        var planeBuffer = _frame.GetPlane(0);
        _refFrames[candIndex * 2] = Av1Constants.LastFrame;
        _refFrames[(candIndex * 2) + 1] = NoReference;
        _interInfo[candIndex] = (byte)(_interpFilter0 | (_interpFilter1 << 2));
        _yMode = Av1Constants.Newmv;
        var bw = bw4 * Av1Constants.MiSize;
        var bh = bh4 * Av1Constants.MiSize;
        for (var step = 4; step >= 2; step >>= 1)
        {
            var (centerRow, centerCol) = (mvRow, mvCol);
            for (var i = 0; i < pattern.Length; i += 2)
            {
                var row = centerRow + (pattern[i + 1] * step);
                var col = centerCol + (pattern[i] * step);
                _mvs[candIndex * 4] = (short)row;
                _mvs[(candIndex * 4) + 1] = (short)col;
                PredictInter(plane: 0, x0, y0, bw, bh, _miRow, _miCol);
                var cost = (MotionSearch.GetDifference(motion.Source, motion.SourceStride, planeBuffer.Samples, planeBuffer.Stride, x0, y0, motion.Width, motion.Height) << 8) + motion.GetRate(row, col);
                if (cost < best)
                    (best, mvRow, mvCol) = (cost, row, col);
            }
        }

        // The block is not written yet
        _refFrames[candIndex * 2] = Av1FrameState.RefFrameNotWritten;

        static int RoundToSample(int value) => value >= 0 ? (value + 4) >> 3 : -((-value + 4) >> 3);
    }

    // The state of the motion search of a block: the luma samples to predict and the reference plane
    [StructLayout(LayoutKind.Auto)]
    private ref struct MotionSearch
    {
        public ReadOnlySpan<ushort> Source;
        public int SourceStride;
        public ReadOnlySpan<ushort> Reference;
        public int ReferenceStride;
        public int LastX;
        public int LastY;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public int PredictedRow;
        public int PredictedCol;
        public long Lambda;

        // The cost of a motion vector of whole samples: the sum of the absolute differences of the samples, with 8
        // fractional bits, plus the cost of its bits; a value that is not less than limit when the cost reaches limit
        public readonly long GetCost(int mvX, int mvY, long limit)
        {
            var cost = GetRate(mvY * 8, mvX * 8);
            if (cost >= limit)
                return cost;

            var refX = X + mvX;
            var refY = Y + mvY;
            long difference = 0;
            var inside = refX >= 0 && refY >= 0 && refX + Width - 1 <= LastX && refY + Height - 1 <= LastY;
            for (var i = 0; i < Height; i++)
            {
                var original = Source.Slice(((Y + i) * SourceStride) + X, Width);
                if (inside)
                {
                    var predicted = Reference.Slice(((refY + i) * ReferenceStride) + refX, Width);
                    for (var j = 0; j < original.Length; j++)
                    {
                        difference += Math.Abs(original[j] - predicted[j]);
                    }
                }
                else
                {
                    // The samples beyond the edges of the reference frame are the ones of its edges
                    var row = Reference.Slice(Math.Clamp(refY + i, 0, LastY) * ReferenceStride, LastX + 1);
                    for (var j = 0; j < original.Length; j++)
                    {
                        difference += Math.Abs(original[j] - row[Math.Clamp(refX + j, 0, LastX)]);
                    }
                }

                if (cost + (difference << 8) >= limit)
                    break;
            }

            return cost + (difference << 8);
        }

        // An estimate of the cost of the bits of a motion vector: two bits for each doubling of its difference with the
        // predicted motion vector
        public readonly long GetRate(int mvRow, int mvCol)
        {
            return Lambda * (GetBits(mvRow - PredictedRow) + GetBits(mvCol - PredictedCol));

            static int GetBits(int difference) => difference is 0 ? 1 : (2 * BitOperations.Log2((uint)Math.Abs(difference))) + 3;
        }

        public static long GetDifference(ReadOnlySpan<ushort> source, int sourceStride, ReadOnlySpan<ushort> predicted, int predictedStride, int x, int y, int width, int height)
        {
            long difference = 0;
            for (var i = 0; i < height; i++)
            {
                var original = source.Slice(((y + i) * sourceStride) + x, width);
                var prediction = predicted.Slice(((y + i) * predictedStride) + x, width);
                for (var j = 0; j < original.Length; j++)
                {
                    difference += Math.Abs(original[j] - prediction[j]);
                }
            }

            return difference;
        }
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

        // The transform type of a luma block is searched; the type of a chroma block follows from its mode, or from the
        // type of the luma block for an inter block
        var count = txSz is Av1Constants.Tx16x64 or Av1Constants.Tx64x16 ? 512 : Math.Min(1024, w * h);
        var coefficients = workspace.Coefficients[..count];
        var lumaTxType = plane is 0 ? SearchTransformType(residual, coefficients, txSz, startX, startY) : Av1Constants.DctDct;
        var planeTxType = plane is 0 ? GetLumaPlaneTxType(txSz, lumaTxType) : ComputeTxType(plane, txSz, startX >> 2, startY >> 2);
        if (_lossless)
        {
            Av1ForwardTransform.ForwardWalshHadamard4x4(residual, coefficients);
        }
        else
        {
            GetQuantizers(plane, out var dcQuantizer, out var acQuantizer);
            Av1ForwardTransform.Forward(residual, coefficients, txSz, planeTxType);
            Av1Quantizer.Quantize(coefficients, coefficients, dcQuantizer, acQuantizer, Av1Reconstruction.GetDequantizationDenominator(txSz), _bitDepth, _decisions.QuantizerRounding);
        }

        // The record: the levels in scan order, up to the last one that is not zero
        var scan = GetScan(txSz, planeTxType);
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

        record[0] = lumaTxType;
        record[1] = eob;
        _recordPosition += 2 + eob;
        WriteCoeffs(plane, startX, startY, txSz, lumaTxType, record.Slice(2, eob), eob);
        if (eob > 0)
        {
            _hasCoefficients = true;
            Reconstruct(samples, stride, plane, startX, startY, txSz);
        }

        if (!_lossless)
        {
            // The squared error of the samples of the transform block that are part of the frame
            var visibleWidth = Math.Max(0, Math.Min(w, planeBuffer.Width - startX));
            var visibleHeight = Math.Max(0, Math.Min(h, planeBuffer.Height - startY));
            long error = 0;
            for (var i = 0; i < visibleHeight; i++)
            {
                var reconstructed = samples.Slice(((startY + i) * stride) + startX, visibleWidth);
                var original = source.Slice(((startY + i) * sourceStride) + startX, visibleWidth);
                for (var j = 0; j < visibleWidth; j++)
                {
                    var difference = original[j] - reconstructed[j];
                    error += difference * difference;
                }
            }

            _distortion += error;
        }

        FinishTransformBlock(plane, txSz, startX, startY);
    }

    // The transform types in the order they are tried: the search of a block tries the first ones that its transform set
    // holds
    private static ReadOnlySpan<byte> TransformTypeOrder =>
    [
        Av1Constants.DctDct, Av1Constants.AdstAdst, Av1Constants.AdstDct, Av1Constants.DctAdst, Av1Constants.Idtx, Av1Constants.VDct, Av1Constants.HDct,
        Av1Constants.FlipadstDct, Av1Constants.DctFlipadst, Av1Constants.FlipadstFlipadst, Av1Constants.AdstFlipadst, Av1Constants.FlipadstAdst,
        Av1Constants.VAdst, Av1Constants.HAdst, Av1Constants.VFlipadst, Av1Constants.HFlipadst,
    ];

    // The search of the transform type of a luma transform block (the value of intra_tx_type or inter_tx_type): each type
    // is costed with its coefficients and with the error of their quantization, which is measured on the coefficients
    // rather than on the samples that they are transformed back to. The contexts are left as they are found. The modes of
    // a block are compared with a DCT in both directions: the types are only searched for the mode that is kept
    private int SearchTransformType(scoped ReadOnlySpan<int> residual, scoped Span<int> coefficients, int txSz, int startX, int startY)
    {
        var set = GetTxSet(txSz);
        if (!_searchesTransformTypes || _lossless || set is Av1Constants.TxSetDctonly)
            return Av1Constants.DctDct;

        var workspace = _workspace!;
        var x4 = startX >> 2;
        var y4 = startY >> 2;
        var w4 = Av1Tables.TxWidth[txSz] >> 2;
        var h4 = Av1Tables.TxHeight[txSz] >> 2;
        Span<byte> contexts = stackalloc byte[4 * 16];
        CopyTransformBlockContexts(contexts, plane: 0, x4, y4, w4, h4, save: true);
        GetQuantizers(plane: 0, out var dcQuantizer, out var acQuantizer);
        var denominator = Av1Reconstruction.GetDequantizationDenominator(txSz);
        var gain = Av1ForwardTransform.GetSquaredGain(txSz);
        var inSet = _isInter ? Av1Tables.TxTypeInSetInter : Av1Tables.TxTypeInSetIntra;
        var count = coefficients.Length;

        // The levels of a candidate, in the order of the coefficients then in scan order, after the records of the block
        workspace.EnsureRecords(_recordPosition, 2 * count);
        var rate = _rate;
        var best = Av1Constants.DctDct;
        var bestCost = MaxCost;
        var tried = 0;
        foreach (var txType in TransformTypeOrder)
        {
            if (inSet[(set * Av1Constants.TxTypes) + txType] is 0)
                continue;

            if (tried > 0)
                CopyTransformBlockContexts(contexts, plane: 0, x4, y4, w4, h4, save: false);

            var levels = workspace.Records.Slice(_recordPosition, count);
            var ordered = workspace.Records.Slice(_recordPosition + count, count);
            Av1ForwardTransform.Forward(residual, coefficients, txSz, txType);
            Av1Quantizer.Quantize(coefficients, levels, dcQuantizer, acQuantizer, denominator, _bitDepth, _decisions.QuantizerRounding);
            long error = 0;
            for (var i = 0; i < count; i++)
            {
                long difference = coefficients[i] - Av1Quantizer.Dequantize(levels[i], i is 0 ? dcQuantizer : acQuantizer, denominator, _bitDepth);
                error += difference * difference;
            }

            var scan = GetScan(txSz, txType);
            var eob = 0;
            for (var i = 0; i < count; i++)
            {
                var level = levels[scan[i]];
                ordered[i] = level;
                if (level is not 0)
                    eob = i + 1;
            }

            _rate = 0;
            WriteCoeffs(plane: 0, startX, startY, txSz, txType, ordered[..eob], eob);
            var cost = _decisions.GetCost((error << 8) / gain, _rate);
            if (cost < bestCost)
            {
                bestCost = cost;
                best = txType;
            }

            // A block whose coefficients are all quantized to zero by the first type has nothing to gain from another one
            if (eob is 0 || ++tried == _decisions.TransformTypeCandidates)
                break;
        }

        CopyTransformBlockContexts(contexts, plane: 0, x4, y4, w4, h4, save: false);
        _rate = rate;
        return best;
    }

    // Saves or restores the level and DC contexts above and to the left of a transform block: 4 lines of 16 contexts at most
    private readonly void CopyTransformBlockContexts(Span<byte> snapshot, int plane, int x4, int y4, int w4, int h4, bool save)
    {
        var aboveLevel = _state.GetAboveLevelContext(plane).Slice(x4, w4);
        var aboveDc = _state.GetAboveDcContext(plane).Slice(x4, w4);
        var leftLevel = _scratch.GetLeftLevelContext(plane);
        var leftDc = _scratch.GetLeftDcContext(plane);
        if (save)
        {
            aboveLevel.CopyTo(snapshot);
            aboveDc.CopyTo(snapshot[16..]);
        }
        else
        {
            snapshot[..w4].CopyTo(aboveLevel);
            snapshot.Slice(16, w4).CopyTo(aboveDc);
        }

        for (var i = 0; i < h4; i++)
        {
            var index = (y4 + i) & 31;
            if (save)
            {
                snapshot[32 + i] = leftLevel[index];
                snapshot[48 + i] = leftDc[index];
            }
            else
            {
                leftLevel[index] = snapshot[32 + i];
                leftDc[index] = snapshot[48 + i];
            }
        }
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
    // blocks of 4x4 luma samples at (r, c); the chroma blocks that share a 4x4 chroma block with it are included. The
    // reference frames of the blocks of the region are not saved: no block of the region is written when its search starts.
    // A frame that may have palettes also saves the palettes above and to the left of the region
    private readonly void CopyContexts(Span<byte> snapshot, int r, int c, int bw4, int bh4, bool save)
    {
        if (!save && !_refFrames.IsEmpty)
        {
            // A decoder builds the motion vector stack of a block from the blocks that are decoded before it: the blocks of
            // the region are not written any more
            var width = Math.Min(bw4, _miCols - c);
            var height = Math.Min(bh4, _miRows - r);
            for (var y = 0; y < height; y++)
            {
                _refFrames.Slice(MiIndex(r + y, c) * 2, width * 2).Fill(Av1FrameState.RefFrameNotWritten);
            }
        }

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

        if (_header.AllowScreenContentTools)
        {
            // The palettes of the blocks above and to the left, which predict the colors of the palette of a block
            var width = Math.Min(bw4, _miCols - c) * Av1Constants.PaletteColors;
            var height = bh4 * Av1Constants.PaletteColors;
            var abovePlaneLength = Av1Constants.PaletteColors * (_miCols + Av1FrameState.AboveContextMargin);
            for (var plane = 0; plane < 2; plane++)
            {
                CopyColors(_state.AbovePaletteColors.Slice((plane * abovePlaneLength) + (c * Av1Constants.PaletteColors), width), snapshot.Slice(position, width * sizeof(ushort)), save);
                position += width * sizeof(ushort);
                CopyColors(_scratch.GetLeftPaletteColors(plane).Slice((r & 31) * Av1Constants.PaletteColors, height), snapshot.Slice(position, height * sizeof(ushort)), save);
                position += height * sizeof(ushort);
            }
        }

        static void CopyColors(Span<ushort> state, Span<byte> saved, bool save)
        {
            for (var i = 0; i < state.Length; i++)
            {
                if (save)
                {
                    saved[2 * i] = (byte)state[i];
                    saved[(2 * i) + 1] = (byte)(state[i] >> 8);
                }
                else
                {
                    state[i] = (ushort)(saved[2 * i] | (saved[(2 * i) + 1] << 8));
                }
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
