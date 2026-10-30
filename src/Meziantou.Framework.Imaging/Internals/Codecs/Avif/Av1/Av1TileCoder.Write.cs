using System.Diagnostics;
using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>
/// The write walk of the encoder: for each syntax table that the decoder reads, the method that writes the same syntax
/// elements from the variables of the block (the fields of this type). A write method sits on the same context derivations
/// as its read method, and leaves the same state behind, so that what follows it is coded like a decoder decodes it.
/// </content>
/// <remarks>
/// The syntax elements go to the sink selected by <c>_sink</c>: nowhere (the walk only rebuilds the state of the blocks),
/// to a rate (the walk measures the candidates of the search, without adapting the distributions), or to the symbol
/// encoder.
/// </remarks>
internal ref partial struct Av1TileCoder
{
    // The largest value of tx_depth
    private const int MaxCodedTxDepth = 2;

    private enum SymbolSink
    {
        None,
        Cost,
        Range,
        Tape,
    }

    // The values of the tape of the workspace: a symbol and its distribution, a literal, a bit coded with a probability
    // that is not adapted, or the start of a superblock (negative: its row and its column of 4x4 blocks)
    private const int TapeLiteralFlag = 1 << 30;
    private const int TapeBoolFlag = 1 << 29;

    /// <summary>Creates the coder of the tiles of a frame to encode.</summary>
    public Av1TileCoder(Av1FrameBuffer frame, Av1FrameState state, Av1EncoderWorkspace workspace, Av1SymbolEncoder encoder, Av1TileScratch scratch, Av1ReferenceFrames references, Av1InterScratch? interScratch, Span<ushort> cdf, CancellationToken cancellationToken)
        : this(frame, state, symbols: null!, scratch, references, interScratch, cdf, cancellationToken)
    {
        _workspace = workspace;
        _encoder = encoder;
    }

    private void WriteSymbol(int offset, int size, int symbol)
    {
        Debug.Assert((uint)symbol < (uint)(size - 1));
        switch (_sink)
        {
            case SymbolSink.Cost:
                _rate += Av1Cost.GetSymbolCost(_cdf.Slice(offset, size), symbol);
                break;
            case SymbolSink.Range:
                _encoder!.WriteSymbol(_cdf.Slice(offset, size), symbol);
                break;
            case SymbolSink.Tape:
                // The distributions adapt as they do when the symbol is written: the search reads them
                if (!_header.DisableCdfUpdate)
                    Av1Cdf.Update(_cdf.Slice(offset, size), symbol);

                _workspace!.AppendTape((offset << 10) | (size << 5) | symbol);
                break;
        }
    }

    private void WriteLiteral(int value, int count)
    {
        switch (_sink)
        {
            case SymbolSink.Cost:
                _rate += Av1Cost.GetLiteralCost(count);
                break;
            case SymbolSink.Range:
                _encoder!.WriteLiteral(value, count);
                break;
            case SymbolSink.Tape:
                Debug.Assert(count <= 24);
                if (count > 0)
                    _workspace!.AppendTape(TapeLiteralFlag | (count << 24) | value);

                break;
        }
    }

    /// <summary>
    /// Sets whether the syntax elements of the tile are kept on the tape of the workspace, to be written by
    /// <see cref="ReplayTape"/> once the parameters of the loop restoration are chosen, rather than written as the
    /// superblocks are encoded.
    /// </summary>
    public void SetDeferred(bool value) => _defersSymbols = value;

    /// <summary>
    /// Writes the syntax elements of a tile that the tape holds, with the parameters of the loop restoration units of each
    /// superblock before its blocks (<c>read_lr</c> in <c>decode_tile</c>, section 5.11.2). The distributions are the ones
    /// that the tile starts with.
    /// </summary>
    public void ReplayTape(int tileRow, int tileCol, ReadOnlySpan<int> tape)
    {
        _miRowStart = _header.MiRowStarts[tileRow];
        _miRowEnd = _header.MiRowStarts[tileRow + 1];
        _miColStart = _header.MiColStarts[tileCol];
        _miColEnd = _header.MiColStarts[tileCol + 1];
        ResetLoopRestorationReferences();
        _sink = SymbolSink.Range;
        var encoder = _encoder!;
        var sbSize = _sequence.Use128x128Superblock ? Av1Constants.Block128x128 : Av1Constants.Block64x64;
        Span<ushort> cdf = [0, 1 << 15, 0];
        foreach (var value in tape)
        {
            if (value < 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                WriteLr((value >> 15) & 0x7FFF, value & 0x7FFF, sbSize);
            }
            else if ((value & TapeLiteralFlag) is not 0)
            {
                encoder.WriteLiteral(value & 0xFFFFFF, (value >> 24) & 0x1F);
            }
            else if ((value & TapeBoolFlag) is not 0)
            {
                cdf[0] = (ushort)((value >> 1) & 0x7FFF);
                cdf[2] = 0;
                encoder.WriteSymbol(cdf, value & 1);
            }
            else
            {
                encoder.WriteSymbol(_cdf.Slice(value >> 10, (value >> 5) & 0x1F), value & 0x1F);
            }
        }
    }

    // The partition syntax element of section 5.11.4 "Decode partition syntax", when it is coded
    private void WritePartition(int r, int c, int bSize, int partition)
    {
        if (bSize < Av1Constants.Block8x8)
            return;

        var availU = IsInside(r - 1, c);
        var availL = IsInside(r, c - 1);
        var halfBlock4x4 = Av1Tables.Num4x4BlocksWide[bSize] >> 1;
        var hasRows = (r + halfBlock4x4) < _miRows;
        var hasCols = (c + halfBlock4x4) < _miCols;
        if (hasRows && hasCols)
        {
            GetPartitionCdf(r, c, bSize, availU, availL, out var offset, out var size);
            WriteSymbol(offset, size, partition);
        }
        else if (hasCols || hasRows)
        {
            // split_or_horz and split_or_vert: a distribution derived from the partition distribution, which is not adapted
            var first = GetSplitOrCdf(r, c, bSize, availU, availL, isHorizontal: hasCols);
            var isSplit = partition is Av1Constants.PartitionSplit;
            switch (_sink)
            {
                case SymbolSink.Cost:
                    _rate += Av1Cost.GetCost(isSplit ? (1 << 15) - first : first);
                    break;
                case SymbolSink.Range:
                    Span<ushort> cdf = [first, 1 << 15, 0];
                    _encoder!.WriteSymbol(cdf, isSplit ? 1 : 0);
                    break;
                case SymbolSink.Tape:
                    _workspace!.AppendTape(TapeBoolFlag | (first << 1) | (isSplit ? 1 : 0));
                    break;
            }
        }
    }

    // The variables that the mode info of a block that is not inter sets without reading a syntax element (sections 5.11.7,
    // 5.11.8 and 5.11.22), for a frame without segmentation
    private void SetIntraBlockDefaults()
    {
        Debug.Assert(!_header.SegmentationEnabled);
        _segmentId = 0;
        _lossless = _header.LosslessArray[0];
        _skip = false;
        _skipMode = false;
        _useIntrabc = false;
        _isInter = false;
        _refFrame0 = Av1Constants.IntraFrame;
        _refFrame1 = NoReference;
        _interintra = false;
        _motionMode = Av1Constants.Simple;
        _compoundType = Av1Constants.CompoundAverage;
        _compGroupIdx = 0;
        _compoundIdx = 0;
        _mv = default;
        _interpFilter0 = 0;
        _interpFilter1 = 0;
        _angleDeltaY = 0;
        _angleDeltaUv = 0;
        _useFilterIntra = false;
        _filterIntraMode = 0;
        _cflAlphaU = 0;
        _cflAlphaV = 0;
        _paletteSizeY = 0;
        _paletteSizeUv = 0;
        _uvMode = Av1Constants.DcPred;
    }

    // The variables of a block predicted from LAST_FRAME alone that the mode info sets without reading a syntax element
    // (sections 5.11.23 to 5.11.30), for a frame without switchable interpolation filter or motion mode
    private void SetInterBlockDefaults()
    {
        SetIntraBlockDefaults();
        _isInter = true;
        _refFrame0 = Av1Constants.LastFrame;
        _refMvIdx = 0;
        _compoundIdx = 1;
        _interpFilter0 = _header.InterpolationFilter;
        _interpFilter1 = _header.InterpolationFilter;
    }

    // A block of an intra frame that is predicted from a part of the frame that is already coded (intra block copy): the
    // values that intra_frame_mode_info gives it
    private void SetCopyBlockDefaults()
    {
        SetIntraBlockDefaults();
        _useIntrabc = true;
        _isInter = true;
        _yMode = Av1Constants.DcPred;
        _uvMode = Av1Constants.DcPred;
        _interpFilter0 = Av1Constants.Bilinear;
        _interpFilter1 = Av1Constants.Bilinear;
    }

    // Section 5.11.6 "Mode info syntax"
    private void WriteModeInfo()
    {
        if (_header.FrameIsIntra)
        {
            WriteIntraFrameModeInfo();
        }
        else
        {
            WriteInterFrameModeInfo();
        }
    }

    // Section 5.11.18 "Inter frame mode info syntax", for a frame without segmentation, skip mode or loop filter delta
    private void WriteInterFrameModeInfo()
    {
        Debug.Assert(!_header.SkipModePresent && !_header.DeltaLfPresent && !_useIntrabc);
        SetNeighborReferences();
        WriteSkip();
        WriteCdef();
        WriteDeltaQIndex();
        _readDeltas = false;
        WriteSymbol(Av1CdfLayout.IsInter + (GetIsInterContext() * Av1CdfLayout.IsInterSize), Av1CdfLayout.IsInterSize, _isInter ? 1 : 0);
        if (_isInter)
        {
            WriteInterBlockModeInfo();
        }
        else
        {
            // Section 5.11.22 "Intra block mode info syntax"
            WriteYMode();
            WriteIntraModeDetails();
        }
    }

    // intra_frame_y_mode in an intra frame, y_mode in an inter frame
    private void WriteYMode()
    {
        if (_header.FrameIsIntra)
        {
            WriteSymbol(GetIntraFrameYModeCdf(), Av1CdfLayout.IntraFrameYModeSize, _yMode);
        }
        else
        {
            WriteSymbol(Av1CdfLayout.YMode + (Av1Tables.SizeGroup[_miSize] * Av1CdfLayout.YModeSize), Av1CdfLayout.YModeSize, _yMode);
        }
    }

    // Section 5.11.23 "Inter block mode info syntax", for a block predicted from LAST_FRAME alone. Like the decoder, builds
    // the motion vector stack of the block; the motion vector of a mode that codes none is the one of the stack
    private void WriteInterBlockModeInfo()
    {
        Debug.Assert(_refFrame0 is Av1Constants.LastFrame && _refFrame1 is NoReference && !_header.ReferenceSelect);
        Debug.Assert(_header.InterpolationFilter is not Av1Constants.Switchable && !_header.IsMotionModeSwitchable && !_sequence.EnableInterIntraCompound);

        // Section 5.11.25 "Ref frames syntax": single_ref_p1, single_ref_p3 and single_ref_p4 select LAST_FRAME
        WriteSingleRef(0, GetSingleRefP1Context());
        WriteSingleRef(2, GetCompRefContext());
        WriteSingleRef(3, GetCompRefP1Context());
        FindMvStack(isCompound: false);
        WriteSymbol(Av1CdfLayout.NewMv + (_newMvContext * Av1CdfLayout.NewMvSize), Av1CdfLayout.NewMvSize, _yMode is Av1Constants.Newmv ? 0 : 1);
        if (_yMode is not Av1Constants.Newmv)
        {
            WriteSymbol(Av1CdfLayout.ZeroMv + (_zeroMvContext * Av1CdfLayout.ZeroMvSize), Av1CdfLayout.ZeroMvSize, _yMode is Av1Constants.Globalmv ? 0 : 1);
            if (_yMode is not Av1Constants.Globalmv)
                WriteSymbol(Av1CdfLayout.RefMv + (_refMvContext * Av1CdfLayout.RefMvSize), Av1CdfLayout.RefMvSize, _yMode is Av1Constants.Nearestmv ? 0 : 1);
        }

        if (_yMode is Av1Constants.Newmv)
        {
            WriteDrlMode(firstIndex: 0);
        }
        else if (_yMode is Av1Constants.Nearmv)
        {
            WriteDrlMode(firstIndex: 1);
        }
        else
        {
            Debug.Assert(_refMvIdx is 0);
        }

        // Section 5.11.26 "Assign MV syntax"
        SetPredMv(refList: 0, _yMode);
        if (_yMode is Av1Constants.Newmv)
        {
            WriteMv(mvCtx: 0);
        }
        else
        {
            _mv[0] = _predMv[0];
            _mv[1] = _predMv[1];
        }

        Debug.Assert(IsMvValid(isCompound: false));
    }

    // A single_ref_p* syntax element that selects the group of reference frames that holds LAST_FRAME
    private void WriteSingleRef(int index, int ctx) => WriteSymbol(Av1CdfLayout.SingleRef + (((ctx * (Av1Constants.SingleRefs - 1)) + index) * Av1CdfLayout.SingleRefSize), Av1CdfLayout.SingleRefSize, 0);

    // The drl_mode syntax elements, which select RefMvIdx among two candidates from firstIndex
    private void WriteDrlMode(int firstIndex)
    {
        var coded = firstIndex;
        for (var idx = firstIndex; idx < firstIndex + 2; idx++)
        {
            if (_numMvFound <= idx + 1)
                continue;

            var drlMode = _refMvIdx > idx ? 1 : 0;
            WriteSymbol(Av1CdfLayout.DrlMode + (_drlCtxStack[idx] * Av1CdfLayout.DrlModeSize), Av1CdfLayout.DrlModeSize, drlMode);
            if (drlMode is 0)
            {
                coded = idx;
                break;
            }

            coded = idx + 1;
        }

        Debug.Assert(coded == _refMvIdx);
    }

    // Section 5.11.31 "MV syntax", for the first reference list of a block that is not coded with intra block copy
    private void WriteMv(int mvCtx)
    {
        var diffRow = _mv[0] - _predMv[0];
        var diffCol = _mv[1] - _predMv[1];
        var mvJoint = (diffRow is not 0 ? Av1Constants.MvJointHzvnz : Av1Constants.MvJointZero) | (diffCol is not 0 ? Av1Constants.MvJointHnzvz : Av1Constants.MvJointZero);
        WriteSymbol(Av1CdfLayout.MvJoint + (mvCtx * Av1CdfLayout.MvJointSize), Av1CdfLayout.MvJointSize, mvJoint);
        if (diffRow is not 0)
            WriteMvComponent(mvCtx, comp: 0, diffRow);

        if (diffCol is not 0)
            WriteMvComponent(mvCtx, comp: 1, diffCol);
    }

    // Section 5.11.32 "MV component syntax": the difference is mag = offset + 1 eighths of a sample, where the class of
    // offset is the position of its most significant bit, and its 3 least significant bits are the fraction
    private void WriteMvComponent(int mvCtx, int comp, int diff)
    {
        var context = (mvCtx * 2) + comp;
        var offset = Math.Abs(diff) - 1;
        Debug.Assert(offset >= 0 && offset < Av1Constants.Class0Size << (Av1Constants.MvClasses + 2));
        var mvClass = offset < Av1Constants.Class0Size << 3 ? Av1Constants.MvClass0 : BitOperations.Log2((uint)(offset >> 3));
        WriteSymbol(Av1CdfLayout.MvSign + (context * Av1CdfLayout.MvSignSize), Av1CdfLayout.MvSignSize, diff < 0 ? 1 : 0);
        WriteSymbol(Av1CdfLayout.MvClass + (context * Av1CdfLayout.MvClassSize), Av1CdfLayout.MvClassSize, mvClass);
        var fraction = (offset >> 1) & 3;
        var highPrecision = offset & 1;
        if (mvClass is Av1Constants.MvClass0)
        {
            var mvClass0Bit = offset >> 3;
            WriteSymbol(Av1CdfLayout.MvClass0Bit + (context * Av1CdfLayout.MvClass0BitSize), Av1CdfLayout.MvClass0BitSize, mvClass0Bit);
            if (!_header.ForceIntegerMv)
            {
                WriteSymbol(Av1CdfLayout.MvClass0Fr + (((context * Av1Constants.Class0Size) + mvClass0Bit) * Av1CdfLayout.MvClass0FrSize), Av1CdfLayout.MvClass0FrSize, fraction);
            }
            else
            {
                Debug.Assert(fraction is 3);
            }

            if (_header.AllowHighPrecisionMv)
            {
                WriteSymbol(Av1CdfLayout.MvClass0Hp + (context * Av1CdfLayout.MvClass0HpSize), Av1CdfLayout.MvClass0HpSize, highPrecision);
            }
            else
            {
                Debug.Assert(highPrecision is 1);
            }
        }
        else
        {
            var d = (offset - (Av1Constants.Class0Size << (mvClass + 2))) >> 3;
            for (var i = 0; i < mvClass; i++)
            {
                WriteSymbol(Av1CdfLayout.MvBit + (((context * Av1Constants.MvOffsetBits) + i) * Av1CdfLayout.MvBitSize), Av1CdfLayout.MvBitSize, (d >> i) & 1);
            }

            if (!_header.ForceIntegerMv)
            {
                WriteSymbol(Av1CdfLayout.MvFr + (context * Av1CdfLayout.MvFrSize), Av1CdfLayout.MvFrSize, fraction);
            }
            else
            {
                Debug.Assert(fraction is 3);
            }

            if (_header.AllowHighPrecisionMv)
            {
                WriteSymbol(Av1CdfLayout.MvHp + (context * Av1CdfLayout.MvHpSize), Av1CdfLayout.MvHpSize, highPrecision);
            }
            else
            {
                Debug.Assert(highPrecision is 1);
            }
        }
    }

    // Section 5.11.7 "Intra frame mode info syntax"
    private void WriteIntraFrameModeInfo()
    {
        WriteSkip();
        WriteCdef();
        WriteDeltaQIndex();
        _readDeltas = false;
        if (_header.AllowIntrabc)
            WriteSymbol(Av1CdfLayout.Intrabc, Av1CdfLayout.IntrabcSize, _useIntrabc ? 1 : 0);

        if (_useIntrabc)
        {
            // Like the decoder, builds the motion vector stack of the block: the motion vector is coded as its
            // difference with the one that the stack predicts
            FindMvStack(isCompound: false);
            SetIntrabcPredMv();
            WriteMv(Av1Constants.MvIntrabcContext);
            Debug.Assert(IsMvValid(isCompound: false));
            return;
        }

        WriteYMode();
        WriteIntraModeDetails();
    }

    // The syntax elements that follow the luma mode of a block that is not inter
    private void WriteIntraModeDetails()
    {
        WriteIntraAngleInfoY();
        if (_hasChroma)
            WriteUvModeInfo();

        if (IsPaletteAllowed())
        {
            WritePaletteModeInfoY();
            WritePaletteModeInfoUv();
        }

        WriteFilterIntraModeInfo();
    }

    // Section 5.11.11 "Skip syntax"
    private void WriteSkip() => WriteSymbol(Av1CdfLayout.Skip + (GetSkipContext() * Av1CdfLayout.SkipSize), Av1CdfLayout.SkipSize, _skip ? 1 : 0);

    // Section 5.11.56 "Read CDEF syntax": the index of the 64x64 block is coded with its first block that is not skipped
    private void WriteCdef()
    {
        if (_skip || _header.CodedLossless || !_sequence.EnableCdef || _header.AllowIntrabc)
            return;

        const int CdefSize4 = 16;
        var r = _miRow & ~(CdefSize4 - 1);
        var c = _miCol & ~(CdefSize4 - 1);
        var cols = _state.CdefCols;
        var index = ((r >> 4) * cols) + (c >> 4);
        if (_cdefIdx[index] is not -1)
            return;

        // A frame codes one pair of strengths until the search of the strengths of each 64x64 block exists
        Debug.Assert(_header.CdefBits is 0);
        const sbyte Value = 0;
        WriteLiteral(Value, _header.CdefBits);
        if (_sink is not (SymbolSink.Range or SymbolSink.Tape))
            return;

        // The search and the rebuilding of the state write the mode info of a block several times: only the walk that
        // produces the stream, or the tape it is written from, marks the index as coded
        var w4 = Av1Tables.Num4x4BlocksWide[_miSize];
        var h4 = Av1Tables.Num4x4BlocksHigh[_miSize];
        for (var i = r; i < r + h4 && i < _miRows; i += CdefSize4)
        {
            for (var j = c; j < c + w4 && j < _miCols; j += CdefSize4)
            {
                _cdefIdx[((i >> 4) * cols) + (j >> 4)] = Value;
            }
        }
    }

    // Section 5.11.12 "Quantizer index delta syntax"
    private readonly void WriteDeltaQIndex()
    {
        if (_header.DeltaQPresent)
            throw new NotSupportedException("Quantizer index deltas are not encoded yet.");
    }

    // Section 5.11.42 "Intra angle info luma syntax"
    private void WriteIntraAngleInfoY()
    {
        if (_miSize >= Av1Constants.Block8x8 && IsDirectionalMode(_yMode))
        {
            WriteSymbol(Av1CdfLayout.AngleDelta + ((_yMode - Av1Constants.VPred) * Av1CdfLayout.AngleDeltaSize), Av1CdfLayout.AngleDeltaSize, _angleDeltaY + Av1Constants.MaxAngleDelta);
        }
        else
        {
            Debug.Assert(_angleDeltaY is 0);
        }
    }

    // uv_mode, section 5.11.45 "Read CFL alphas syntax" and section 5.11.43 "Intra angle info chroma syntax"
    private void WriteUvModeInfo()
    {
        if (IsCflAllowed())
        {
            WriteSymbol(Av1CdfLayout.UvModeCflAllowed + (_yMode * Av1CdfLayout.UvModeCflAllowedSize), Av1CdfLayout.UvModeCflAllowedSize, _uvMode);
        }
        else
        {
            Debug.Assert(_uvMode is not Av1Constants.UvCflPred);
            WriteSymbol(Av1CdfLayout.UvModeCflNotAllowed + (_yMode * Av1CdfLayout.UvModeCflNotAllowedSize), Av1CdfLayout.UvModeCflNotAllowedSize, _uvMode);
        }

        if (_uvMode is Av1Constants.UvCflPred)
        {
            // The signs are CFL_SIGN_ZERO (0), CFL_SIGN_NEG (1) and CFL_SIGN_POS (2); both are not zero together
            var signU = _cflAlphaU is 0 ? 0 : (_cflAlphaU < 0 ? 1 : 2);
            var signV = _cflAlphaV is 0 ? 0 : (_cflAlphaV < 0 ? 1 : 2);
            Debug.Assert(signU is not 0 || signV is not 0);
            WriteSymbol(Av1CdfLayout.CflSign, Av1CdfLayout.CflSignSize, (signU * 3) + signV - 1);
            if (signU is not 0)
            {
                var ctx = ((signU - 1) * 3) + signV;
                WriteSymbol(Av1CdfLayout.CflAlpha + (ctx * Av1CdfLayout.CflAlphaSize), Av1CdfLayout.CflAlphaSize, Math.Abs(_cflAlphaU) - 1);
            }

            if (signV is not 0)
            {
                var ctx = ((signV - 1) * 3) + signU;
                WriteSymbol(Av1CdfLayout.CflAlpha + (ctx * Av1CdfLayout.CflAlphaSize), Av1CdfLayout.CflAlphaSize, Math.Abs(_cflAlphaV) - 1);
            }
        }

        if (_miSize >= Av1Constants.Block8x8 && IsDirectionalMode(_uvMode))
        {
            WriteSymbol(Av1CdfLayout.AngleDelta + ((_uvMode - Av1Constants.VPred) * Av1CdfLayout.AngleDeltaSize), Av1CdfLayout.AngleDeltaSize, _angleDeltaUv + Av1Constants.MaxAngleDelta);
        }
        else
        {
            Debug.Assert(_angleDeltaUv is 0);
        }
    }

    // Section 5.11.24 "Filter intra mode info syntax"
    private void WriteFilterIntraModeInfo()
    {
        if (IsFilterIntraAllowed())
        {
            WriteSymbol(Av1CdfLayout.FilterIntra + (_miSize * Av1CdfLayout.FilterIntraSize), Av1CdfLayout.FilterIntraSize, _useFilterIntra ? 1 : 0);
            if (_useFilterIntra)
                WriteSymbol(Av1CdfLayout.FilterIntraMode, Av1CdfLayout.FilterIntraModeSize, _filterIntraMode);
        }
        else
        {
            Debug.Assert(!_useFilterIntra);
        }
    }

    // Section 5.11.16 "Block TX size syntax". The transform size of a block that is not inter is the one of the block
    // (TxSize), which the search chose; the transform blocks of an inter block are not split
    private void WriteBlockTxSize()
    {
        var maxTxSize = Av1Tables.MaxTxSizeRect[_miSize];
        var selects = _header.TxMode is Av1Constants.TxModeSelect && _miSize > Av1Constants.Block4x4 && !_lossless;
        if (selects && _isInter && !_skip)
        {
            // Section 5.11.17 "Var TX size syntax", for each transform block of the largest size
            var txW4 = Av1Tables.TxWidth[maxTxSize] / Av1Constants.MiSize;
            var txH4 = Av1Tables.TxHeight[maxTxSize] / Av1Constants.MiSize;
            for (var row = _miRow; row < _miRow + Av1Tables.Num4x4BlocksHigh[_miSize]; row += txH4)
            {
                for (var col = _miCol; col < _miCol + Av1Tables.Num4x4BlocksWide[_miSize]; col += txW4)
                {
                    WriteVarTxSize(row, col, maxTxSize, depth: 0);
                }
            }

            return;
        }

        // Section 5.11.15 "TX size syntax": a lossless block has 4x4 transform blocks, and a block without tx_depth the
        // largest transform of its size
        if (_lossless)
        {
            _txSize = Av1Constants.Tx4x4;
        }
        else if (selects && !_isInter)
        {
            var txDepth = 0;
            for (var txSz = maxTxSize; txSz != _txSize; txSz = Av1Tables.SplitTxSize[txSz])
            {
                txDepth++;
                Debug.Assert(txDepth <= Math.Min(MaxCodedTxDepth, (int)Av1Tables.MaxTxDepth[_miSize]));
            }

            GetTxDepthCdf(out var offset, out var size);
            WriteSymbol(offset, size, txDepth);
        }
        else
        {
            _txSize = maxTxSize;
        }

        var width = Math.Min(Av1Tables.Num4x4BlocksWide[_miSize], _miCols - _miCol);
        var height = Math.Min(Av1Tables.Num4x4BlocksHigh[_miSize], _miRows - _miRow);
        for (var row = 0; row < height; row++)
        {
            _interTxSizes.Slice(MiIndex(_miRow + row, _miCol), width).Fill((byte)_txSize);
        }
    }

    // Section 5.11.17 "Var TX size syntax" for a transform block that is not split
    private void WriteVarTxSize(int row, int col, int txSz, int depth)
    {
        if (row >= _miRows || col >= _miCols)
            return;

        if (txSz is not Av1Constants.Tx4x4 && depth is not Av1Constants.MaxVartxDepth)
            WriteSymbol(Av1CdfLayout.TxfmSplit + (GetTxfmSplitContext(row, col, txSz) * Av1CdfLayout.TxfmSplitSize), Av1CdfLayout.TxfmSplitSize, 0);

        var width = Math.Min(Av1Tables.TxWidth[txSz] / Av1Constants.MiSize, _miCols - col);
        var height = Math.Min(Av1Tables.TxHeight[txSz] / Av1Constants.MiSize, _miRows - row);
        for (var i = 0; i < height; i++)
        {
            _interTxSizes.Slice(MiIndex(row + i, col), width).Fill((byte)txSz);
        }

        _txSize = txSz;
    }

    // The transform type that compute_tx_type returns for a luma transform block whose transform type syntax element is
    // lumaTxType: the scan order of the coefficients depends on it
    private readonly int GetLumaPlaneTxType(int txSz, int lumaTxType)
    {
        return _lossless || Av1Tables.TxSizeSqrUp[txSz] > Av1Constants.Tx32x32 ? Av1Constants.DctDct : lumaTxType;
    }

    /// <summary>
    /// Writes the coefficients of a transform block (section 5.11.39 "Coefficients syntax"): <paramref name="eob"/> levels
    /// in scan order, the last one not zero. Like the decoder, leaves the coefficients of the <paramref name="eob"/> first
    /// positions of the scan in <c>Quant</c>, the transform type in <c>PlaneTxType</c>, and updates the level and DC contexts.
    /// </summary>
    private void WriteCoeffs(int plane, int startX, int startY, int txSz, int lumaTxType, scoped ReadOnlySpan<int> levels, int eob)
    {
        Debug.Assert(eob is 0 || levels[eob - 1] is not 0);
        var x4 = startX >> 2;
        var y4 = startY >> 2;
        var w4 = Av1Tables.TxWidth[txSz] >> 2;
        var h4 = Av1Tables.TxHeight[txSz] >> 2;
        var txSzCtx = (Av1Tables.TxSizeSqr[txSz] + Av1Tables.TxSizeSqrUp[txSz] + 1) >> 1;
        var ptype = plane > 0 ? 1 : 0;
        var quant = _scratch.Quant;

        var aboveLevel = _state.GetAboveLevelContext(plane);
        var aboveDc = _state.GetAboveDcContext(plane);
        var leftLevel = _scratch.GetLeftLevelContext(plane);
        var leftDc = _scratch.GetLeftDcContext(plane);
        var maxX4 = plane > 0 ? _miCols >> _subX : _miCols;
        var maxY4 = plane > 0 ? _miRows >> _subY : _miRows;

        var culLevel = 0;
        var dcCategory = 0;
        WriteSymbol(Av1CdfLayout.TxbSkip + (((txSzCtx * Av1Constants.TxbSkipContexts) + GetAllZeroContext(plane, txSz, x4, y4, w4, h4, maxX4, maxY4)) * Av1CdfLayout.TxbSkipSize), Av1CdfLayout.TxbSkipSize, eob is 0 ? 1 : 0);
        if (eob is 0)
        {
            if (plane is 0)
                SetTxTypes(x4, y4, w4, h4, Av1Constants.DctDct);
        }
        else
        {
            if (plane is 0)
                WriteTransformType(x4, y4, txSz, lumaTxType);

            _planeTxType = ComputeTxType(plane, txSz, x4, y4);
            var scan = GetScan(txSz, _planeTxType);
            var txClass = GetTxClass(_planeTxType);

            WriteEob(txSz, txSzCtx, ptype, txClass, eob);

            var adjTxSz = Av1Tables.AdjustedTxSize[txSz];
            var bwl = Av1Tables.TxWidthLog2[adjTxSz];
            var width = 1 << bwl;
            var height = Av1Tables.TxHeight[adjTxSz];
            var coeffBaseOffset = Av1CdfLayout.CoeffBase + ((((txSzCtx * Av1Constants.PlaneTypes) + ptype) * Av1Constants.SigCoefContexts) * Av1CdfLayout.CoeffBaseSize);
            var coeffBaseEobOffset = Av1CdfLayout.CoeffBaseEob + ((((txSzCtx * Av1Constants.PlaneTypes) + ptype) * Av1Constants.SigCoefContextsEob) * Av1CdfLayout.CoeffBaseEobSize);
            var coeffBrOffset = Av1CdfLayout.CoeffBr + ((((Math.Min(txSzCtx, Av1Constants.Tx32x32) * Av1Constants.PlaneTypes) + ptype) * Av1Constants.LevelContexts) * Av1CdfLayout.CoeffBrSize);
            const int MaxCodedLevel = Av1Constants.NumBaseLevels + Av1Constants.CoeffBaseRange + 1;
            var levelStride = width + Av1TileScratch.LevelMapPadding;
            var levelMap = _scratch.LevelMap[..((height + Av1TileScratch.LevelMapPadding) * levelStride)];
            levelMap.Clear();
            for (var c = eob - 1; c >= 0; c--)
            {
                var pos = scan[c];
                var row = pos >> bwl;
                var col = pos & (width - 1);
                var magnitude = Math.Abs(levels[c]);
                var level = Math.Min(magnitude, Av1Constants.NumBaseLevels + 1);
                if (c == eob - 1)
                {
                    var ctx = GetCoeffBaseEobContext(c, width, height);
                    WriteSymbol(coeffBaseEobOffset + (ctx * Av1CdfLayout.CoeffBaseEobSize), Av1CdfLayout.CoeffBaseEobSize, level - 1);
                }
                else
                {
                    var ctx = GetCoeffBaseContext(levelMap, txSz, txClass, levelStride, row, col);
                    WriteSymbol(coeffBaseOffset + (ctx * Av1CdfLayout.CoeffBaseSize), Av1CdfLayout.CoeffBaseSize, level);
                }

                if (level > Av1Constants.NumBaseLevels)
                {
                    var ctx = GetCoeffBrContext(levelMap, txClass, levelStride, row, col);
                    for (var idx = 0; idx < Av1Constants.CoeffBaseRange / (Av1Constants.BrCdfSize - 1); idx++)
                    {
                        var coeffBr = Math.Min(magnitude - level, Av1Constants.BrCdfSize - 1);
                        WriteSymbol(coeffBrOffset + (ctx * Av1CdfLayout.CoeffBrSize), Av1CdfLayout.CoeffBrSize, coeffBr);
                        level += coeffBr;
                        if (coeffBr < Av1Constants.BrCdfSize - 1)
                            break;
                    }
                }

                // The contexts of the coefficients that follow read the levels coded so far, without their Golomb part
                Debug.Assert(level == Math.Min(magnitude, MaxCodedLevel));
                levelMap[(row * levelStride) + col] = (byte)level;
            }

            for (var c = 0; c < eob; c++)
            {
                var pos = scan[c];
                var value = levels[c];
                quant[pos] = value;
                if (value is 0)
                    continue;

                var sign = value < 0 ? 1 : 0;
                var magnitude = Math.Abs(value);
                Debug.Assert(magnitude <= 0xFFFFF);
                if (c is 0)
                {
                    var ctx = GetDcSignContext(aboveDc, leftDc, x4, y4, w4, h4, maxX4, maxY4);
                    WriteSymbol(Av1CdfLayout.DcSign + (((ptype * Av1Constants.DcSignContexts) + ctx) * Av1CdfLayout.DcSignSize), Av1CdfLayout.DcSignSize, sign);
                }
                else
                {
                    WriteLiteral(sign, 1);
                }

                if (magnitude >= MaxCodedLevel)
                {
                    // golomb_length_bit then golomb_data_bit: x has length bits, written after length - 1 zero bits
                    var x = magnitude - (MaxCodedLevel - 1);
                    var length = BitOperations.Log2((uint)x) + 1;
                    WriteLiteral(0, length - 1);
                    WriteLiteral(1, 1);
                    WriteLiteral(x & ((1 << (length - 1)) - 1), length - 1);
                }

                if (pos is 0)
                    dcCategory = sign is not 0 ? 1 : 2;

                culLevel += magnitude;
            }

            culLevel = Math.Min(63, culLevel);
        }

        aboveLevel.Slice(x4, w4).Fill((byte)culLevel);
        aboveDc.Slice(x4, w4).Fill((byte)dcCategory);
        for (var i = 0; i < h4; i++)
        {
            leftLevel[(y4 + i) & 31] = (byte)culLevel;
            leftDc[(y4 + i) & 31] = (byte)dcCategory;
        }
    }

    // eob_pt_16 to eob_pt_1024, eob_extra and eob_extra_bit (section 5.11.39 "Coefficients syntax")
    private void WriteEob(int txSz, int txSzCtx, int ptype, int txClass, int eob)
    {
        // eob_pt_*: eob is 1, 2, or 2^(eobPt - 2) + 1 plus eobPt - 2 extra bits
        var eobPt = eob <= 2 ? eob : BitOperations.Log2((uint)(eob - 1)) + 2;
        var eobMultisize = Math.Min((int)Av1Tables.TxWidthLog2[txSz], 5) + Math.Min((int)Av1Tables.TxHeightLog2[txSz], 5) - 4;
        var eobCtx = txClass is TxClass2D ? 0 : 1;
        switch (eobMultisize)
        {
            case 0:
                WriteSymbol(Av1CdfLayout.EobPt16 + (((ptype * 2) + eobCtx) * Av1CdfLayout.EobPt16Size), Av1CdfLayout.EobPt16Size, eobPt - 1);
                break;
            case 1:
                WriteSymbol(Av1CdfLayout.EobPt32 + (((ptype * 2) + eobCtx) * Av1CdfLayout.EobPt32Size), Av1CdfLayout.EobPt32Size, eobPt - 1);
                break;
            case 2:
                WriteSymbol(Av1CdfLayout.EobPt64 + (((ptype * 2) + eobCtx) * Av1CdfLayout.EobPt64Size), Av1CdfLayout.EobPt64Size, eobPt - 1);
                break;
            case 3:
                WriteSymbol(Av1CdfLayout.EobPt128 + (((ptype * 2) + eobCtx) * Av1CdfLayout.EobPt128Size), Av1CdfLayout.EobPt128Size, eobPt - 1);
                break;
            case 4:
                WriteSymbol(Av1CdfLayout.EobPt256 + (((ptype * 2) + eobCtx) * Av1CdfLayout.EobPt256Size), Av1CdfLayout.EobPt256Size, eobPt - 1);
                break;
            case 5:
                WriteSymbol(Av1CdfLayout.EobPt512 + (ptype * Av1CdfLayout.EobPt512Size), Av1CdfLayout.EobPt512Size, eobPt - 1);
                break;
            default:
                WriteSymbol(Av1CdfLayout.EobPt1024 + (ptype * Av1CdfLayout.EobPt1024Size), Av1CdfLayout.EobPt1024Size, eobPt - 1);
                break;
        }

        if (eobPt >= 3)
        {
            // eob_extra then eob_extra_bit: the bits of eob - (2^(eobPt - 2) + 1), most significant first
            var extra = eob - ((1 << (eobPt - 2)) + 1);
            var extraBits = eobPt - 2;
            WriteSymbol(Av1CdfLayout.EobExtra + (((((txSzCtx * Av1Constants.PlaneTypes) + ptype) * Av1Constants.EobCoefContexts) + (eobPt - 3)) * Av1CdfLayout.EobExtraSize), Av1CdfLayout.EobExtraSize, (extra >> (extraBits - 1)) & 1);
            for (var i = extraBits - 2; i >= 0; i--)
            {
                WriteLiteral((extra >> i) & 1, 1);
            }
        }
    }

    // Section 5.11.47 "Transform type syntax"
    private void WriteTransformType(int x4, int y4, int txSz, int txType)
    {
        var set = GetTxSet(txSz);
        var qIndex = _header.SegmentationEnabled ? _header.GetQIndex(_segmentId, currentQIndex: -1) : _header.BaseQIdx;
        if (set > 0 && qIndex > 0)
        {
            var txSzSqr = Av1Tables.TxSizeSqr[txSz];
            var symbol = Av1EncoderTables.GetTxTypeSymbol(_isInter, set, txType);
            Debug.Assert(symbol >= 0);
            if (_isInter)
            {
                switch (set)
                {
                    case Av1Constants.TxSetInter1:
                        WriteSymbol(Av1CdfLayout.InterTxTypeSet1 + (txSzSqr * Av1CdfLayout.InterTxTypeSet1Size), Av1CdfLayout.InterTxTypeSet1Size, symbol);
                        break;
                    case Av1Constants.TxSetInter2:
                        WriteSymbol(Av1CdfLayout.InterTxTypeSet2, Av1CdfLayout.InterTxTypeSet2Size, symbol);
                        break;
                    default:
                        WriteSymbol(Av1CdfLayout.InterTxTypeSet3 + (txSzSqr * Av1CdfLayout.InterTxTypeSet3Size), Av1CdfLayout.InterTxTypeSet3Size, symbol);
                        break;
                }
            }
            else
            {
                var intraDir = _useFilterIntra ? Av1Tables.FilterIntraModeToIntraDir[_filterIntraMode] : _yMode;
                if (set is Av1Constants.TxSetIntra1)
                {
                    WriteSymbol(Av1CdfLayout.IntraTxTypeSet1 + (((txSzSqr * Av1Constants.IntraModes) + intraDir) * Av1CdfLayout.IntraTxTypeSet1Size), Av1CdfLayout.IntraTxTypeSet1Size, symbol);
                }
                else
                {
                    WriteSymbol(Av1CdfLayout.IntraTxTypeSet2 + (((txSzSqr * Av1Constants.IntraModes) + intraDir) * Av1CdfLayout.IntraTxTypeSet2Size), Av1CdfLayout.IntraTxTypeSet2Size, symbol);
                }
            }
        }
        else
        {
            Debug.Assert(txType is Av1Constants.DctDct);
        }

        SetTxTypes(x4, y4, Av1Tables.TxWidth[txSz] >> 2, Av1Tables.TxHeight[txSz] >> 2, txType);
    }
}
