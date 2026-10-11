namespace Meziantou.Framework.Imaging.Internals;

/// <content>The mode info of a block (AV1 specification, sections 5.11.7 to 5.11.17, 5.11.24 and 5.11.42 to 5.11.46).</content>
internal ref partial struct Av1TileDecoder
{
    // Section 5.11.7 "Intra frame mode info syntax"
    private void IntraFrameModeInfo()
    {
        _skip = false;
        if (_header.SegIdPreSkip)
            IntraSegmentId();

        _skipMode = false;
        ReadSkip();
        if (!_header.SegIdPreSkip)
            IntraSegmentId();

        ReadCdef();
        ReadDeltaQIndex();
        ReadDeltaLf();
        _readDeltas = false;
        _useIntrabc = _header.AllowIntrabc && ReadSymbol(Av1CdfLayout.Intrabc, Av1CdfLayout.IntrabcSize) is not 0;
        _refFrame0 = Av1Constants.IntraFrame;
        _refFrame1 = NoReference;
        _interintra = false;
        _motionMode = Av1Constants.Simple;
        _compoundType = Av1Constants.CompoundAverage;
        _compGroupIdx = 0;
        _compoundIdx = 0;
        if (_useIntrabc)
        {
            _isInter = true;
            _yMode = Av1Constants.DcPred;
            _uvMode = Av1Constants.DcPred;
            _paletteSizeY = 0;
            _paletteSizeUv = 0;
            _useFilterIntra = false;
            _angleDeltaY = 0;
            _angleDeltaUv = 0;
            _interpFilter0 = Av1Constants.Bilinear;
            _interpFilter1 = Av1Constants.Bilinear;
            FindMvStack(isCompound: false);
            AssignMv(isCompound: false);
            return;
        }

        var aboveMode = Av1Tables.IntraModeContext[_availU ? _yModes[MiIndex(_miRow - 1, _miCol)] : Av1Constants.DcPred];
        var leftMode = Av1Tables.IntraModeContext[_availL ? _yModes[MiIndex(_miRow, _miCol - 1)] : Av1Constants.DcPred];
        _yMode = ReadSymbol(Av1CdfLayout.IntraFrameYMode + (((aboveMode * Av1Constants.IntraModeContexts) + leftMode) * Av1CdfLayout.IntraFrameYModeSize), Av1CdfLayout.IntraFrameYModeSize);
        ReadIntraModeDetails();
    }

    // The syntax elements that follow the luma mode of a block that is not inter, in an intra frame and in an inter frame
    private void ReadIntraModeDetails()
    {
        _isInter = false;
        _mv = default;
        _interpFilter0 = 0;
        _interpFilter1 = 0;
        _paletteSizeY = 0;
        _paletteSizeUv = 0;
        IntraAngleInfoY();
        if (_hasChroma)
        {
            ReadUvMode();
            if (_uvMode is Av1Constants.UvCflPred)
                ReadCflAlphas();

            IntraAngleInfoUv();
        }

        if (_miSize >= Av1Constants.Block8x8 && BlockWidth(_miSize) <= 64 && BlockHeight(_miSize) <= 64 && _header.AllowScreenContentTools)
            PaletteModeInfo();

        FilterIntraModeInfo();
    }

    private static int BlockWidth(int size) => 4 * Av1Tables.Num4x4BlocksWide[size];

    private static int BlockHeight(int size) => 4 * Av1Tables.Num4x4BlocksHigh[size];

    private static bool IsDirectionalMode(int mode) => mode is >= Av1Constants.VPred and <= Av1Constants.D67Pred;

    // Section 5.11.8 "Intra segment ID syntax"
    private void IntraSegmentId()
    {
        if (_header.SegmentationEnabled)
        {
            ReadSegmentId();
        }
        else
        {
            _segmentId = 0;
        }

        _lossless = _header.LosslessArray[_segmentId];
    }

    // Section 5.11.9 "Read segment ID syntax"
    private void ReadSegmentId()
    {
        var prevUL = _availU && _availL ? _segmentIds[MiIndex(_miRow - 1, _miCol - 1)] : -1;
        var prevU = _availU ? _segmentIds[MiIndex(_miRow - 1, _miCol)] : -1;
        var prevL = _availL ? _segmentIds[MiIndex(_miRow, _miCol - 1)] : -1;
        int pred;
        if (prevU is -1)
        {
            pred = prevL is -1 ? 0 : prevL;
        }
        else if (prevL is -1)
        {
            pred = prevU;
        }
        else
        {
            pred = prevUL == prevU ? prevU : prevL;
        }

        if (_skip)
        {
            _segmentId = pred;
            return;
        }

        int ctx;
        if (prevUL < 0)
        {
            ctx = 0;
        }
        else if (prevUL == prevU && prevUL == prevL)
        {
            ctx = 2;
        }
        else if (prevUL == prevU || prevUL == prevL || prevU == prevL)
        {
            ctx = 1;
        }
        else
        {
            ctx = 0;
        }

        var coded = ReadSymbol(Av1CdfLayout.SegmentId + (ctx * Av1CdfLayout.SegmentIdSize), Av1CdfLayout.SegmentIdSize);
        var max = _header.LastActiveSegId + 1;
        _segmentId = NegDeinterleave(coded, pred, max);
        if (_segmentId < 0 || _segmentId >= max)
            throw Av1Errors.Invalid("a block uses a segment that is not active.");
    }

    private static int NegDeinterleave(int diff, int reference, int max)
    {
        if (reference is 0)
            return diff;

        if (reference >= max - 1)
            return max - diff - 1;

        if (2 * reference < max)
        {
            if (diff <= 2 * reference)
                return (diff & 1) is not 0 ? reference + ((diff + 1) >> 1) : reference - (diff >> 1);

            return diff;
        }

        if (diff <= 2 * (max - reference - 1))
            return (diff & 1) is not 0 ? reference + ((diff + 1) >> 1) : reference - (diff >> 1);

        return max - (diff + 1);
    }

    private readonly bool IsSegmentationFeatureActive(int feature) => _header.IsSegmentationFeatureActive(_segmentId, feature);

    // Section 5.11.11 "Skip syntax"
    private void ReadSkip()
    {
        if (_header.SegIdPreSkip && IsSegmentationFeatureActive(Av1Constants.SegLvlSkip))
        {
            _skip = true;
            return;
        }

        var ctx = 0;
        if (_availU)
            ctx += _flags[MiIndex(_miRow - 1, _miCol)] & Av1FrameState.SkipFlag;

        if (_availL)
            ctx += _flags[MiIndex(_miRow, _miCol - 1)] & Av1FrameState.SkipFlag;

        _skip = ReadSymbol(Av1CdfLayout.Skip + (ctx * Av1CdfLayout.SkipSize), Av1CdfLayout.SkipSize) is not 0;
    }

    // Section 5.11.56 "Read CDEF syntax"
    private readonly void ReadCdef()
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

        var value = (sbyte)ReadLiteral(_header.CdefBits);
        var w4 = Av1Tables.Num4x4BlocksWide[_miSize];
        var h4 = Av1Tables.Num4x4BlocksHigh[_miSize];
        for (var i = r; i < r + h4 && i < _miRows; i += CdefSize4)
        {
            for (var j = c; j < c + w4 && j < _miCols; j += CdefSize4)
            {
                _cdefIdx[((i >> 4) * cols) + (j >> 4)] = value;
            }
        }
    }

    // Section 5.11.12 "Quantizer index delta syntax"
    private void ReadDeltaQIndex()
    {
        var sbSize = _sequence.Use128x128Superblock ? Av1Constants.Block128x128 : Av1Constants.Block64x64;
        if (_miSize == sbSize && _skip)
            return;

        if (!_readDeltas)
            return;

        var deltaQAbs = ReadSymbol(Av1CdfLayout.DeltaQ, Av1CdfLayout.DeltaQSize);
        if (deltaQAbs is Av1Constants.DeltaQSmall)
        {
            var deltaQRemBits = ReadLiteral(3) + 1;
            var deltaQAbsBits = ReadLiteral(deltaQRemBits);
            deltaQAbs = deltaQAbsBits + (1 << deltaQRemBits) + 1;
        }

        if (deltaQAbs is not 0)
        {
            var reducedDeltaQIndex = ReadLiteral(1) is not 0 ? -deltaQAbs : deltaQAbs;
            _currentQIndex = Math.Clamp(_currentQIndex + (reducedDeltaQIndex << _header.DeltaQRes), 1, 255);
        }
    }

    // Section 5.11.13 "Loop filter delta syntax"
    private void ReadDeltaLf()
    {
        var sbSize = _sequence.Use128x128Superblock ? Av1Constants.Block128x128 : Av1Constants.Block64x64;
        if (_miSize == sbSize && _skip)
            return;

        if (!_readDeltas || !_header.DeltaLfPresent)
            return;

        var frameLfCount = 1;
        if (_header.DeltaLfMulti)
            frameLfCount = _planeCount > 1 ? Av1Constants.FrameLfCount : Av1Constants.FrameLfCount - 2;

        for (var i = 0; i < frameLfCount; i++)
        {
            var offset = _header.DeltaLfMulti ? Av1CdfLayout.DeltaLfMulti + (i * Av1CdfLayout.DeltaLfMultiCopyLength) : Av1CdfLayout.DeltaLf;
            var deltaLfAbs = ReadSymbol(offset, Av1CdfLayout.DeltaLfSize);
            if (deltaLfAbs is Av1Constants.DeltaLfSmall)
            {
                var n = ReadLiteral(3) + 1;
                deltaLfAbs = ReadLiteral(n) + (1 << n) + 1;
            }

            if (deltaLfAbs is not 0)
            {
                var reducedDeltaLfLevel = ReadLiteral(1) is not 0 ? -deltaLfAbs : deltaLfAbs;
                _deltaLf[i] = Math.Clamp(_deltaLf[i] + (reducedDeltaLfLevel << _header.DeltaLfRes), -Av1Constants.MaxLoopFilter, Av1Constants.MaxLoopFilter);
            }
        }
    }

    // Section 5.11.42 "Intra angle info luma syntax"
    private void IntraAngleInfoY()
    {
        _angleDeltaY = 0;
        if (_miSize >= Av1Constants.Block8x8 && IsDirectionalMode(_yMode))
            _angleDeltaY = ReadSymbol(Av1CdfLayout.AngleDelta + ((_yMode - Av1Constants.VPred) * Av1CdfLayout.AngleDeltaSize), Av1CdfLayout.AngleDeltaSize) - Av1Constants.MaxAngleDelta;
    }

    // Section 5.11.43 "Intra angle info chroma syntax"
    private void IntraAngleInfoUv()
    {
        _angleDeltaUv = 0;
        if (_miSize >= Av1Constants.Block8x8 && IsDirectionalMode(_uvMode))
            _angleDeltaUv = ReadSymbol(Av1CdfLayout.AngleDelta + ((_uvMode - Av1Constants.VPred) * Av1CdfLayout.AngleDeltaSize), Av1CdfLayout.AngleDeltaSize) - Av1Constants.MaxAngleDelta;
    }

    private void ReadUvMode()
    {
        bool cflAllowed;
        if (_lossless && GetPlaneResidualSize(_miSize, 1) is Av1Constants.Block4x4)
        {
            cflAllowed = true;
        }
        else if (!_lossless && Math.Max(BlockWidth(_miSize), BlockHeight(_miSize)) <= 32)
        {
            cflAllowed = true;
        }
        else
        {
            cflAllowed = false;
        }

        _uvMode = cflAllowed
            ? ReadSymbol(Av1CdfLayout.UvModeCflAllowed + (_yMode * Av1CdfLayout.UvModeCflAllowedSize), Av1CdfLayout.UvModeCflAllowedSize)
            : ReadSymbol(Av1CdfLayout.UvModeCflNotAllowed + (_yMode * Av1CdfLayout.UvModeCflNotAllowedSize), Av1CdfLayout.UvModeCflNotAllowedSize);
    }

    // Section 5.11.45 "Read CFL alphas syntax": the signs are CFL_SIGN_ZERO (0), CFL_SIGN_NEG (1) and CFL_SIGN_POS (2)
    private void ReadCflAlphas()
    {
        var cflAlphaSigns = ReadSymbol(Av1CdfLayout.CflSign, Av1CdfLayout.CflSignSize);
        var signU = (cflAlphaSigns + 1) / 3;
        var signV = (cflAlphaSigns + 1) % 3;
        _cflAlphaU = 0;
        _cflAlphaV = 0;
        if (signU is not 0)
        {
            var ctx = ((signU - 1) * 3) + signV;
            _cflAlphaU = 1 + ReadSymbol(Av1CdfLayout.CflAlpha + (ctx * Av1CdfLayout.CflAlphaSize), Av1CdfLayout.CflAlphaSize);
            if (signU is 1)
                _cflAlphaU = -_cflAlphaU;
        }

        if (signV is not 0)
        {
            var ctx = ((signV - 1) * 3) + signU;
            _cflAlphaV = 1 + ReadSymbol(Av1CdfLayout.CflAlpha + (ctx * Av1CdfLayout.CflAlphaSize), Av1CdfLayout.CflAlphaSize);
            if (signV is 1)
                _cflAlphaV = -_cflAlphaV;
        }
    }

    // Section 5.11.24 "Filter intra mode info syntax"
    private void FilterIntraModeInfo()
    {
        _useFilterIntra = false;
        if (_sequence.EnableFilterIntra && _yMode is Av1Constants.DcPred && _paletteSizeY is 0 && Math.Max(BlockWidth(_miSize), BlockHeight(_miSize)) <= 32)
        {
            _useFilterIntra = ReadSymbol(Av1CdfLayout.FilterIntra + (_miSize * Av1CdfLayout.FilterIntraSize), Av1CdfLayout.FilterIntraSize) is not 0;
            if (_useFilterIntra)
                _filterIntraMode = ReadSymbol(Av1CdfLayout.FilterIntraMode, Av1CdfLayout.FilterIntraModeSize);
        }
    }

    // Section 5.11.15 "TX size syntax"
    private void ReadTxSize(bool allowSelect)
    {
        if (_lossless)
        {
            _txSize = Av1Constants.Tx4x4;
            return;
        }

        var maxRectTxSize = Av1Tables.MaxTxSizeRect[_miSize];
        var maxTxDepth = Av1Tables.MaxTxDepth[_miSize];
        _txSize = maxRectTxSize;
        if (_miSize > Av1Constants.Block4x4 && allowSelect && _header.TxMode is Av1Constants.TxModeSelect)
        {
            var maxTxWidth = Av1Tables.TxWidth[maxRectTxSize];
            var maxTxHeight = Av1Tables.TxHeight[maxRectTxSize];
            int aboveW;
            if (_availU && (_flags[MiIndex(_miRow - 1, _miCol)] & Av1FrameState.IsInterFlag) is not 0)
            {
                aboveW = BlockWidth(_miSizes[MiIndex(_miRow - 1, _miCol)]);
            }
            else
            {
                aboveW = _availU ? GetAboveTxWidth(_miRow, _miCol) : 0;
            }

            int leftH;
            if (_availL && (_flags[MiIndex(_miRow, _miCol - 1)] & Av1FrameState.IsInterFlag) is not 0)
            {
                leftH = BlockHeight(_miSizes[MiIndex(_miRow, _miCol - 1)]);
            }
            else
            {
                leftH = _availL ? GetLeftTxHeight(_miRow, _miCol) : 0;
            }

            var ctx = (aboveW >= maxTxWidth ? 1 : 0) + (leftH >= maxTxHeight ? 1 : 0);
            var (offset, size) = maxTxDepth switch
            {
                4 => (Av1CdfLayout.Tx64x64, Av1CdfLayout.Tx64x64Size),
                3 => (Av1CdfLayout.Tx32x32, Av1CdfLayout.Tx32x32Size),
                2 => (Av1CdfLayout.Tx16x16, Av1CdfLayout.Tx16x16Size),
                _ => (Av1CdfLayout.Tx8x8, Av1CdfLayout.Tx8x8Size),
            };

            var txDepth = ReadSymbol(offset + (ctx * size), size);
            for (var i = 0; i < txDepth; i++)
            {
                _txSize = Av1Tables.SplitTxSize[_txSize];
            }
        }
    }

    private readonly int GetAboveTxWidth(int row, int col)
    {
        if (row == _miRow)
        {
            if (!_availU)
                return 64;

            var index = MiIndex(row - 1, col);
            const byte SkipAndInter = Av1FrameState.SkipFlag | Av1FrameState.IsInterFlag;
            if ((_flags[index] & SkipAndInter) is SkipAndInter)
                return BlockWidth(_miSizes[index]);
        }

        return Av1Tables.TxWidth[_interTxSizes[MiIndex(row - 1, col)]];
    }

    private readonly int GetLeftTxHeight(int row, int col)
    {
        if (col == _miCol)
        {
            if (!_availL)
                return 64;

            var index = MiIndex(row, col - 1);
            const byte SkipAndInter = Av1FrameState.SkipFlag | Av1FrameState.IsInterFlag;
            if ((_flags[index] & SkipAndInter) is SkipAndInter)
                return BlockHeight(_miSizes[index]);
        }

        return Av1Tables.TxHeight[_interTxSizes[MiIndex(row, col - 1)]];
    }

    // Section 5.11.16 "Block TX size syntax"
    private void ReadBlockTxSize()
    {
        var bw4 = Av1Tables.Num4x4BlocksWide[_miSize];
        var bh4 = Av1Tables.Num4x4BlocksHigh[_miSize];
        if (_header.TxMode is Av1Constants.TxModeSelect && _miSize > Av1Constants.Block4x4 && _isInter && !_skip && !_lossless)
        {
            var maxTxSz = Av1Tables.MaxTxSizeRect[_miSize];
            var txW4 = Av1Tables.TxWidth[maxTxSz] / Av1Constants.MiSize;
            var txH4 = Av1Tables.TxHeight[maxTxSz] / Av1Constants.MiSize;
            for (var row = _miRow; row < _miRow + bh4; row += txH4)
            {
                for (var col = _miCol; col < _miCol + bw4; col += txW4)
                {
                    ReadVarTxSize(row, col, maxTxSz, 0);
                }
            }

            return;
        }

        ReadTxSize(!_skip || !_isInter);
        var width = Math.Min(bw4, _miCols - _miCol);
        var height = Math.Min(bh4, _miRows - _miRow);
        for (var row = 0; row < height; row++)
        {
            _interTxSizes.Slice(MiIndex(_miRow + row, _miCol), width).Fill((byte)_txSize);
        }
    }
}
