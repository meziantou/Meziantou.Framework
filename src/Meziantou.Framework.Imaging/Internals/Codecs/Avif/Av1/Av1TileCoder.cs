using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes the blocks of one tile of an AV1 frame (AV1 specification, sections 5.11.2 to 5.11.58): reads the syntax elements
/// of each block with the symbol decoder, predicts it and reconstructs its residual into the planes of the frame. The methods
/// follow the syntax tables of the specification and keep their names; the variables of the specification that describe the
/// block being decoded are the fields of this type.
/// </summary>
internal ref partial struct Av1TileCoder
{
    private readonly Av1SequenceHeader _sequence;
    private readonly Av1FrameHeader _header;
    private readonly Av1FrameState _state;
    private readonly Av1FrameBuffer _frame;
    private readonly Av1SymbolDecoder _symbols;
    private readonly Av1TileScratch _scratch;
    private readonly Span<ushort> _cdf;
    private readonly Av1ReferenceFrames _references;
    private readonly Av1InterScratch? _interScratch;
    private readonly Span<short> _motionFieldMvs;
    private readonly CancellationToken _cancellationToken;
    private readonly int _miCols;
    private readonly int _miRows;
    private readonly int _subX;
    private readonly int _subY;
    private readonly int _planeCount;
    private readonly int _bitDepth;
    private readonly int _sbMask;

    // The frame arrays that are indexed by the position of a 4x4 luma block
    private readonly Span<byte> _miSizes;
    private readonly Span<byte> _yModes;
    private readonly Span<byte> _uvModes;
    private readonly Span<byte> _flags;
    private readonly Span<byte> _paletteSizes;
    private readonly Span<byte> _segmentIds;
    private readonly Span<byte> _interTxSizes;
    private readonly Span<sbyte> _cdefIdx;
    private readonly Span<sbyte> _refFrames;
    private readonly Span<short> _mvs;
    private readonly Span<byte> _interInfo;
    private readonly ReadOnlySpan<byte> _prevSegmentIds;

    // The tile being decoded
    private int _miRowStart;
    private int _miRowEnd;
    private int _miColStart;
    private int _miColEnd;
    private int _currentQIndex;
    private bool _readDeltas;
    private InlineArray4<int> _deltaLf;
    private InlineArray3<InlineArray2<int>> _refSgrXqd;
    private InlineArray3<InlineArray2<InlineArray3<int>>> _refLrWiener;

    // The block being decoded
    private int _miRow;
    private int _miCol;
    private int _miSize;
    private bool _hasChroma;
    private bool _availU;
    private bool _availL;
    private bool _availUChroma;
    private bool _availLChroma;
    private bool _skip;
    private bool _skipMode;
    private bool _isInter;
    private bool _useIntrabc;
    private int _segmentId;
    private bool _lossless;
    private int _yMode;
    private int _uvMode;
    private int _angleDeltaY;
    private int _angleDeltaUv;
    private bool _useFilterIntra;
    private int _filterIntraMode;
    private int _cflAlphaU;
    private int _cflAlphaV;
    private int _paletteSizeY;
    private int _paletteSizeUv;
    private int _txSize;
    private int _maxLumaW;
    private int _maxLumaH;
    private int _planeTxType;
    private int _qIndex;

    // The block being decoded: RefFrame, Mv, PredMv and the syntax elements of an inter block. A motion vector is stored as
    // its row then its column, the vector of the second reference list after the one of the first
    private int _refFrame0;
    private int _refFrame1;
    private InlineArray4<int> _mv;
    private InlineArray4<int> _predMv;
    private int _leftRefFrame0;
    private int _leftRefFrame1;
    private int _aboveRefFrame0;
    private int _aboveRefFrame1;
    private bool _leftIntra;
    private bool _aboveIntra;
    private bool _leftSingle;
    private bool _aboveSingle;
    private int _refMvIdx;
    private int _motionMode;
    private bool _interintra;
    private int _interintraMode;
    private bool _wedgeInterintra;
    private int _wedgeIndex;
    private int _wedgeSign;
    private int _maskType;
    private int _compoundType;
    private int _compGroupIdx;
    private int _compoundIdx;
    private int _interpFilter0;
    private int _interpFilter1;

    // The outputs of the find MV stack process. A motion vector is stored as its row then its column, the vector of the
    // second reference list after the one of the first
    private int _numMvFound;
    private int _newMvCount;
    private bool _foundMatch;
    private int _closeMatches;
    private int _totalMatches;
    private int _newMvContext;
    private int _refMvContext;
    private int _zeroMvContext;
    private InlineArray4<int> _globalMvs;
    private InlineArray8<InlineArray4<int>> _refStackMv;
    private InlineArray8<int> _weightStack;
    private InlineArray8<int> _drlCtxStack;

    // The outputs of the find warp samples process
    private int _numSamples;
    private int _numSamplesScanned;
    private InlineArray8<InlineArray4<int>> _candList;

    // The warp model of the block being decoded (LocalValid, LocalWarpParams and the shears derived from them)
    private bool _localValid;
    private InlineArray6<int> _localWarpParams;
    private InlineArray4<int> _localShear;

    public Av1TileCoder(Av1FrameBuffer frame, Av1FrameState state, Av1SymbolDecoder symbols, Av1TileScratch scratch, Av1ReferenceFrames references, Av1InterScratch? interScratch, Span<ushort> cdf, CancellationToken cancellationToken)
    {
        _frame = frame;
        _references = references;
        _interScratch = interScratch;
        _sequence = frame.Sequence;
        _header = frame.Header;
        _state = state;
        _symbols = symbols;
        _scratch = scratch;
        _cdf = cdf;
        _cancellationToken = cancellationToken;
        _miCols = _header.MiCols;
        _miRows = _header.MiRows;
        _subX = _sequence.SubsamplingX;
        _subY = _sequence.SubsamplingY;
        _planeCount = _sequence.PlaneCount;
        _bitDepth = _sequence.BitDepth;
        _sbMask = _sequence.Use128x128Superblock ? 31 : 15;
        _miSizes = state.MiSizes;
        _yModes = state.YModes;
        _uvModes = state.UvModes;
        _flags = state.Flags;
        _paletteSizes = state.PaletteSizes;
        _segmentIds = state.SegmentIds;
        _interTxSizes = state.InterTxSizes;
        _cdefIdx = state.CdefIdx;
        if (!_header.FrameIsIntra || _header.AllowIntrabc)
        {
            _refFrames = state.RefFrames;
            _mvs = state.Mvs;
            _interInfo = state.InterInfo;
        }

        _prevSegmentIds = state.PrevSegmentIds;
        _motionFieldMvs = _header.UseRefFrameMvs ? state.MotionFieldMvs : default;
    }

    /// <summary>
    /// Decodes the tile whose data the symbol decoder was initialized with (<c>decode_tile</c>, section 5.11.2).
    /// </summary>
    /// <param name="tileRow">The row of the tile.</param>
    /// <param name="tileCol">The column of the tile.</param>
    public void DecodeTile(int tileRow, int tileCol)
    {
        _miRowStart = _header.MiRowStarts[tileRow];
        _miRowEnd = _header.MiRowStarts[tileRow + 1];
        _miColStart = _header.MiColStarts[tileCol];
        _miColEnd = _header.MiColStarts[tileCol + 1];
        _currentQIndex = _header.BaseQIdx;

        ClearAboveContext();
        _deltaLf = default;
        for (var plane = 0; plane < _planeCount; plane++)
        {
            for (var pass = 0; pass < 2; pass++)
            {
                _refSgrXqd[plane][pass] = Av1Tables.SgrprojXqdMid[pass];
                for (var i = 0; i < Av1Constants.WienerCoeffs; i++)
                {
                    _refLrWiener[plane][pass][i] = Av1Tables.WienerTapsMid[i];
                }
            }
        }

        var sbSize = _sequence.Use128x128Superblock ? Av1Constants.Block128x128 : Av1Constants.Block64x64;
        var sbSize4 = Av1Tables.Num4x4BlocksWide[sbSize];
        for (var r = _miRowStart; r < _miRowEnd; r += sbSize4)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            ClearLeftContext();
            for (var c = _miColStart; c < _miColEnd; c += sbSize4)
            {
                _readDeltas = _header.DeltaQPresent;
                ClearCdef(r, c);
                ClearBlockDecodedFlags(r, c, sbSize4);
                ReadLr(r, c, sbSize);
                DecodePartition(r, c, sbSize);
            }

            // SymbolMaxBits only decreases: a tile that read past its data cannot recover
            _symbols.EnsureNotOverrun();
        }
    }

    private readonly int MiIndex(int row, int col) => (row * _miCols) + col;

    private readonly bool IsInside(int candidateR, int candidateC) => candidateC >= _miColStart && candidateC < _miColEnd && candidateR >= _miRowStart && candidateR < _miRowEnd;

    private readonly int ReadSymbol(int offset, int size) => _symbols.ReadSymbol(_cdf.Slice(offset, size));

    private readonly int ReadLiteral(int count) => _symbols.ReadLiteral(count);

    private readonly void ClearAboveContext()
    {
        for (var plane = 0; plane < _planeCount; plane++)
        {
            var subX = plane > 0 ? _subX : 0;
            var start = _miColStart >> subX;
            var length = ((_miColEnd - _miColStart) >> subX) + Av1FrameState.AboveContextMargin;
            length = Math.Min(length, _state.GetAboveLevelContext(plane).Length - start);
            _state.GetAboveLevelContext(plane).Slice(start, length).Clear();
            _state.GetAboveDcContext(plane).Slice(start, length).Clear();
        }

        _state.AboveSegPredContext[_miColStart..].Clear();
    }

    private readonly void ClearLeftContext()
    {
        for (var plane = 0; plane < 3; plane++)
        {
            _scratch.GetLeftLevelContext(plane).Clear();
            _scratch.GetLeftDcContext(plane).Clear();
        }

        _scratch.LeftSegPredContext.Clear();
    }

    // Section 5.11.55 "Clear CDEF function"
    private readonly void ClearCdef(int r, int c)
    {
        var size = _sequence.Use128x128Superblock ? 2 : 1;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var row = (r >> 4) + y;
                var col = (c >> 4) + x;
                if (row < ((_miRows + 15) >> 4) && col < _state.CdefCols)
                    _cdefIdx[(row * _state.CdefCols) + col] = -1;
            }
        }
    }

    // Section 5.11.3 "Clear block decoded flags function"
    private readonly void ClearBlockDecodedFlags(int r, int c, int sbSize4)
    {
        for (var plane = 0; plane < _planeCount; plane++)
        {
            var subX = plane > 0 ? _subX : 0;
            var subY = plane > 0 ? _subY : 0;
            var sbWidth4 = (_miColEnd - c) >> subX;
            var sbHeight4 = (_miRowEnd - r) >> subY;
            var flags = _scratch.GetBlockDecoded(plane);
            for (var y = -1; y <= (sbSize4 >> subY); y++)
            {
                for (var x = -1; x <= (sbSize4 >> subX); x++)
                {
                    byte value = 0;
                    if (y < 0 && x < sbWidth4)
                    {
                        value = 1;
                    }
                    else if (x < 0 && y < sbHeight4)
                    {
                        value = 1;
                    }

                    flags[BlockDecodedIndex(y, x)] = value;
                }
            }

            flags[BlockDecodedIndex(sbSize4 >> subY, -1)] = 0;
        }
    }

    private static int BlockDecodedIndex(int y, int x) => ((y + 1) * Av1TileScratch.BlockDecodedStride) + x + 1;

    // Section 5.11.4 "Decode partition syntax"
    private void DecodePartition(int r, int c, int bSize)
    {
        if (r >= _miRows || c >= _miCols)
            return;

        var availU = IsInside(r - 1, c);
        var availL = IsInside(r, c - 1);
        var num4x4 = Av1Tables.Num4x4BlocksWide[bSize];
        var halfBlock4x4 = num4x4 >> 1;
        var quarterBlock4x4 = halfBlock4x4 >> 1;
        var hasRows = (r + halfBlock4x4) < _miRows;
        var hasCols = (c + halfBlock4x4) < _miCols;
        int partition;
        if (bSize < Av1Constants.Block8x8)
        {
            partition = Av1Constants.PartitionNone;
        }
        else if (hasRows && hasCols)
        {
            GetPartitionCdf(r, c, bSize, availU, availL, out var offset, out var size);
            partition = ReadSymbol(offset, size);
        }
        else if (hasCols)
        {
            partition = ReadSplitOr(r, c, bSize, availU, availL, isHorizontal: true) ? Av1Constants.PartitionSplit : Av1Constants.PartitionHorz;
        }
        else if (hasRows)
        {
            partition = ReadSplitOr(r, c, bSize, availU, availL, isHorizontal: false) ? Av1Constants.PartitionSplit : Av1Constants.PartitionVert;
        }
        else
        {
            partition = Av1Constants.PartitionSplit;
        }

        var subSize = Av1Tables.PartitionSubsize[(partition * Av1Constants.BlockSizes) + bSize];
        var splitSize = Av1Tables.PartitionSubsize[(Av1Constants.PartitionSplit * Av1Constants.BlockSizes) + bSize];
        if (subSize is Av1Constants.BlockInvalid || (_planeCount > 1 && GetPlaneResidualSize(subSize, 1) is Av1Constants.BlockInvalid))
            throw Av1Errors.Invalid("a block is partitioned into a size that is not allowed.");

        switch (partition)
        {
            case Av1Constants.PartitionNone:
                DecodeBlock(r, c, subSize);
                break;
            case Av1Constants.PartitionHorz:
                DecodeBlock(r, c, subSize);
                if (hasRows)
                    DecodeBlock(r + halfBlock4x4, c, subSize);

                break;
            case Av1Constants.PartitionVert:
                DecodeBlock(r, c, subSize);
                if (hasCols)
                    DecodeBlock(r, c + halfBlock4x4, subSize);

                break;
            case Av1Constants.PartitionSplit:
                DecodePartition(r, c, subSize);
                DecodePartition(r, c + halfBlock4x4, subSize);
                DecodePartition(r + halfBlock4x4, c, subSize);
                DecodePartition(r + halfBlock4x4, c + halfBlock4x4, subSize);
                break;
            case Av1Constants.PartitionHorzA:
                DecodeBlock(r, c, splitSize);
                DecodeBlock(r, c + halfBlock4x4, splitSize);
                DecodeBlock(r + halfBlock4x4, c, subSize);
                break;
            case Av1Constants.PartitionHorzB:
                DecodeBlock(r, c, subSize);
                DecodeBlock(r + halfBlock4x4, c, splitSize);
                DecodeBlock(r + halfBlock4x4, c + halfBlock4x4, splitSize);
                break;
            case Av1Constants.PartitionVertA:
                DecodeBlock(r, c, splitSize);
                DecodeBlock(r + halfBlock4x4, c, splitSize);
                DecodeBlock(r, c + halfBlock4x4, subSize);
                break;
            case Av1Constants.PartitionVertB:
                DecodeBlock(r, c, subSize);
                DecodeBlock(r, c + halfBlock4x4, splitSize);
                DecodeBlock(r + halfBlock4x4, c + halfBlock4x4, splitSize);
                break;
            case Av1Constants.PartitionHorz4:
                DecodeBlock(r, c, subSize);
                DecodeBlock(r + quarterBlock4x4, c, subSize);
                DecodeBlock(r + (quarterBlock4x4 * 2), c, subSize);
                if (r + (quarterBlock4x4 * 3) < _miRows)
                    DecodeBlock(r + (quarterBlock4x4 * 3), c, subSize);

                break;
            default:
                DecodeBlock(r, c, subSize);
                DecodeBlock(r, c + quarterBlock4x4, subSize);
                DecodeBlock(r, c + (quarterBlock4x4 * 2), subSize);
                if (c + (quarterBlock4x4 * 3) < _miCols)
                    DecodeBlock(r, c + (quarterBlock4x4 * 3), subSize);

                break;
        }
    }

    // The CDF selection of the partition syntax element (section 8.3.2)
    private readonly void GetPartitionCdf(int r, int c, int bSize, bool availU, bool availL, out int offset, out int size)
    {
        var bsl = Av1Tables.MiWidthLog2[bSize];
        var above = availU && Av1Tables.MiWidthLog2[_miSizes[MiIndex(r - 1, c)]] < bsl;
        var left = availL && Av1Tables.MiHeightLog2[_miSizes[MiIndex(r, c - 1)]] < bsl;
        var ctx = ((left ? 1 : 0) * 2) + (above ? 1 : 0);
        (offset, size) = bsl switch
        {
            1 => (Av1CdfLayout.PartitionW8, Av1CdfLayout.PartitionW8Size),
            2 => (Av1CdfLayout.PartitionW16, Av1CdfLayout.PartitionW16Size),
            3 => (Av1CdfLayout.PartitionW32, Av1CdfLayout.PartitionW32Size),
            4 => (Av1CdfLayout.PartitionW64, Av1CdfLayout.PartitionW64Size),
            _ => (Av1CdfLayout.PartitionW128, Av1CdfLayout.PartitionW128Size),
        };

        offset += ctx * size;
    }

    // The split_or_horz and split_or_vert syntax elements: a two-symbol distribution derived from the partition distribution,
    // which is not adapted
    private readonly bool ReadSplitOr(int r, int c, int bSize, bool availU, bool availL, bool isHorizontal)
    {
        GetPartitionCdf(r, c, bSize, availU, availL, out var offset, out var size);
        var partitionCdf = _cdf.Slice(offset, size);
        var psum = 0;
        if (isHorizontal)
        {
            psum += Probability(partitionCdf, Av1Constants.PartitionVert);
            psum += Probability(partitionCdf, Av1Constants.PartitionSplit);
            psum += Probability(partitionCdf, Av1Constants.PartitionHorzA);
            psum += Probability(partitionCdf, Av1Constants.PartitionVertA);
            psum += Probability(partitionCdf, Av1Constants.PartitionVertB);
            if (bSize is not Av1Constants.Block128x128)
                psum += Probability(partitionCdf, Av1Constants.PartitionVert4);
        }
        else
        {
            psum += Probability(partitionCdf, Av1Constants.PartitionHorz);
            psum += Probability(partitionCdf, Av1Constants.PartitionSplit);
            psum += Probability(partitionCdf, Av1Constants.PartitionHorzA);
            psum += Probability(partitionCdf, Av1Constants.PartitionHorzB);
            psum += Probability(partitionCdf, Av1Constants.PartitionVertA);
            if (bSize is not Av1Constants.Block128x128)
                psum += Probability(partitionCdf, Av1Constants.PartitionHorz4);
        }

        Span<ushort> cdf = [(ushort)((1 << 15) - psum), 1 << 15, 0];
        return _symbols.ReadSymbol(cdf) is not 0;

        static int Probability(ReadOnlySpan<ushort> cdf, int symbol) => cdf[symbol] - cdf[symbol - 1];
    }

    private readonly int GetPlaneResidualSize(int subSize, int plane)
    {
        var subX = plane > 0 ? _subX : 0;
        var subY = plane > 0 ? _subY : 0;
        return Av1Tables.SubsampledSize[(((subSize * 2) + subX) * 2) + subY];
    }

    // Section 5.11.5 "Decode block syntax"
    private void DecodeBlock(int r, int c, int subSize)
    {
        _miRow = r;
        _miCol = c;
        _miSize = subSize;
        var bw4 = Av1Tables.Num4x4BlocksWide[subSize];
        var bh4 = Av1Tables.Num4x4BlocksHigh[subSize];
        if (bh4 is 1 && _subY is 1 && (r & 1) is 0)
        {
            _hasChroma = false;
        }
        else if (bw4 is 1 && _subX is 1 && (c & 1) is 0)
        {
            _hasChroma = false;
        }
        else
        {
            _hasChroma = _planeCount > 1;
        }

        _availU = IsInside(r - 1, c);
        _availL = IsInside(r, c - 1);
        _availUChroma = _availU;
        _availLChroma = _availL;
        if (_hasChroma)
        {
            if (_subY is 1 && bh4 is 1)
                _availUChroma = IsInside(r - 2, c);

            if (_subX is 1 && bw4 is 1)
                _availLChroma = IsInside(r, c - 2);
        }
        else
        {
            _availUChroma = false;
            _availLChroma = false;
        }

        ModeInfo();
        PaletteTokens();
        ReadBlockTxSize();
        if (_skip)
            ResetBlockContext(bw4, bh4);

        StoreBlock(bw4, bh4);
        _state.Tools |= GetBlockTools();
        ComputePrediction();
        Residual();
    }

    // The coding tools of the block being decoded that its mode info tells
    private readonly Av1Tools GetBlockTools()
    {
        var tools = Av1Tools.None;
        if (_lossless)
            tools |= Av1Tools.LosslessBlock;

        if (!_isInter)
        {
            if (!_header.FrameIsIntra)
                tools |= Av1Tools.IntraBlockInInterFrame;

            if (_paletteSizeY is not 0 || _paletteSizeUv is not 0)
                tools |= Av1Tools.Palette;

            if (_useFilterIntra)
                tools |= Av1Tools.FilterIntra;

            if (_hasChroma && _uvMode is Av1Constants.UvCflPred)
                tools |= Av1Tools.ChromaFromLuma;

            if (IsDirectionalMode(_yMode))
                tools |= Av1Tools.DirectionalIntra;

            return tools;
        }

        if (_useIntrabc)
            return tools | Av1Tools.IntraBlockCopy;

        tools |= Av1Tools.InterBlock;
        if (_skipMode)
            tools |= Av1Tools.SkipMode;

        if (_refFrame1 > Av1Constants.IntraFrame)
        {
            tools |= Av1Tools.CompoundReference;
            if ((_refFrame0 >= Av1Constants.BwdrefFrame) == (_refFrame1 >= Av1Constants.BwdrefFrame))
                tools |= Av1Tools.UnidirectionalCompound;

            tools |= _compoundType switch
            {
                Av1Constants.CompoundWedge => Av1Tools.WedgeCompound,
                Av1Constants.CompoundDiffwtd => Av1Tools.DifferenceWeightedCompound,
                Av1Constants.CompoundDistance => Av1Tools.DistanceWeightedCompound,
                _ => Av1Tools.None,
            };
        }

        if (_interintra)
            tools |= _wedgeInterintra ? Av1Tools.InterIntra | Av1Tools.WedgeInterIntra : Av1Tools.InterIntra;

        if (_yMode is Av1Constants.Globalmv or Av1Constants.GlobalGlobalmv)
            tools |= Av1Tools.GlobalMotionVector;

        if (HasNewmv(_yMode))
            tools |= Av1Tools.NewMotionVector;

        if (_motionMode is Av1Constants.Obmc)
            tools |= Av1Tools.OverlappedMotionCompensation;

        if (_interpFilter0 != _interpFilter1)
            tools |= Av1Tools.DualInterpolationFilter;

        if (_interpFilter0 is Av1Constants.Bilinear)
            tools |= Av1Tools.BilinearInterpolationFilter;

        if (_references.IsScaled(_refFrame0))
            tools |= Av1Tools.ScaledReference;

        if (IsSegmentationFeatureActive(Av1Constants.SegLvlRefFrame))
            tools |= Av1Tools.SegmentReferenceFrame;

        return tools;
    }

    private readonly void ResetBlockContext(int bw4, int bh4)
    {
        for (var plane = 0; plane < 1 + (_hasChroma ? 2 : 0); plane++)
        {
            var subX = plane > 0 ? _subX : 0;
            var subY = plane > 0 ? _subY : 0;
            var start = _miCol >> subX;
            var length = ((_miCol + bw4) >> subX) - start;
            _state.GetAboveLevelContext(plane).Slice(start, length).Clear();
            _state.GetAboveDcContext(plane).Slice(start, length).Clear();
            for (var i = _miRow >> subY; i < ((_miRow + bh4) >> subY); i++)
            {
                _scratch.GetLeftLevelContext(plane)[i & 31] = 0;
                _scratch.GetLeftDcContext(plane)[i & 31] = 0;
            }
        }
    }

    // The assignments of decode_block to the arrays indexed by the position of a 4x4 block: none of them is read by the
    // residual of the block itself, so they are all made before it
    private readonly void StoreBlock(int bw4, int bh4)
    {
        var width = Math.Min(bw4, _miCols - _miCol);
        var height = Math.Min(bh4, _miRows - _miRow);
        var flags = (byte)((_skip ? Av1FrameState.SkipFlag : 0) | (_isInter ? Av1FrameState.IsInterFlag : 0) | (_skipMode ? Av1FrameState.SkipModeFlag : 0));
        var paletteSizes = (byte)(_paletteSizeY | (_paletteSizeUv << 4));
        for (var y = 0; y < height; y++)
        {
            var start = MiIndex(_miRow + y, _miCol);
            _yModes.Slice(start, width).Fill((byte)_yMode);
            if (!_isInter && _hasChroma)
                _uvModes.Slice(start, width).Fill((byte)_uvMode);

            _flags.Slice(start, width).Fill(flags);
            _miSizes.Slice(start, width).Fill((byte)_miSize);
            _segmentIds.Slice(start, width).Fill((byte)_segmentId);
            _paletteSizes.Slice(start, width).Fill(paletteSizes);
        }

        if (!_refFrames.IsEmpty)
        {
            var interInfo = (byte)(_interpFilter0 | (_interpFilter1 << 2) | (_compGroupIdx << Av1FrameState.CompGroupIdxShift) | (_compoundIdx << Av1FrameState.CompoundIdxShift));
            for (var y = 0; y < height; y++)
            {
                var start = MiIndex(_miRow + y, _miCol);
                _interInfo.Slice(start, width).Fill(interInfo);
                for (var x = 0; x < width; x++)
                {
                    _refFrames[(start + x) * 2] = (sbyte)_refFrame0;
                    _refFrames[((start + x) * 2) + 1] = (sbyte)_refFrame1;
                    _mvs[(start + x) * 4] = (short)_mv[0];
                    _mvs[((start + x) * 4) + 1] = (short)_mv[1];
                    _mvs[((start + x) * 4) + 2] = (short)_mv[2];
                    _mvs[((start + x) * 4) + 3] = (short)_mv[3];
                }
            }
        }

        if (_header.DeltaLfPresent)
        {
            var deltaLfs = _state.DeltaLfs;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = MiIndex(_miRow + y, _miCol + x) * Av1Constants.FrameLfCount;
                    for (var i = 0; i < Av1Constants.FrameLfCount; i++)
                    {
                        deltaLfs[index + i] = (sbyte)_deltaLf[i];
                    }
                }
            }
        }

        // The palettes are only read from the block above and the block to the left
        for (var plane = 0; plane < 2; plane++)
        {
            var size = plane is 0 ? _paletteSizeY : _paletteSizeUv;
            if (size is 0)
                continue;

            var colors = _scratch.GetPaletteColors(plane)[..size];
            var above = _state.AbovePaletteColors[(plane * Av1Constants.PaletteColors * (_miCols + Av1FrameState.AboveContextMargin))..];
            for (var x = 0; x < width; x++)
            {
                colors.CopyTo(above[((_miCol + x) * Av1Constants.PaletteColors)..]);
            }

            var left = _scratch.GetLeftPaletteColors(plane);
            for (var y = 0; y < bh4; y++)
            {
                colors.CopyTo(left[(((_miRow + y) & 31) * Av1Constants.PaletteColors)..]);
            }
        }
    }

    // Section 5.11.6 "Mode info syntax"
    private void ModeInfo()
    {
        if (_header.FrameIsIntra)
        {
            IntraFrameModeInfo();
        }
        else
        {
            InterFrameModeInfo();
        }
    }
}
