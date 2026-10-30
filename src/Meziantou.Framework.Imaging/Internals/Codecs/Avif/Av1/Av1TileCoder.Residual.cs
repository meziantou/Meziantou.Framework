namespace Meziantou.Framework.Imaging.Internals;

/// <content>The residual of a block (AV1 specification, sections 5.11.34 to 5.11.41, 5.11.47 and 5.11.48).</content>
internal ref partial struct Av1TileCoder
{
    private const int TxClass2D = Av1Constants.TxClass2d;

    private enum ResidualOperation
    {
        Decode,
        Search,
        Replay,
    }

    // Section 5.11.34 "Residual syntax"
    private void Residual() => Residual(ResidualOperation.Decode, planeMask: 0b111);

    // The transform blocks of the planes of planeMask, in the order of the residual syntax. The decoder reads and
    // reconstructs them; the encoder searches their coefficients, or writes the ones it recorded
    private void Residual(ResidualOperation operation, int planeMask)
    {
        _qIndex = _header.GetQIndex(_segmentId, _currentQIndex);
        var widthChunks = Math.Max(1, BlockWidth(_miSize) >> 6);
        var heightChunks = Math.Max(1, BlockHeight(_miSize) >> 6);
        var miSizeChunk = widthChunks > 1 || heightChunks > 1 ? Av1Constants.Block64x64 : _miSize;
        for (var chunkY = 0; chunkY < heightChunks; chunkY++)
        {
            for (var chunkX = 0; chunkX < widthChunks; chunkX++)
            {
                for (var plane = 0; plane < 1 + (_hasChroma ? 2 : 0); plane++)
                {
                    if (((planeMask >> plane) & 1) is 0)
                        continue;

                    var txSz = _lossless ? Av1Constants.Tx4x4 : GetTxSize(plane, _txSize);
                    var stepX = Av1Tables.TxWidth[txSz] >> 2;
                    var stepY = Av1Tables.TxHeight[txSz] >> 2;
                    var planeSz = GetPlaneResidualSize(miSizeChunk, plane);
                    var num4x4W = Av1Tables.Num4x4BlocksWide[planeSz];
                    var num4x4H = Av1Tables.Num4x4BlocksHigh[planeSz];
                    var subX = plane > 0 ? _subX : 0;
                    var subY = plane > 0 ? _subY : 0;
                    if (_isInter && !_lossless && plane is 0)
                    {
                        var miRowChunk = _miRow + (chunkY << 4);
                        var miColChunk = _miCol + (chunkX << 4);
                        TransformTree(operation, miColChunk * Av1Constants.MiSize, miRowChunk * Av1Constants.MiSize, num4x4W * 4, num4x4H * 4);
                        continue;
                    }

                    var baseXBlock = (_miCol >> subX) * Av1Constants.MiSize;
                    var baseYBlock = (_miRow >> subY) * Av1Constants.MiSize;
                    for (var y = 0; y < num4x4H; y += stepY)
                    {
                        for (var x = 0; x < num4x4W; x += stepX)
                        {
                            var blockX = x + ((chunkX << 4) >> subX);
                            var blockY = y + ((chunkY << 4) >> subY);
                            switch (operation)
                            {
                                case ResidualOperation.Decode:
                                    TransformBlock(plane, baseXBlock, baseYBlock, txSz, blockX, blockY);
                                    break;
                                case ResidualOperation.Search:
                                    SearchTransformBlock(plane, baseXBlock, baseYBlock, txSz, blockX, blockY);
                                    break;
                                default:
                                    ReplayTransformBlock(plane, baseXBlock, baseYBlock, txSz, blockX, blockY);
                                    break;
                            }
                        }
                    }
                }
            }
        }
    }

    // Section 5.11.37 "Get TX size function"
    private readonly int GetTxSize(int plane, int txSz)
    {
        if (plane is 0)
            return txSz;

        var uvTx = Av1Tables.MaxTxSizeRect[GetPlaneResidualSize(_miSize, plane)];
        if (Av1Tables.TxWidth[uvTx] is 64 || Av1Tables.TxHeight[uvTx] is 64)
        {
            if (Av1Tables.TxWidth[uvTx] is 16)
                return Av1Constants.Tx16x32;

            if (Av1Tables.TxHeight[uvTx] is 16)
                return Av1Constants.Tx32x16;

            return Av1Constants.Tx32x32;
        }

        return uvTx;
    }

    // Section 5.11.35 "Transform block syntax"
    private void TransformBlock(int plane, int baseX, int baseY, int txSz, int x, int y)
    {
        var startX = baseX + (4 * x);
        var startY = baseY + (4 * y);
        if (!IsTransformBlockInFrame(plane, startX, startY))
            return;

        PredictTransformBlock(plane, txSz, startX, startY, x, y);
        if (!_skip)
        {
            var eob = Coeffs(plane, startX, startY, txSz);
            if (eob > 0)
            {
                var planeBuffer = _frame.GetPlane(plane);
                Reconstruct(planeBuffer.Samples, planeBuffer.Stride, plane, startX, startY, txSz, eob);
            }
        }

        FinishTransformBlock(plane, txSz, startX, startY);
    }

    // The transform blocks that start outside the frame are not coded
    private readonly bool IsTransformBlockInFrame(int plane, int startX, int startY)
    {
        var maxX = (_miCols * Av1Constants.MiSize) >> (plane > 0 ? _subX : 0);
        var maxY = (_miRows * Av1Constants.MiSize) >> (plane > 0 ? _subY : 0);
        return startX < maxX && startY < maxY;
    }

    // The prediction of a transform block of a block that is not inter (section 5.11.35): the palette, or the intra
    // prediction and the chroma from luma prediction
    private void PredictTransformBlock(int plane, int txSz, int startX, int startY, int x, int y)
    {
        var subX = plane > 0 ? _subX : 0;
        var subY = plane > 0 ? _subY : 0;
        var row = (startY << subY) >> Av1Constants.MiSizeLog2;
        var col = (startX << subX) >> Av1Constants.MiSizeLog2;
        var subBlockMiRow = row & _sbMask;
        var subBlockMiCol = col & _sbMask;
        var stepX = Av1Tables.TxWidth[txSz] >> Av1Constants.MiSizeLog2;
        var stepY = Av1Tables.TxHeight[txSz] >> Av1Constants.MiSizeLog2;
        var maxX = (_miCols * Av1Constants.MiSize) >> subX;
        var maxY = (_miRows * Av1Constants.MiSize) >> subY;
        var planeBuffer = _frame.GetPlane(plane);
        var samples = planeBuffer.Samples;
        var stride = planeBuffer.Stride;
        var blockDecoded = _scratch.GetBlockDecoded(plane);
        if (!_isInter)
        {
            if ((plane is 0 && _paletteSizeY is not 0) || (plane is not 0 && _paletteSizeUv is not 0))
            {
                PredictPalette(samples, stride, plane, startX, startY, x, y, txSz);
            }
            else
            {
                var isCfl = plane > 0 && _uvMode is Av1Constants.UvCflPred;
                int mode;
                if (plane is 0)
                {
                    mode = _yMode;
                }
                else
                {
                    mode = isCfl ? Av1Constants.DcPred : _uvMode;
                }

                var log2W = Av1Tables.TxWidthLog2[txSz];
                var log2H = Av1Tables.TxHeightLog2[txSz];
                var haveLeft = (plane is 0 ? _availL : _availLChroma) || x > 0;
                var haveAbove = (plane is 0 ? _availU : _availUChroma) || y > 0;
                var haveAboveRight = blockDecoded[BlockDecodedIndex((subBlockMiRow >> subY) - 1, (subBlockMiCol >> subX) + stepX)] is not 0;
                var haveBelowLeft = blockDecoded[BlockDecodedIndex((subBlockMiRow >> subY) + stepY, (subBlockMiCol >> subX) - 1)] is not 0;
                var parameters = new Av1IntraPredictor.Parameters(
                    _bitDepth,
                    mode,
                    plane is 0 ? _angleDeltaY : _angleDeltaUv,
                    plane is 0 && _useFilterIntra ? _filterIntraMode : -1,
                    _sequence.EnableIntraEdgeFilter,
                    _sequence.EnableIntraEdgeFilter && IsDirectionalMode(mode) && GetFilterType(plane));
                Av1IntraPredictor.Predict(samples, stride, startX, startY, haveLeft, haveAbove, haveAboveRight, haveBelowLeft, log2W, log2H, maxX - 1, maxY - 1, parameters);
                if (isCfl)
                {
                    var luma = _frame.GetPlane(0);
                    Av1Reconstruction.PredictChromaFromLuma(luma.Samples, luma.Stride, samples, stride, startX, startY, txSz, plane is 1 ? _cflAlphaU : _cflAlphaV, _subX, _subY, _maxLumaW, _maxLumaH, _bitDepth, _scratch.Cfl);
                }
            }

            if (plane is 0)
            {
                _maxLumaW = startX + (stepX * 4);
                _maxLumaH = startY + (stepY * 4);
            }
        }
    }

    // The end of the transform block syntax: the transform size that the loop filter reads, and the blocks that the intra
    // prediction of the following blocks may read
    private readonly void FinishTransformBlock(int plane, int txSz, int startX, int startY)
    {
        var subX = plane > 0 ? _subX : 0;
        var subY = plane > 0 ? _subY : 0;
        var row = (startY << subY) >> Av1Constants.MiSizeLog2;
        var col = (startX << subX) >> Av1Constants.MiSizeLog2;
        var subBlockMiRow = row & _sbMask;
        var subBlockMiCol = col & _sbMask;
        var stepX = Av1Tables.TxWidth[txSz] >> Av1Constants.MiSizeLog2;
        var stepY = Av1Tables.TxHeight[txSz] >> Av1Constants.MiSizeLog2;
        var blockDecoded = _scratch.GetBlockDecoded(plane);
        var loopFilterTxSizes = _state.GetLoopFilterTxSizes(plane);
        var planeMiCols = _miCols >> subX;
        var planeMiRows = _miRows >> subY;
        for (var i = 0; i < stepY; i++)
        {
            var planeRow = (row >> subY) + i;
            for (var j = 0; j < stepX; j++)
            {
                var planeCol = (col >> subX) + j;
                if (planeRow < planeMiRows && planeCol < planeMiCols)
                    loopFilterTxSizes[(planeRow * _miCols) + planeCol] = (byte)txSz;

                blockDecoded[BlockDecodedIndex((subBlockMiRow >> subY) + i, (subBlockMiCol >> subX) + j)] = 1;
            }
        }
    }

    // Section 7.11.2.8 "Intra filter type process"
    private readonly bool GetFilterType(int plane)
    {
        var aboveSmooth = false;
        var leftSmooth = false;
        if (plane is 0 ? _availU : _availUChroma)
        {
            var r = _miRow - 1;
            var c = _miCol;
            if (plane > 0)
            {
                if (_subX is 1 && (_miCol & 1) is 0)
                    c++;

                if (_subY is 1 && (_miRow & 1) is not 0)
                    r--;
            }

            aboveSmooth = IsSmooth(r, c, plane);
        }

        if (plane is 0 ? _availL : _availLChroma)
        {
            var r = _miRow;
            var c = _miCol - 1;
            if (plane > 0)
            {
                if (_subX is 1 && (_miCol & 1) is not 0)
                    c--;

                if (_subY is 1 && (_miRow & 1) is 0)
                    r++;
            }

            leftSmooth = IsSmooth(r, c, plane);
        }

        return aboveSmooth || leftSmooth;
    }

    private readonly bool IsSmooth(int row, int col, int plane)
    {
        // The block that holds the chroma samples of a sub-8x8 luma block may be outside the frame
        if (row >= _miRows || col >= _miCols)
            return false;

        var index = MiIndex(row, col);
        int mode;
        if (plane is 0)
        {
            mode = _yModes[index];
        }
        else
        {
            if ((_flags[index] & Av1FrameState.IsInterFlag) is not 0)
                return false;

            mode = _uvModes[index];
        }

        return mode is Av1Constants.SmoothPred or Av1Constants.SmoothVPred or Av1Constants.SmoothHPred;
    }

    // get_dc_quant( plane ) and get_ac_quant( plane ) for the block (section 7.12.2)
    private readonly void GetQuantizers(int plane, out int dcQuantizer, out int acQuantizer)
    {
        var delta = plane switch
        {
            0 => (_header.DeltaQYDc, 0),
            1 => (_header.DeltaQUDc, _header.DeltaQUAc),
            _ => (_header.DeltaQVDc, _header.DeltaQVAc),
        };

        dcQuantizer = Av1Reconstruction.GetDcQuantizer(_qIndex + delta.Item1, _bitDepth);
        acQuantizer = Av1Reconstruction.GetAcQuantizer(_qIndex + delta.Item2, _bitDepth);
    }

    // Quant holds the coefficients of the eob first positions of the scan order of the transform block; the other entries
    // are not read
    private readonly void Reconstruct(Span<ushort> samples, int stride, int plane, int startX, int startY, int txSz, int eob)
    {
        GetQuantizers(plane, out var dcQuantizer, out var acQuantizer);
        var quantizerMatrix = ReadOnlySpan<byte>.Empty;
        if (_header.UsingQmatrix && !_lossless)
        {
            var level = plane switch
            {
                0 => _header.QmY,
                1 => _header.QmU,
                _ => _header.QmV,
            };

            if (level < 15)
                quantizerMatrix = Av1Tables.QuantizerMatrix[((((level * 2) + (plane > 0 ? 1 : 0)) * Av1Constants.QmTotalSize) + Av1Tables.QmOffset[txSz])..];
        }

        Av1Reconstruction.Reconstruct(samples, stride, startX, startY, _scratch.Quant, GetScan(txSz, _planeTxType), eob, _scratch.Transform, txSz, _planeTxType, _lossless, _bitDepth, dcQuantizer, acQuantizer, quantizerMatrix);
    }

    // Section 5.11.39 "Coefficients syntax"
    private int Coeffs(int plane, int startX, int startY, int txSz)
    {
        var x4 = startX >> 2;
        var y4 = startY >> 2;
        var w4 = Av1Tables.TxWidth[txSz] >> 2;
        var h4 = Av1Tables.TxHeight[txSz] >> 2;
        var txSzCtx = (Av1Tables.TxSizeSqr[txSz] + Av1Tables.TxSizeSqrUp[txSz] + 1) >> 1;
        var ptype = plane > 0 ? 1 : 0;
        var quant = _scratch.Quant;

        var aboveLevel = _scratch.GetAboveLevelContext(plane);
        var aboveDc = _scratch.GetAboveDcContext(plane);
        var leftLevel = _scratch.GetLeftLevelContext(plane);
        var leftDc = _scratch.GetLeftDcContext(plane);
        var maxX4 = plane > 0 ? _miCols >> _subX : _miCols;
        var maxY4 = plane > 0 ? _miRows >> _subY : _miRows;

        var eob = 0;
        var culLevel = 0;
        var dcCategory = 0;
        var allZero = ReadSymbol(Av1CdfLayout.TxbSkip + (((txSzCtx * Av1Constants.TxbSkipContexts) + GetAllZeroContext(plane, txSz, x4, y4, w4, h4, maxX4, maxY4)) * Av1CdfLayout.TxbSkipSize), Av1CdfLayout.TxbSkipSize) is not 0;
        if (allZero)
        {
            if (plane is 0)
                SetTxTypes(x4, y4, w4, h4, Av1Constants.DctDct);
        }
        else
        {
            if (plane is 0)
                TransformType(x4, y4, txSz);

            _planeTxType = ComputeTxType(plane, txSz, x4, y4);
            if (_planeTxType is Av1Constants.Idtx)
                _scratch.Tools |= Av1Tools.IdentityTransform;

            if (Av1Tables.TxSizeSqrUp[txSz] is Av1Constants.Tx64x64)
                _scratch.Tools |= Av1Tools.Transform64;

            var scan = GetScan(txSz, _planeTxType);
            var txClass = GetTxClass(_planeTxType);
            var eobMultisize = Math.Min((int)Av1Tables.TxWidthLog2[txSz], 5) + Math.Min((int)Av1Tables.TxHeightLog2[txSz], 5) - 4;
            var eobCtx = txClass is TxClass2D ? 0 : 1;
            var eobPt = 1 + eobMultisize switch
            {
                0 => ReadSymbol(Av1CdfLayout.EobPt16 + (((ptype * 2) + eobCtx) * Av1CdfLayout.EobPt16Size), Av1CdfLayout.EobPt16Size),
                1 => ReadSymbol(Av1CdfLayout.EobPt32 + (((ptype * 2) + eobCtx) * Av1CdfLayout.EobPt32Size), Av1CdfLayout.EobPt32Size),
                2 => ReadSymbol(Av1CdfLayout.EobPt64 + (((ptype * 2) + eobCtx) * Av1CdfLayout.EobPt64Size), Av1CdfLayout.EobPt64Size),
                3 => ReadSymbol(Av1CdfLayout.EobPt128 + (((ptype * 2) + eobCtx) * Av1CdfLayout.EobPt128Size), Av1CdfLayout.EobPt128Size),
                4 => ReadSymbol(Av1CdfLayout.EobPt256 + (((ptype * 2) + eobCtx) * Av1CdfLayout.EobPt256Size), Av1CdfLayout.EobPt256Size),
                5 => ReadSymbol(Av1CdfLayout.EobPt512 + (ptype * Av1CdfLayout.EobPt512Size), Av1CdfLayout.EobPt512Size),
                _ => ReadSymbol(Av1CdfLayout.EobPt1024 + (ptype * Av1CdfLayout.EobPt1024Size), Av1CdfLayout.EobPt1024Size),
            };

            eob = eobPt < 2 ? eobPt : (1 << (eobPt - 2)) + 1;
            var eobShift = Math.Max(-1, eobPt - 3);
            if (eobShift >= 0)
            {
                var eobExtra = ReadSymbol(Av1CdfLayout.EobExtra + (((((txSzCtx * Av1Constants.PlaneTypes) + ptype) * Av1Constants.EobCoefContexts) + (eobPt - 3)) * Av1CdfLayout.EobExtraSize), Av1CdfLayout.EobExtraSize);
                if (eobExtra is not 0)
                    eob += 1 << eobShift;

                for (var i = 1; i < Math.Max(0, eobPt - 2); i++)
                {
                    eobShift = Math.Max(0, eobPt - 2) - 1 - i;
                    if (ReadLiteral(1) is not 0)
                        eob += 1 << eobShift;
                }
            }

            var adjTxSz = Av1Tables.AdjustedTxSize[txSz];
            var bwl = Av1Tables.TxWidthLog2[adjTxSz];
            var width = 1 << bwl;
            var height = Av1Tables.TxHeight[adjTxSz];
            var coeffBaseOffset = Av1CdfLayout.CoeffBase + ((((txSzCtx * Av1Constants.PlaneTypes) + ptype) * Av1Constants.SigCoefContexts) * Av1CdfLayout.CoeffBaseSize);
            var coeffBaseEobOffset = Av1CdfLayout.CoeffBaseEob + ((((txSzCtx * Av1Constants.PlaneTypes) + ptype) * Av1Constants.SigCoefContextsEob) * Av1CdfLayout.CoeffBaseEobSize);
            var coeffBrOffset = Av1CdfLayout.CoeffBr + ((((Math.Min(txSzCtx, Av1Constants.Tx32x32) * Av1Constants.PlaneTypes) + ptype) * Av1Constants.LevelContexts) * Av1CdfLayout.CoeffBrSize);
            var levelStride = width + Av1TileScratch.LevelMapPadding;
            var levels = _scratch.LevelMap[..((height + Av1TileScratch.LevelMapPadding) * levelStride)];
            levels.Clear();
            for (var c = eob - 1; c >= 0; c--)
            {
                var pos = scan[c];
                var row = pos >> bwl;
                var col = pos & (width - 1);
                int level;
                if (c == eob - 1)
                {
                    var ctx = GetCoeffBaseEobContext(c, width, height);
                    level = ReadSymbol(coeffBaseEobOffset + (ctx * Av1CdfLayout.CoeffBaseEobSize), Av1CdfLayout.CoeffBaseEobSize) + 1;
                }
                else
                {
                    var ctx = GetCoeffBaseContext(levels, txSz, txClass, levelStride, row, col);
                    level = ReadSymbol(coeffBaseOffset + (ctx * Av1CdfLayout.CoeffBaseSize), Av1CdfLayout.CoeffBaseSize);
                }

                if (level > Av1Constants.NumBaseLevels)
                {
                    var ctx = GetCoeffBrContext(levels, txClass, levelStride, row, col);
                    for (var idx = 0; idx < Av1Constants.CoeffBaseRange / (Av1Constants.BrCdfSize - 1); idx++)
                    {
                        var coeffBr = ReadSymbol(coeffBrOffset + (ctx * Av1CdfLayout.CoeffBrSize), Av1CdfLayout.CoeffBrSize);
                        level += coeffBr;
                        if (coeffBr < Av1Constants.BrCdfSize - 1)
                            break;
                    }
                }

                quant[pos] = level;
                levels[(row * levelStride) + col] = (byte)level;
            }

            for (var c = 0; c < eob; c++)
            {
                var pos = scan[c];
                var sign = 0;
                if (quant[pos] is not 0)
                {
                    if (c is 0)
                    {
                        var ctx = GetDcSignContext(aboveDc, leftDc, x4, y4, w4, h4, maxX4, maxY4);
                        sign = ReadSymbol(Av1CdfLayout.DcSign + (((ptype * Av1Constants.DcSignContexts) + ctx) * Av1CdfLayout.DcSignSize), Av1CdfLayout.DcSignSize);
                    }
                    else
                    {
                        sign = ReadLiteral(1);
                    }
                }

                if (quant[pos] > Av1Constants.NumBaseLevels + Av1Constants.CoeffBaseRange)
                {
                    var length = 0;
                    int golombLengthBit;
                    do
                    {
                        length++;
                        golombLengthBit = ReadLiteral(1);
                    }
                    while (golombLengthBit is 0 && length < 32);

                    if (golombLengthBit is 0)
                        throw Av1Errors.Invalid("a coefficient is too large.");

                    var x = 1;
                    for (var i = length - 2; i >= 0; i--)
                    {
                        x = (x << 1) | ReadLiteral(1);
                    }

                    quant[pos] = x + Av1Constants.CoeffBaseRange + Av1Constants.NumBaseLevels;
                }

                if (pos is 0 && quant[pos] > 0)
                    dcCategory = sign is not 0 ? 1 : 2;

                quant[pos] &= 0xFFFFF;
                culLevel += quant[pos];
                if (sign is not 0)
                    quant[pos] = -quant[pos];
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

        return eob;
    }

    // The CDF selection of all_zero (section 8.3.2)
    private readonly int GetAllZeroContext(int plane, int txSz, int x4, int y4, int w4, int h4, int maxX4, int maxY4)
    {
        var aboveLevel = _scratch.GetAboveLevelContext(plane);
        var leftLevel = _scratch.GetLeftLevelContext(plane);
        var w = Av1Tables.TxWidth[txSz];
        var h = Av1Tables.TxHeight[txSz];
        var bsize = GetPlaneResidualSize(_miSize, plane);
        var bw = BlockWidth(bsize);
        var bh = BlockHeight(bsize);
        if (plane is 0)
        {
            var top = 0;
            var left = 0;
            for (var k = 0; k < w4; k++)
            {
                if (x4 + k < maxX4)
                    top = Math.Max(top, aboveLevel[x4 + k]);
            }

            for (var k = 0; k < h4; k++)
            {
                if (y4 + k < maxY4)
                    left = Math.Max(left, leftLevel[(y4 + k) & 31]);
            }

            top = Math.Min(top, 255);
            left = Math.Min(left, 255);
            if (bw == w && bh == h)
                return 0;

            if (top is 0 && left is 0)
                return 1;

            if (top is 0 || left is 0)
                return 2 + (Math.Max(top, left) > 3 ? 1 : 0);

            if (Math.Max(top, left) <= 3)
                return 4;

            return Math.Min(top, left) <= 3 ? 5 : 6;
        }

        var aboveDc = _scratch.GetAboveDcContext(plane);
        var leftDc = _scratch.GetLeftDcContext(plane);
        var above = 0;
        var leftAny = 0;
        for (var i = 0; i < w4; i++)
        {
            if (x4 + i < maxX4)
                above |= aboveLevel[x4 + i] | aboveDc[x4 + i];
        }

        for (var i = 0; i < h4; i++)
        {
            if (y4 + i < maxY4)
                leftAny |= leftLevel[(y4 + i) & 31] | leftDc[(y4 + i) & 31];
        }

        var ctx = (above is not 0 ? 1 : 0) + (leftAny is not 0 ? 1 : 0) + 7;
        if (bw * bh > w * h)
            ctx += 3;

        return ctx;
    }

    // The CDF selection of coeff_base_eob: get_coeff_base_ctx with isEob equal to 1, minus SIG_COEF_CONTEXTS - SIG_COEF_CONTEXTS_EOB
    private static int GetCoeffBaseEobContext(int c, int width, int height)
    {
        if (c is 0)
            return 0;

        if (c <= (height * width) / 8)
            return 1;

        return c <= (height * width) / 4 ? 2 : 3;
    }

    // The CDF selection of coeff_base: get_coeff_base_ctx with isEob equal to 0. The levels coded so far are in the level
    // map, whose padding makes the neighbors outside the block read as zero
    private static int GetCoeffBaseContext(ReadOnlySpan<byte> levels, int txSz, int txClass, int stride, int row, int col)
    {
        var neighbors = levels[((row * stride) + col)..];
        if (txClass is TxClass2D)
        {
            if ((row | col) is 0)
                return 0;

            var mag = Math.Min((int)neighbors[1], 3) + Math.Min((int)neighbors[stride], 3) + Math.Min((int)neighbors[stride + 1], 3) + Math.Min((int)neighbors[2], 3) + Math.Min((int)neighbors[2 * stride], 3);
            return Math.Min((mag + 1) >> 1, 4) + Av1Tables.CoeffBaseCtxOffset[(((txSz * 5) + Math.Min(row, 4)) * 5) + Math.Min(col, 4)];
        }

        if (txClass is Av1Constants.TxClassHoriz)
        {
            var mag = Math.Min((int)neighbors[1], 3) + Math.Min((int)neighbors[stride], 3) + Math.Min((int)neighbors[2], 3) + Math.Min((int)neighbors[3], 3) + Math.Min((int)neighbors[4], 3);
            return Math.Min((mag + 1) >> 1, 4) + Av1Tables.CoeffBasePosCtxOffset[Math.Min(col, 2)];
        }
        else
        {
            var mag = Math.Min((int)neighbors[1], 3) + Math.Min((int)neighbors[stride], 3) + Math.Min((int)neighbors[2 * stride], 3) + Math.Min((int)neighbors[3 * stride], 3) + Math.Min((int)neighbors[4 * stride], 3);
            return Math.Min((mag + 1) >> 1, 4) + Av1Tables.CoeffBasePosCtxOffset[Math.Min(row, 2)];
        }
    }

    // The CDF selection of coeff_br. The levels of the map are at most NUM_BASE_LEVELS + COEFF_BASE_RANGE + 1
    private static int GetCoeffBrContext(ReadOnlySpan<byte> levels, int txClass, int stride, int row, int col)
    {
        var neighbors = levels[((row * stride) + col)..];
        var mag = neighbors[1] + neighbors[stride] + txClass switch
        {
            TxClass2D => neighbors[stride + 1],
            Av1Constants.TxClassHoriz => neighbors[2],
            _ => neighbors[2 * stride],
        };

        mag = Math.Min((mag + 1) >> 1, 6);
        if ((row | col) is 0)
            return mag;

        return txClass switch
        {
            TxClass2D => row < 2 && col < 2 ? mag + 7 : mag + 14,
            Av1Constants.TxClassHoriz => col is 0 ? mag + 7 : mag + 14,
            _ => row is 0 ? mag + 7 : mag + 14,
        };
    }

    // The CDF selection of dc_sign
    private static int GetDcSignContext(ReadOnlySpan<byte> aboveDc, ReadOnlySpan<byte> leftDc, int x4, int y4, int w4, int h4, int maxX4, int maxY4)
    {
        var dcSign = 0;
        for (var k = 0; k < w4; k++)
        {
            if (x4 + k < maxX4)
            {
                var sign = aboveDc[x4 + k];
                if (sign is 1)
                {
                    dcSign--;
                }
                else if (sign is 2)
                {
                    dcSign++;
                }
            }
        }

        for (var k = 0; k < h4; k++)
        {
            if (y4 + k < maxY4)
            {
                var sign = leftDc[(y4 + k) & 31];
                if (sign is 1)
                {
                    dcSign--;
                }
                else if (sign is 2)
                {
                    dcSign++;
                }
            }
        }

        if (dcSign < 0)
            return 1;

        return dcSign > 0 ? 2 : 0;
    }

    private static int GetTxClass(int txType) => txType switch
    {
        Av1Constants.VDct or Av1Constants.VAdst or Av1Constants.VFlipadst => Av1Constants.TxClassVert,
        Av1Constants.HDct or Av1Constants.HAdst or Av1Constants.HFlipadst => Av1Constants.TxClassHoriz,
        _ => TxClass2D,
    };

    private readonly void SetTxTypes(int x4, int y4, int w4, int h4, int txType)
    {
        var txTypes = _scratch.TxTypes;
        for (var j = 0; j < h4; j++)
        {
            for (var i = 0; i < w4; i++)
            {
                txTypes[(((y4 + j) & 31) * Av1TileScratch.SuperblockSize4) + ((x4 + i) & 31)] = (byte)txType;
            }
        }
    }

    // Section 5.11.47 "Transform type syntax"
    private readonly void TransformType(int x4, int y4, int txSz)
    {
        var set = GetTxSet(txSz);
        var txType = Av1Constants.DctDct;
        var qIndex = _header.SegmentationEnabled ? _header.GetQIndex(_segmentId, currentQIndex: -1) : _header.BaseQIdx;
        if (set > 0 && qIndex > 0)
        {
            var txSzSqr = Av1Tables.TxSizeSqr[txSz];
            if (_isInter)
            {
                if (set is Av1Constants.TxSetInter1)
                {
                    txType = Av1Tables.TxTypeInterInvSet1[ReadSymbol(Av1CdfLayout.InterTxTypeSet1 + (txSzSqr * Av1CdfLayout.InterTxTypeSet1Size), Av1CdfLayout.InterTxTypeSet1Size)];
                }
                else if (set is Av1Constants.TxSetInter2)
                {
                    txType = Av1Tables.TxTypeInterInvSet2[ReadSymbol(Av1CdfLayout.InterTxTypeSet2, Av1CdfLayout.InterTxTypeSet2Size)];
                }
                else
                {
                    txType = Av1Tables.TxTypeInterInvSet3[ReadSymbol(Av1CdfLayout.InterTxTypeSet3 + (txSzSqr * Av1CdfLayout.InterTxTypeSet3Size), Av1CdfLayout.InterTxTypeSet3Size)];
                }
            }
            else
            {
                var intraDir = _useFilterIntra ? Av1Tables.FilterIntraModeToIntraDir[_filterIntraMode] : _yMode;
                if (set is Av1Constants.TxSetIntra1)
                {
                    txType = Av1Tables.TxTypeIntraInvSet1[ReadSymbol(Av1CdfLayout.IntraTxTypeSet1 + (((txSzSqr * Av1Constants.IntraModes) + intraDir) * Av1CdfLayout.IntraTxTypeSet1Size), Av1CdfLayout.IntraTxTypeSet1Size)];
                }
                else
                {
                    txType = Av1Tables.TxTypeIntraInvSet2[ReadSymbol(Av1CdfLayout.IntraTxTypeSet2 + (((txSzSqr * Av1Constants.IntraModes) + intraDir) * Av1CdfLayout.IntraTxTypeSet2Size), Av1CdfLayout.IntraTxTypeSet2Size)];
                }
            }
        }

        SetTxTypes(x4, y4, Av1Tables.TxWidth[txSz] >> 2, Av1Tables.TxHeight[txSz] >> 2, txType);
    }

    // Section 5.11.48 "Get transform set function"
    private readonly int GetTxSet(int txSz)
    {
        var txSzSqr = Av1Tables.TxSizeSqr[txSz];
        var txSzSqrUp = Av1Tables.TxSizeSqrUp[txSz];
        if (txSzSqrUp > Av1Constants.Tx32x32)
            return Av1Constants.TxSetDctonly;

        if (_isInter)
        {
            if (_header.ReducedTxSet || txSzSqrUp is Av1Constants.Tx32x32)
                return Av1Constants.TxSetInter3;

            return txSzSqr is Av1Constants.Tx16x16 ? Av1Constants.TxSetInter2 : Av1Constants.TxSetInter1;
        }

        if (txSzSqrUp is Av1Constants.Tx32x32)
            return Av1Constants.TxSetDctonly;

        if (_header.ReducedTxSet || txSzSqr is Av1Constants.Tx16x16)
            return Av1Constants.TxSetIntra2;

        return Av1Constants.TxSetIntra1;
    }

    // Section 5.11.40 "Compute transform type function"
    private readonly int ComputeTxType(int plane, int txSz, int blockX, int blockY)
    {
        var txSzSqrUp = Av1Tables.TxSizeSqrUp[txSz];
        if (_lossless || txSzSqrUp > Av1Constants.Tx32x32)
            return Av1Constants.DctDct;

        var txSet = GetTxSet(txSz);
        var txTypes = _scratch.TxTypes;
        if (plane is 0)
            return txTypes[((blockY & 31) * Av1TileScratch.SuperblockSize4) + (blockX & 31)];

        int txType;
        if (_isInter)
        {
            var x4 = Math.Max(_miCol, blockX << _subX);
            var y4 = Math.Max(_miRow, blockY << _subY);
            txType = txTypes[((y4 & 31) * Av1TileScratch.SuperblockSize4) + (x4 & 31)];
        }
        else
        {
            txType = Av1Tables.ModeToTxfm[_uvMode];
        }

        var inSet = _isInter
            ? Av1Tables.TxTypeInSetInter[(txSet * Av1Constants.TxTypes) + txType]
            : Av1Tables.TxTypeInSetIntra[(txSet * Av1Constants.TxTypes) + txType];
        return inSet is not 0 ? txType : Av1Constants.DctDct;
    }

    // Section 5.11.41 "Get scan function"
    private static Av1Scan GetScan(int txSz, int planeTxType)
    {
        if (txSz is Av1Constants.Tx16x64)
            return new Av1Scan(Av1Tables.DefaultScan16x32);

        if (txSz is Av1Constants.Tx64x16)
            return new Av1Scan(Av1Tables.DefaultScan32x16);

        if (Av1Tables.TxSizeSqrUp[txSz] is Av1Constants.Tx64x64)
            return new Av1Scan(Av1Tables.DefaultScan32x32);

        if (planeTxType is Av1Constants.Idtx)
            return GetDefaultScan(txSz);

        var preferRow = planeTxType is Av1Constants.VDct or Av1Constants.VAdst or Av1Constants.VFlipadst;
        var preferCol = planeTxType is Av1Constants.HDct or Av1Constants.HAdst or Av1Constants.HFlipadst;
        if (preferRow)
        {
            return new Av1Scan(txSz switch
            {
                Av1Constants.Tx4x4 => Av1Tables.MrowScan4x4,
                Av1Constants.Tx4x8 => Av1Tables.MrowScan4x8,
                Av1Constants.Tx8x4 => Av1Tables.MrowScan8x4,
                Av1Constants.Tx8x8 => Av1Tables.MrowScan8x8,
                Av1Constants.Tx8x16 => Av1Tables.MrowScan8x16,
                Av1Constants.Tx16x8 => Av1Tables.MrowScan16x8,
                Av1Constants.Tx16x16 => Av1Tables.MrowScan16x16,
                Av1Constants.Tx4x16 => Av1Tables.MrowScan4x16,
                _ => Av1Tables.MrowScan16x4,
            });
        }

        if (preferCol)
        {
            return new Av1Scan(txSz switch
            {
                Av1Constants.Tx4x4 => Av1Tables.McolScan4x4,
                Av1Constants.Tx4x8 => Av1Tables.McolScan4x8,
                Av1Constants.Tx8x4 => Av1Tables.McolScan8x4,
                Av1Constants.Tx8x8 => Av1Tables.McolScan8x8,
                Av1Constants.Tx8x16 => Av1Tables.McolScan8x16,
                Av1Constants.Tx16x8 => Av1Tables.McolScan16x8,
                Av1Constants.Tx16x16 => Av1Tables.McolScan16x16,
                Av1Constants.Tx4x16 => Av1Tables.McolScan4x16,
                _ => Av1Tables.McolScan16x4,
            });
        }

        return GetDefaultScan(txSz);
    }

    private static Av1Scan GetDefaultScan(int txSz) => txSz switch
    {
        Av1Constants.Tx4x4 => new Av1Scan(Av1Tables.DefaultScan4x4),
        Av1Constants.Tx4x8 => new Av1Scan(Av1Tables.DefaultScan4x8),
        Av1Constants.Tx8x4 => new Av1Scan(Av1Tables.DefaultScan8x4),
        Av1Constants.Tx8x8 => new Av1Scan(Av1Tables.DefaultScan8x8),
        Av1Constants.Tx8x16 => new Av1Scan(Av1Tables.DefaultScan8x16),
        Av1Constants.Tx16x8 => new Av1Scan(Av1Tables.DefaultScan16x8),
        Av1Constants.Tx16x16 => new Av1Scan(Av1Tables.DefaultScan16x16),
        Av1Constants.Tx16x32 => new Av1Scan(Av1Tables.DefaultScan16x32),
        Av1Constants.Tx32x16 => new Av1Scan(Av1Tables.DefaultScan32x16),
        Av1Constants.Tx4x16 => new Av1Scan(Av1Tables.DefaultScan4x16),
        Av1Constants.Tx16x4 => new Av1Scan(Av1Tables.DefaultScan16x4),
        Av1Constants.Tx8x32 => new Av1Scan(Av1Tables.DefaultScan8x32),
        Av1Constants.Tx32x8 => new Av1Scan(Av1Tables.DefaultScan32x8),
        _ => new Av1Scan(Av1Tables.DefaultScan32x32),
    };
}
