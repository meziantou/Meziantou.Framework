namespace Meziantou.Framework.Imaging.Internals;

// The syntax of a coding tree unit down to the prediction modes of its coding units: sample adaptive offset parameters,
// coding quadtree, coding unit, PCM samples and intra prediction modes
internal ref partial struct HevcSliceCoder
{
    // Clause 7.3.8.3 "Sample adaptive offset syntax" and clause 7.4.9.3 "Sample adaptive offset semantics"
    private void ReadSao(int rx, int ry)
    {
        var sao = _state.Sao.Slice(_ctbAddrInRs * HevcPictureState.SaoValuesPerCtb, HevcPictureState.SaoValuesPerCtb);
        var saoMergeLeftFlag = false;
        var saoMergeUpFlag = false;
        if (rx > 0)
        {
            var leftCtbInSliceSeg = _ctbAddrInRs > _header.SliceAddrRs;
            var leftCtbInTile = _tileId[_ctbAddrInTs] == _tileId[_ctbAddrRsToTs[_ctbAddrInRs - 1]];
            if (leftCtbInSliceSeg && leftCtbInTile)
            {
                saoMergeLeftFlag = _cabac.DecodeDecision(HevcContextLayout.SaoMergeFlag) is not 0;
            }
        }

        if (ry > 0 && !saoMergeLeftFlag)
        {
            var upCtbInSliceSeg = _ctbAddrInRs - _picWidthInCtbsY >= _header.SliceAddrRs;
            var upCtbInTile = _tileId[_ctbAddrInTs] == _tileId[_ctbAddrRsToTs[_ctbAddrInRs - _picWidthInCtbsY]];
            if (upCtbInSliceSeg && upCtbInTile)
            {
                saoMergeUpFlag = _cabac.DecodeDecision(HevcContextLayout.SaoMergeFlag) is not 0;
            }
        }

        if (saoMergeLeftFlag || saoMergeUpFlag)
        {
            // Every syntax element is the one of the coding tree block to the left or above
            var source = saoMergeLeftFlag ? _ctbAddrInRs - 1 : _ctbAddrInRs - _picWidthInCtbsY;
            _state.Sao.Slice(source * HevcPictureState.SaoValuesPerCtb, HevcPictureState.SaoValuesPerCtb).CopyTo(sao);
            _tools |= HevcTools.SaoMerge;
            return;
        }

        sao.Clear();
        var cMax = (1 << (Math.Min(_bitDepth, 10) - 5)) - 1;
        for (var cIdx = 0; cIdx < (_chromaArrayType is not 0 ? 3 : 1); cIdx++)
        {
            if (!(cIdx is 0 ? _header.SliceSaoLumaFlag : _header.SliceSaoChromaFlag))
                continue;

            var values = sao.Slice(cIdx * 6, 6);
            if (cIdx is 2)
            {
                // sao_type_idx_chroma and sao_eo_class_chroma are those of both chroma components
                values[0] = sao[6];
            }
            else if (_cabac.DecodeDecision(HevcContextLayout.SaoTypeIdx) is not 0)
            {
                // sao_type_idx_luma, sao_type_idx_chroma: a truncated Rice binarization with cMax equal to 2
                values[0] = (sbyte)(1 + _cabac.DecodeBypass());
            }

            if (values[0] is 0)
                continue;

            for (var i = 0; i < 4; i++)
            {
                // sao_offset_abs: a truncated unary binarization of bypass bins
                var saoOffsetAbs = 0;
                while (saoOffsetAbs < cMax && _cabac.DecodeBypass() is not 0)
                {
                    saoOffsetAbs++;
                }

                values[2 + i] = (sbyte)saoOffsetAbs;
            }

            if (values[0] is 1)
            {
                for (var i = 0; i < 4; i++)
                {
                    if (values[2 + i] is not 0 && _cabac.DecodeBypass() is not 0)
                    {
                        // sao_offset_sign
                        values[2 + i] = (sbyte)-values[2 + i];
                    }
                }

                // sao_band_position
                values[1] = (sbyte)_cabac.DecodeBypassBits(5);
                _tools |= HevcTools.SaoBandOffset;
            }
            else
            {
                // sao_eo_class_luma, sao_eo_class_chroma
                values[1] = cIdx is 2 ? sao[7] : (sbyte)_cabac.DecodeBypassBits(2);

                // sao_offset_sign is inferred: 0 for the first two offsets, 1 for the last two
                values[4] = (sbyte)-values[4];
                values[5] = (sbyte)-values[5];
                _tools |= HevcTools.SaoEdgeOffset;
            }
        }
    }

    // Clause 7.3.8.4 "Coding quadtree syntax"
    private void CodingQuadtree(int x0, int y0, int log2CbSize, int cqtDepth)
    {
        var size = 1 << log2CbSize;
        bool splitCuFlag;
        if (x0 + size <= _sps.PicWidthInLumaSamples && y0 + size <= _sps.PicHeightInLumaSamples && log2CbSize > _minCbLog2SizeY)
        {
            splitCuFlag = ReadSplitCuFlag(x0, y0, cqtDepth);
        }
        else
        {
            // Clause 7.4.9.4: a block that is not a whole block of the picture is split down to the minimum size
            splitCuFlag = log2CbSize > _minCbLog2SizeY;
        }

        if (_pps.CuQpDeltaEnabledFlag && log2CbSize >= _layout.Log2MinCuQpDeltaSize)
        {
            BeginQuantizationGroup(x0, y0);
        }

        if (_header.CuChromaQpOffsetEnabledFlag && log2CbSize >= _layout.Log2MinCuChromaQpOffsetSize)
        {
            _isCuChromaQpOffsetCoded = false;
        }

        if (splitCuFlag)
        {
            var x1 = x0 + (size >> 1);
            var y1 = y0 + (size >> 1);
            CodingQuadtree(x0, y0, log2CbSize - 1, cqtDepth + 1);
            if (x1 < _sps.PicWidthInLumaSamples)
            {
                CodingQuadtree(x1, y0, log2CbSize - 1, cqtDepth + 1);
            }

            if (y1 < _sps.PicHeightInLumaSamples)
            {
                CodingQuadtree(x0, y1, log2CbSize - 1, cqtDepth + 1);
            }

            if (x1 < _sps.PicWidthInLumaSamples && y1 < _sps.PicHeightInLumaSamples)
            {
                CodingQuadtree(x1, y1, log2CbSize - 1, cqtDepth + 1);
            }
        }
        else
        {
            CodingUnit(x0, y0, log2CbSize, cqtDepth);
        }
    }

    // split_cu_flag: clause 9.3.4.2.2 "Derivation process of ctxInc using left and above syntax elements"
    private bool ReadSplitCuFlag(int x0, int y0, int cqtDepth)
    {
        return _cabac.DecodeDecision(HevcContextLayout.SplitCuFlag + GetSplitCuFlagCtxInc(x0, y0, cqtDepth)) is not 0;
    }

    private readonly int GetSplitCuFlagCtxInc(int x0, int y0, int cqtDepth)
    {
        var ctxInc = 0;
        if (IsAvailable(x0, y0, x0 - 1, y0) && GetCtDepth(x0 - 1, y0) > cqtDepth)
        {
            ctxInc++;
        }

        if (IsAvailable(x0, y0, x0, y0 - 1) && GetCtDepth(x0, y0 - 1) > cqtDepth)
        {
            ctxInc++;
        }

        return ctxInc;
    }

    private readonly int GetCtDepth(int x, int y) => _cuFlags[((y >> _minCbLog2SizeY) * _widthInMinCbs) + (x >> _minCbLog2SizeY)] >> HevcPictureState.CuDepthShift;

    private readonly byte GetCuFlags(int x, int y) => _cuFlags[((y >> _minCbLog2SizeY) * _widthInMinCbs) + (x >> _minCbLog2SizeY)];

    // Clause 7.3.8.5 "Coding unit syntax"
    private void CodingUnit(int x0, int y0, int log2CbSize, int cqtDepth)
    {
        var nCbS = 1 << log2CbSize;
        _log2CbSize = log2CbSize;
        _xCb = x0;
        _yCb = y0;
        _ctDepth = cqtDepth;
        _cuTransquantBypassFlag = _pps.TransquantBypassEnabledFlag && _cabac.DecodeDecision(HevcContextLayout.CuTransquantBypassFlag) is not 0;

        // cu_skip_flag and pred_mode_flag are those of the coding units of the P and B slices
        var isInterSlice = _refs is not null;
        var skip = isInterSlice && ReadCuSkipFlag(x0, y0);
        _isIntra = !skip && (!isInterSlice || _cabac.DecodeDecision(HevcContextLayout.PredModeFlag) is not 0);
        var flags = (byte)(cqtDepth << HevcPictureState.CuDepthShift);
        if (_isIntra)
        {
            flags |= HevcPictureState.CuIntra;
            _tools |= isInterSlice ? HevcTools.IntraInInterSlices : HevcTools.None;
        }
        else if (skip)
        {
            flags |= HevcPictureState.CuSkip;
        }

        if (_cuTransquantBypassFlag)
        {
            flags |= HevcPictureState.CuTransquantBypass;
            _tools |= HevcTools.TransquantBypass;
        }

        // part_mode (Table 9-45): one bin for a coding unit coded in intra prediction mode
        var partMode = HevcConstants.Part2Nx2N;
        if (!_isIntra)
        {
            partMode = skip ? HevcConstants.Part2Nx2N : ReadPartModeInter(log2CbSize);
        }
        else if (log2CbSize == _minCbLog2SizeY && _cabac.DecodeDecision(HevcContextLayout.PartMode) is 0)
        {
            partMode = HevcConstants.PartNxN;
            _tools |= HevcTools.IntraNxN;
        }

        _partMode = partMode;
        _intraSplitFlag = _isIntra && partMode is HevcConstants.PartNxN ? 1 : 0;
        _interSplitFlag = 0;
        var pcmFlag = false;
        if (_isIntra && partMode is HevcConstants.Part2Nx2N && _sps.PcmEnabledFlag && log2CbSize >= _sps.Log2MinIpcmCbSizeY && log2CbSize <= _sps.Log2MaxIpcmCbSizeY)
        {
            pcmFlag = _cabac.DecodeTerminate() is not 0;
        }

        if (pcmFlag)
        {
            flags |= HevcPictureState.CuPcm;
        }

        // The coding unit is known to the syntax elements and to the processes that follow: the modes of its own blocks
        // are neighbours of each other
        SetCodingUnit(x0, y0, log2CbSize, flags);
        MarkTransformEdges(x0, y0, log2CbSize);
        UpdateQpY(x0, y0);
        if (!_isIntra)
        {
            var mergeFlag = DecodePredictionUnits(x0, y0, nCbS, partMode, skip);
            if (skip)
                return;

            // rqt_root_cbf is 1 for a coding unit whose only prediction unit is merged, which would have been skipped
            // without a residual
            if (!(partMode is HevcConstants.Part2Nx2N && mergeFlag) && _cabac.DecodeDecision(HevcContextLayout.RqtRootCbf) is 0)
                return;

            // Clause 7.4.9.8: interSplitFlag
            _maxTrafoDepth = _sps.MaxTransformHierarchyDepthInter;
            _interSplitFlag = _maxTrafoDepth is 0 && partMode is not HevcConstants.Part2Nx2N ? 1 : 0;
            TransformTree(x0, y0, x0, y0, log2CbSize, 0, 0, parentCbfCb: 3, parentCbfCr: 3);
            return;
        }

        if (pcmFlag)
        {
            ReadPcmSamples(x0, y0, log2CbSize);
            SetIntraPredModeY(x0, y0, nCbS, HevcConstants.IntraDc);
            _tools |= HevcTools.Pcm;
            return;
        }

        ReadIntraPredictionModes(x0, y0, nCbS, partMode);

        // rqt_root_cbf is 1 for a coding unit coded in intra prediction mode
        _maxTrafoDepth = _sps.MaxTransformHierarchyDepthIntra + _intraSplitFlag;
        TransformTree(x0, y0, x0, y0, log2CbSize, 0, 0, parentCbfCb: 3, parentCbfCr: 3);
    }

    private readonly void SetCodingUnit(int x0, int y0, int log2CbSize, byte flags)
    {
        var count = 1 << (log2CbSize - _minCbLog2SizeY);
        var index = ((y0 >> _minCbLog2SizeY) * _widthInMinCbs) + (x0 >> _minCbLog2SizeY);
        for (var y = 0; y < count; y++)
        {
            _cuFlags.Slice(index + (y * _widthInMinCbs), count).Fill(flags);
        }
    }

    private readonly void SetIntraPredModeY(int x, int y, int size, int mode)
    {
        var count = size >> 2;
        var index = ((y >> 2) * _widthIn4x4) + (x >> 2);
        for (var i = 0; i < count; i++)
        {
            _intraPredModeY.Slice(index + (i * _widthIn4x4), count).Fill((byte)mode);
        }
    }

    // The edges of a block that are edges of a transform block for the deblocking filter (clause 8.7.2.2)
    private readonly void MarkTransformEdges(int x0, int y0, int log2Size)
    {
        var count = 1 << (log2Size - 2);
        var index = ((y0 >> 2) * _widthIn4x4) + (x0 >> 2);
        for (var i = 0; i < count; i++)
        {
            _edgeFlags[index + (i * _widthIn4x4)] |= HevcPictureState.EdgeLeftTransform;
            _edgeFlags[index + i] |= HevcPictureState.EdgeTopTransform;
        }
    }

    // Clause 7.3.8.7 "PCM sample syntax" and Equations 8-12, 8-15 and 8-16
    private void ReadPcmSamples(int x0, int y0, int log2CbSize)
    {
        // pcm_alignment_zero_bit, then the samples, which are not arithmetic coded
        var position = ReadAlignmentZeroBits();
        var reader = new HevcBitReader(_rbsp.Array.AsSpan(position, _end - position));
        var nCbS = 1 << log2CbSize;
        ReadPcmPlane(ref reader, _picture.GetPlane(0), x0, y0, nCbS, nCbS, _sps.PcmBitDepthY);
        if (_chromaArrayType is not 0)
        {
            var width = nCbS / _sps.SubWidthC;
            var height = nCbS / _sps.SubHeightC;
            ReadPcmPlane(ref reader, _picture.GetPlane(1), x0 / _sps.SubWidthC, y0 / _sps.SubHeightC, width, height, _sps.PcmBitDepthC);
            ReadPcmPlane(ref reader, _picture.GetPlane(2), x0 / _sps.SubWidthC, y0 / _sps.SubHeightC, width, height, _sps.PcmBitDepthC);
        }

        // Clause 9.3.1: the arithmetic decoding engine is initialized after the PCM samples
        _cabac.Initialize(_rbsp.Array, position + reader.BytePosition, _end);
    }

    private readonly void ReadPcmPlane(ref HevcBitReader reader, Av1Plane plane, int x0, int y0, int width, int height, int pcmBitDepth)
    {
        var samples = plane.Samples;
        var shift = _bitDepth - pcmBitDepth;
        for (var y = 0; y < height; y++)
        {
            var row = samples.Slice(((y0 + y) * plane.Stride) + x0, width);
            for (var x = 0; x < width; x++)
            {
                row[x] = (ushort)(reader.ReadBits(pcmBitDepth) << shift);
            }
        }
    }

    // The intra prediction modes of the coding unit syntax, and clauses 8.4.2 "Derivation process for luma intra prediction
    // mode" and 8.4.3 "Derivation process for chroma intra prediction mode"
    private void ReadIntraPredictionModes(int x0, int y0, int nCbS, int partMode)
    {
        var count = partMode is HevcConstants.PartNxN ? 4 : 1;
        var pbSize = partMode is HevcConstants.PartNxN ? nCbS >> 1 : nCbS;
        Span<bool> prevIntraLumaPredFlag = stackalloc bool[4];
        for (var i = 0; i < count; i++)
        {
            prevIntraLumaPredFlag[i] = _cabac.DecodeDecision(HevcContextLayout.PrevIntraLumaPredFlag) is not 0;
        }

        Span<int> candModeList = stackalloc int[3];
        for (var i = 0; i < count; i++)
        {
            var xPb = x0 + ((i & 1) * pbSize);
            var yPb = y0 + ((i >> 1) * pbSize);
            DeriveCandModeList(xPb, yPb, candModeList);
            int intraPredModeY;
            if (prevIntraLumaPredFlag[i])
            {
                // mpm_idx: a truncated Rice binarization with cMax equal to 2, of bypass bins
                var mpmIdx = _cabac.DecodeBypass();
                if (mpmIdx is not 0)
                {
                    mpmIdx += _cabac.DecodeBypass();
                }

                intraPredModeY = candModeList[mpmIdx];
            }
            else
            {
                // rem_intra_luma_pred_mode, then Equations 8-29 to 8-31 and the steps that follow them
                intraPredModeY = _cabac.DecodeBypassBits(5);
                if (candModeList[0] > candModeList[1])
                {
                    (candModeList[0], candModeList[1]) = (candModeList[1], candModeList[0]);
                }

                if (candModeList[0] > candModeList[2])
                {
                    (candModeList[0], candModeList[2]) = (candModeList[2], candModeList[0]);
                }

                if (candModeList[1] > candModeList[2])
                {
                    (candModeList[1], candModeList[2]) = (candModeList[2], candModeList[1]);
                }

                for (var k = 0; k < 3; k++)
                {
                    if (intraPredModeY >= candModeList[k])
                    {
                        intraPredModeY++;
                    }
                }
            }

            SetIntraPredModeY(xPb, yPb, pbSize, intraPredModeY);
        }

        _intraChromaPredMode = 0;
        _intraPredModeC = 0;
        if (_chromaArrayType is 0)
            return;

        var chromaCount = _chromaArrayType is 3 ? count : 1;
        for (var i = 0; i < chromaCount; i++)
        {
            // intra_chroma_pred_mode (Table 9-46)
            var intraChromaPredMode = 4;
            if (_cabac.DecodeDecision(HevcContextLayout.IntraChromaPredMode) is not 0)
            {
                intraChromaPredMode = _cabac.DecodeBypassBits(2);
            }

            // Table 8-2 - Specification of modeIdx
            var xPb = x0 + ((i & 1) * pbSize);
            var yPb = y0 + ((i >> 1) * pbSize);
            int intraPredModeY = _intraPredModeY[((yPb >> 2) * _widthIn4x4) + (xPb >> 2)];
            var intraPredModeC = GetIntraPredModeC(intraChromaPredMode, intraPredModeY);
            _intraChromaPredMode |= (uint)intraChromaPredMode << (i * 8);
            _intraPredModeC |= (uint)intraPredModeC << (i * 8);
        }

        if (chromaCount is 1)
        {
            // One mode for the coding unit
            _intraChromaPredMode *= 0x01010101;
            _intraPredModeC *= 0x01010101;
        }
    }

    // Clause 8.4.3: IntraPredModeC from intra_chroma_pred_mode and the luma intra prediction mode (Table 8-2), then the
    // mode of the 4:2:2 chroma format (Table 8-3)
    private readonly int GetIntraPredModeC(int intraChromaPredMode, int intraPredModeY)
    {
        var modeIdx = intraChromaPredMode switch
        {
            0 => HevcConstants.IntraPlanar,
            1 => HevcConstants.IntraAngular26,
            2 => HevcConstants.IntraAngular10,
            3 => HevcConstants.IntraDc,
            _ => intraPredModeY,
        };
        if (intraChromaPredMode is not 4 && modeIdx == intraPredModeY)
        {
            modeIdx = HevcConstants.IntraAngular34;
        }

        return _chromaArrayType is 2 ? HevcIntraTables.Mode422[modeIdx] : modeIdx;
    }

    // Clause 8.4.2, steps 1 to 3
    private readonly void DeriveCandModeList(int xPb, int yPb, Span<int> candModeList)
    {
        var candIntraPredModeA = GetCandIntraPredMode(xPb, yPb, xPb - 1, yPb);

        // The mode of a block of the coding tree block above is not kept
        var candIntraPredModeB = yPb - 1 < ((yPb >> _ctbLog2SizeY) << _ctbLog2SizeY) ? HevcConstants.IntraDc : GetCandIntraPredMode(xPb, yPb, xPb, yPb - 1);
        if (candIntraPredModeB == candIntraPredModeA)
        {
            if (candIntraPredModeA < 2)
            {
                // Equations 8-21 to 8-23
                candModeList[0] = HevcConstants.IntraPlanar;
                candModeList[1] = HevcConstants.IntraDc;
                candModeList[2] = HevcConstants.IntraAngular26;
            }
            else
            {
                // Equations 8-24 to 8-26
                candModeList[0] = candIntraPredModeA;
                candModeList[1] = 2 + ((candIntraPredModeA + 29) % 32);
                candModeList[2] = 2 + ((candIntraPredModeA - 2 + 1) % 32);
            }
        }
        else
        {
            // Equations 8-27 and 8-28
            candModeList[0] = candIntraPredModeA;
            candModeList[1] = candIntraPredModeB;
            if (candIntraPredModeA is not HevcConstants.IntraPlanar && candIntraPredModeB is not HevcConstants.IntraPlanar)
            {
                candModeList[2] = HevcConstants.IntraPlanar;
            }
            else if (candIntraPredModeA is not HevcConstants.IntraDc && candIntraPredModeB is not HevcConstants.IntraDc)
            {
                candModeList[2] = HevcConstants.IntraDc;
            }
            else
            {
                candModeList[2] = HevcConstants.IntraAngular26;
            }
        }
    }

    private readonly int GetCandIntraPredMode(int xPb, int yPb, int xNb, int yNb)
    {
        if (!IsAvailable(xPb, yPb, xNb, yNb))
            return HevcConstants.IntraDc;

        var flags = GetCuFlags(xNb, yNb);
        if ((flags & HevcPictureState.CuIntra) is 0 || (flags & HevcPictureState.CuPcm) is not 0)
            return HevcConstants.IntraDc;

        return _intraPredModeY[((yNb >> 2) * _widthIn4x4) + (xNb >> 2)];
    }
}
