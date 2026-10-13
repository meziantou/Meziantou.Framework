namespace Meziantou.Framework.Imaging.Internals;

// The transform tree of a coding unit, its transform units, and the quantization parameters
internal ref partial struct HevcSliceCoder
{
    // Clause 7.3.8.8 "Transform tree syntax". A chroma coded block flag has one bit for each of the two chroma blocks of a
    // 4:2:2 transform unit: the bit 0 for the first one, the only one of the other chroma formats.
    private void TransformTree(int x0, int y0, int xBase, int yBase, int log2TrafoSize, int trafoDepth, int blkIdx, int parentCbfCb, int parentCbfCr)
    {
        bool splitTransformFlag;
        if (log2TrafoSize <= _sps.MaxTbLog2SizeY && log2TrafoSize > _minTbLog2SizeY && trafoDepth < _maxTrafoDepth && !(_intraSplitFlag is 1 && trafoDepth is 0))
        {
            splitTransformFlag = _cabac.DecodeDecision(HevcContextLayout.SplitTransformFlag + 5 - log2TrafoSize) is not 0;
        }
        else
        {
            // Clause 7.4.9.8
            splitTransformFlag = log2TrafoSize > _sps.MaxTbLog2SizeY || (_intraSplitFlag is 1 && trafoDepth is 0) || (_interSplitFlag is 1 && trafoDepth is 0);
        }

        if (splitTransformFlag && !_isIntra)
        {
            _tools |= HevcTools.InterTransformSplit;
        }

        var cbfCb = 0;
        var cbfCr = 0;
        if ((log2TrafoSize > 2 && _chromaArrayType is not 0) || _chromaArrayType is 3)
        {
            var two = _chromaArrayType is 2 && (!splitTransformFlag || log2TrafoSize is 3);
            if (trafoDepth is 0 || parentCbfCb is not 0)
            {
                cbfCb = _cabac.DecodeDecision(HevcContextLayout.CbfChroma + trafoDepth);
                if (two)
                {
                    cbfCb |= _cabac.DecodeDecision(HevcContextLayout.CbfChroma + trafoDepth) << 1;
                }
            }

            if (trafoDepth is 0 || parentCbfCr is not 0)
            {
                cbfCr = _cabac.DecodeDecision(HevcContextLayout.CbfChroma + trafoDepth);
                if (two)
                {
                    cbfCr |= _cabac.DecodeDecision(HevcContextLayout.CbfChroma + trafoDepth) << 1;
                }
            }
        }
        else if (trafoDepth > 0 && log2TrafoSize is 2)
        {
            // Clause 7.4.9.8: the flags of the 4x4 luma blocks are those of the block they are split from, which has
            // their chroma blocks
            cbfCb = parentCbfCb;
            cbfCr = parentCbfCr;
        }

        if (splitTransformFlag)
        {
            var x1 = x0 + (1 << (log2TrafoSize - 1));
            var y1 = y0 + (1 << (log2TrafoSize - 1));
            TransformTree(x0, y0, x0, y0, log2TrafoSize - 1, trafoDepth + 1, 0, cbfCb, cbfCr);
            TransformTree(x1, y0, x0, y0, log2TrafoSize - 1, trafoDepth + 1, 1, cbfCb, cbfCr);
            TransformTree(x0, y1, x0, y0, log2TrafoSize - 1, trafoDepth + 1, 2, cbfCb, cbfCr);
            TransformTree(x1, y1, x0, y0, log2TrafoSize - 1, trafoDepth + 1, 3, cbfCb, cbfCr);
        }
        else
        {
            // cbf_luma is 1 for the transform unit that is a whole coding unit coded in inter prediction mode without a
            // chroma residual: rqt_root_cbf would have been 0 otherwise
            var cbfLuma = (!_isIntra && trafoDepth is 0 && (cbfCb | cbfCr) is 0) || _cabac.DecodeDecision(HevcContextLayout.CbfLuma + (trafoDepth is 0 ? 1 : 0)) is not 0;
            TransformUnit(x0, y0, xBase, yBase, log2TrafoSize, blkIdx, cbfLuma, cbfCb, cbfCr);
        }
    }

    // Clause 7.3.8.10 "Transform unit syntax", and the decoding of the blocks of the transform unit as their residual is
    // read (clauses 8.4.4.1 "General decoding process for intra blocks" and 8.5.4 "Decoding process for the residual signal
    // of coding units coded in inter prediction mode"). The intra prediction mode of a block of a coding unit coded in
    // inter prediction mode is -1.
    private void TransformUnit(int x0, int y0, int xBase, int yBase, int log2TrafoSize, int blkIdx, bool cbfLuma, int cbfCb, int cbfCr)
    {
        var log2TrafoSizeC = Math.Max(2, log2TrafoSize - (_chromaArrayType is 3 ? 0 : 1));
        var cbfChroma = (cbfCb | cbfCr) is not 0;
        MarkTransformEdges(x0, y0, log2TrafoSize);
        if (cbfLuma || cbfChroma)
        {
            // Clause 7.3.8.14 "Delta QP syntax"
            if (_pps.CuQpDeltaEnabledFlag && !_isCuQpDeltaCoded)
            {
                ReadCuQpDelta();
            }

            // Clause 7.3.8.15 "Chroma QP offset syntax"
            if (cbfChroma && !_cuTransquantBypassFlag && _header.CuChromaQpOffsetEnabledFlag && !_isCuChromaQpOffsetCoded)
            {
                ReadCuChromaQpOffset();
            }
        }

        var predModeIntraY = _isIntra ? _intraPredModeY[((y0 >> 2) * _widthIn4x4) + (x0 >> 2)] : -1;
        DecodeIntraBlock(0, x0, y0, x0, y0, log2TrafoSize, predModeIntraY, cbfLuma, crossComponent: false);
        if (_chromaArrayType is 0)
            return;

        var subWidthC = _sps.SubWidthC;
        var subHeightC = _sps.SubHeightC;
        var blockCount = 1;
        if (_chromaArrayType is 2)
        {
            // The chroma blocks of a 4:2:2 transform unit are two square blocks, one above the other
            blockCount = 2;
            _tools |= HevcTools.Chroma422Split;
        }

        if (log2TrafoSize > 2 || _chromaArrayType is 3)
        {
            // The prediction block of the transform unit: one of four when the coding unit is split
            var pbIdx = _intraSplitFlag is 0 ? 0 : (((y0 >> (_log2CbSize - 1)) & 1) << 1) | ((x0 >> (_log2CbSize - 1)) & 1);
            var predModeIntraC = _isIntra ? (int)(_intraPredModeC >> (pbIdx * 8)) & 0xFF : -1;
            var crossComponent = _pps.CrossComponentPredictionEnabledFlag && cbfLuma && (!_isIntra || ((_intraChromaPredMode >> (pbIdx * 8)) & 0xFF) is 4);
            for (var cIdx = 1; cIdx <= 2; cIdx++)
            {
                _resScaleVal = 0;
                if (crossComponent)
                {
                    ReadCrossCompPred(cIdx - 1);
                }

                var cbf = cIdx is 1 ? cbfCb : cbfCr;
                for (var tIdx = 0; tIdx < blockCount; tIdx++)
                {
                    var yOffset = tIdx << log2TrafoSizeC;
                    DecodeIntraBlock(cIdx, x0 / subWidthC, (y0 / subHeightC) + yOffset, x0, y0 + (yOffset * subHeightC), log2TrafoSizeC, predModeIntraC, ((cbf >> tIdx) & 1) is not 0, crossComponent);
                }
            }
        }
        else if (blkIdx is 3)
        {
            // The chroma blocks of the four 4x4 luma blocks: 4x4 blocks at the location of the block they are split from
            var predModeIntraC = _isIntra ? (int)_intraPredModeC & 0xFF : -1;
            _resScaleVal = 0;
            for (var cIdx = 1; cIdx <= 2; cIdx++)
            {
                var cbf = cIdx is 1 ? cbfCb : cbfCr;
                for (var tIdx = 0; tIdx < blockCount; tIdx++)
                {
                    var yOffset = tIdx << 2;
                    DecodeIntraBlock(cIdx, xBase / subWidthC, (yBase / subHeightC) + yOffset, xBase, yBase + (yOffset * subHeightC), 2, predModeIntraC, ((cbf >> tIdx) & 1) is not 0, crossComponent: false);
                }
            }
        }
    }

    // cu_qp_delta_abs (clause 9.3.3.10) and cu_qp_delta_sign_flag
    private void ReadCuQpDelta()
    {
        _isCuQpDeltaCoded = true;

        // The prefix: a truncated Rice binarization with cMax equal to 5
        var cuQpDeltaAbs = 0;
        while (cuQpDeltaAbs < 5 && _cabac.DecodeDecision(HevcContextLayout.CuQpDeltaAbs + (cuQpDeltaAbs is 0 ? 0 : 1)) is not 0)
        {
            cuQpDeltaAbs++;
        }

        if (cuQpDeltaAbs is 5)
        {
            // The suffix: a 0-th order Exp-Golomb binarization. A value of the syntax has a prefix of a few bins
            var k = 0;
            while (_cabac.DecodeBypass() is not 0)
            {
                cuQpDeltaAbs += 1 << k;
                if (++k > 6)
                    throw HevcErrors.Invalid("cu_qp_delta_abs is out of range.");
            }

            cuQpDeltaAbs += _cabac.DecodeBypassBits(k);
        }

        _cuQpDeltaVal = cuQpDeltaAbs;
        if (cuQpDeltaAbs is not 0 && _cabac.DecodeBypass() is not 0)
        {
            _cuQpDeltaVal = -cuQpDeltaAbs;
        }

        // Clause 7.4.9.14
        var qpBdOffsetY = _sps.QpBdOffset;
        if (_cuQpDeltaVal < -(26 + (qpBdOffsetY / 2)) || _cuQpDeltaVal > 25 + (qpBdOffsetY / 2))
            throw HevcErrors.Invalid("CuQpDeltaVal is out of range.");

        // The quantization parameter of the whole coding unit is the one derived with the difference
        UpdateQpY(_xCb, _yCb);
        _tools |= HevcTools.CuQpDelta;
    }

    // cu_chroma_qp_offset_flag and cu_chroma_qp_offset_idx, and clause 7.4.9.15
    private void ReadCuChromaQpOffset()
    {
        _isCuChromaQpOffsetCoded = true;
        if (_cabac.DecodeDecision(HevcContextLayout.CuChromaQpOffsetFlag) is 0)
        {
            _cuQpOffsetCb = 0;
            _cuQpOffsetCr = 0;
            return;
        }

        // cu_chroma_qp_offset_idx: a truncated Rice binarization with cMax equal to chroma_qp_offset_list_len_minus1
        var cuChromaQpOffsetIdx = 0;
        while (cuChromaQpOffsetIdx < _pps.ChromaQpOffsetListLenMinus1 && _cabac.DecodeDecision(HevcContextLayout.CuChromaQpOffsetIdx) is not 0)
        {
            cuChromaQpOffsetIdx++;
        }

        _cuQpOffsetCb = _pps.CbQpOffsetList[cuChromaQpOffsetIdx];
        _cuQpOffsetCr = _pps.CrQpOffsetList[cuChromaQpOffsetIdx];
        _tools |= HevcTools.ChromaQpOffsetList;
    }

    // Clause 7.3.8.12 "Cross-component prediction syntax" and clause 7.4.9.12
    private void ReadCrossCompPred(int c)
    {
        // log2_res_scale_abs_plus1: a truncated Rice binarization with cMax equal to 4
        var log2ResScaleAbsPlus1 = 0;
        while (log2ResScaleAbsPlus1 < 4 && _cabac.DecodeDecision(HevcContextLayout.Log2ResScaleAbsPlus1 + (4 * c) + log2ResScaleAbsPlus1) is not 0)
        {
            log2ResScaleAbsPlus1++;
        }

        if (log2ResScaleAbsPlus1 is 0)
        {
            _resScaleVal = 0;
            return;
        }

        var resScaleSignFlag = _cabac.DecodeDecision(HevcContextLayout.ResScaleSignFlag + c);

        // Equation 7-80
        _resScaleVal = (1 << (log2ResScaleAbsPlus1 - 1)) * (1 - (2 * resScaleSignFlag));
        _tools |= HevcTools.CrossComponentPrediction;
    }

    // Clause 8.6.1 "Derivation process for quantization parameters", steps 1 to 4: qPY_PRED of the quantization group
    // whose first coding unit is at ( xQg, yQg )
    private void BeginQuantizationGroup(int xQg, int yQg)
    {
        _isCuQpDeltaCoded = false;
        _cuQpDeltaVal = 0;

        // qPY_PREV is QpY of the last coding unit of the previous quantization group in decoding order, or SliceQpY for
        // the first quantization group of a slice, of a tile and of a row of coding tree blocks of the wavefront
        var qPyPrev = _qpY;

        // The neighbours are available and in the coding tree block of the quantization group when they are in it
        var ctbMask = (1 << _ctbLog2SizeY) - 1;
        var qPyA = (xQg & ctbMask) is not 0 ? _qpYMap[((yQg >> _minCbLog2SizeY) * _widthInMinCbs) + ((xQg - 1) >> _minCbLog2SizeY)] : qPyPrev;
        var qPyB = (yQg & ctbMask) is not 0 ? _qpYMap[(((yQg - 1) >> _minCbLog2SizeY) * _widthInMinCbs) + (xQg >> _minCbLog2SizeY)] : qPyPrev;

        // Equation 8-282
        _qpYPred = (qPyA + qPyB + 1) >> 1;
    }

    // Equation 8-283: QpY of the coding unit at ( xCb, yCb ), of the size of the coding unit being decoded
    private void UpdateQpY(int xCb, int yCb)
    {
        if (_pps.CuQpDeltaEnabledFlag)
        {
            var qpBdOffsetY = _sps.QpBdOffset;
            _qpY = ((_qpYPred + _cuQpDeltaVal + 52 + (2 * qpBdOffsetY)) % (52 + qpBdOffsetY)) - qpBdOffsetY;
        }
        else
        {
            // Every coding unit of the slice has the quantization parameter of the slice
            _qpY = _header.SliceQpY;
        }

        var count = 1 << (_log2CbSize - _minCbLog2SizeY);
        var index = ((yCb >> _minCbLog2SizeY) * _widthInMinCbs) + (xCb >> _minCbLog2SizeY);
        for (var y = 0; y < count; y++)
        {
            _qpYMap.Slice(index + (y * _widthInMinCbs), count).Fill((sbyte)_qpY);
        }
    }

    // Equations 8-285 to 8-290: Qp'Cb (cIdx 1) or Qp'Cr (cIdx 2)
    private readonly int GetChromaQp(int cIdx)
    {
        var qpBdOffsetC = _sps.QpBdOffset;
        var offset = cIdx is 1
            ? _pps.CbQpOffset + _header.SliceCbQpOffset + _cuQpOffsetCb
            : _pps.CrQpOffset + _header.SliceCrQpOffset + _cuQpOffsetCr;
        var qPi = Math.Clamp(_qpY + offset, -qpBdOffsetC, 57);
        var qPc = _chromaArrayType is 1 ? MapChromaQp420(qPi) : Math.Min(qPi, 51);
        return qPc + qpBdOffsetC;
    }

    /// <summary>Gets <c>QpC</c> as a function of <c>qPi</c> for <c>ChromaArrayType</c> equal to 1 (Table 8-10).</summary>
    public static int MapChromaQp420(int qPi)
    {
        if (qPi < 30)
            return qPi;

        return qPi > 43 ? qPi - 6 : HevcQuantizationTables.ChromaQp420[qPi - 30];
    }
}
