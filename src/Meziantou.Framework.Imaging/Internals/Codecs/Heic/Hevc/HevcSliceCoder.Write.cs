using System.Diagnostics;

namespace Meziantou.Framework.Imaging.Internals;

// The encoder side of the slice segment data: the syntax elements of a coding tree unit are written beside the methods
// that read them, with the same derivations of the context variables and the same state of the picture, which a method
// that writes a coding unit updates as the method that reads it does
internal ref partial struct HevcSliceCoder
{
    /// <summary>Creates the encoder of the only slice segment of the picture being encoded.</summary>
    /// <param name="layout">The parameter sets of the picture.</param>
    /// <param name="header">The header of the slice segment, as a decoder reads it.</param>
    /// <param name="picture">The picture being reconstructed.</param>
    /// <param name="state">The state of the picture being reconstructed.</param>
    /// <param name="contexts">The context variables, which are those of an arithmetic decoder.</param>
    /// <param name="scratch">The working buffers of the decoding processes.</param>
    /// <param name="references">The reference picture lists of the slice; <see langword="null"/> for an I slice.</param>
    /// <param name="workspace">The buffers of the encoder.</param>
    /// <param name="sink">Where the bins go.</param>
    /// <param name="decisions">What the encoder searches.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests, checked at every coding tree unit.</param>
    public HevcSliceCoder(HevcPictureLayout layout, HevcSliceSegmentHeader header, HevcPicture picture, HevcPictureState state, HevcCabacDecoder contexts, HevcSliceScratch scratch, HevcReferencePictureLists? references, HevcEncoderWorkspace workspace, HevcBinSink sink, in HevcEncoderDecisions decisions, CancellationToken cancellationToken)
    {
        _layout = layout;
        _sps = layout.Sps;
        _pps = layout.Pps;
        _header = header;
        _picture = picture;
        _state = state;
        _cabac = contexts;
        _scratch = scratch;
        _rbsp = null!;
        _refs = references;
        _motion = references is null ? default : state.Motion;
        _cancellationToken = cancellationToken;
        _cuFlags = state.CuFlags;
        _qpYMap = state.QpY;
        _intraPredModeY = state.IntraPredModeY;
        _edgeFlags = state.EdgeFlags;
        _sliceIndex = state.SliceIndex;
        _ctbAddrRsToTs = layout.CtbAddrRsToTs;
        _ctbAddrTsToRs = layout.CtbAddrTsToRs;
        _tileId = layout.TileId;
        _sliceIdx = state.Slices.Count - 1;
        _chromaArrayType = _sps.ChromaArrayType;
        _ctbLog2SizeY = _sps.CtbLog2SizeY;
        _minCbLog2SizeY = _sps.MinCbLog2SizeY;
        _minTbLog2SizeY = _sps.MinTbLog2SizeY;
        _bitDepth = _sps.BitDepth;
        _widthInMinCbs = state.WidthInMinCbs;
        _widthIn4x4 = state.WidthIn4x4;
        _picWidthInCtbsY = _sps.PicWidthInCtbsY;
        _workspace = workspace;
        _sink = sink;
        _decisions = decisions;
        _qpY = header.SliceQpY;

        // The encoder writes one slice of one tile, whose coding units have the quantization parameter of the slice and
        // none of the tools of the range extensions; its rows may be substreams of the wavefront
        Debug.Assert(!_pps.TilesEnabledFlag && !_pps.CuQpDeltaEnabledFlag && !_sps.PcmEnabledFlag);
        Debug.Assert(!_pps.CrossComponentPredictionEnabledFlag && !_header.CuChromaQpOffsetEnabledFlag && !_sps.PersistentRiceAdaptationEnabledFlag);
        Debug.Assert(!_sps.TransformSkipContextEnabledFlag && !_sps.ImplicitRdpcmEnabledFlag && !_sps.ExplicitRdpcmEnabledFlag && !_sps.TransformSkipRotationEnabledFlag);
    }

    /// <summary>
    /// Searches the coding tree units of a row of the picture (clause 7.3.8.2 "Coding tree unit syntax"), which are
    /// reconstructed and recorded.
    /// </summary>
    /// <param name="ry">The row of coding tree blocks.</param>
    /// <param name="write">
    /// Whether each coding tree unit is written once it is searched, with <c>end_of_slice_segment_flag</c>; the records are
    /// kept for <see cref="WriteCodingTreeUnits"/> otherwise.
    /// </param>
    /// <param name="contextsWpp">
    /// Where the context variables are stored after the second coding tree unit of the row, for the next row of the
    /// wavefront (clause 9.3.2.4); empty without wavefront.
    /// </param>
    public void SearchCodingTreeUnits(int ry, bool write, Span<byte> contextsWpp = default)
    {
        var workspace = _workspace;
        for (var rx = 0; rx < _picWidthInCtbsY; rx++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            BeginCodingTreeUnit(rx, ry);
            var start = workspace.RecordCount;
            if (write)
            {
                SaveContexts(HevcEncoderWorkspace.ContextCtuStart);
            }

            _sink.Mode = HevcBinSinkMode.Cost;
            SearchCodingQuadtree(rx << _ctbLog2SizeY, ry << _ctbLog2SizeY, _ctbLog2SizeY, 0);
            if (!write)
            {
                // The search of the next row of the wavefront starts with the context variables of the search after the
                // second coding tree unit of this row, as the writing of the row does
                if (rx is 1 && !contextsWpp.IsEmpty)
                {
                    _cabac.SaveContexts(contextsWpp);
                }

                continue;
            }

            // The context variables after the search are those of a decoder after the coding tree unit: the search ends
            // every coding unit with the costs of its syntax elements, in the order of the syntax
            SaveContexts(HevcEncoderWorkspace.ContextCheck);
            RestoreContexts(HevcEncoderWorkspace.ContextCtuStart);
            workspace.RecordPosition = start;
            _sink.Mode = HevcBinSinkMode.Encode;
            WriteCodingQuadtree(rx << _ctbLog2SizeY, ry << _ctbLog2SizeY, _ctbLog2SizeY, 0);
            Debug.Assert(workspace.RecordPosition == workspace.RecordCount, "The coding tree unit is not written from all its records.");
            Debug.Assert(_cabac.Contexts.SequenceEqual(workspace.GetContexts(HevcEncoderWorkspace.ContextCheck)), "The search and the arithmetic encoder do not write the same bins.");

            // end_of_slice_segment_flag
            _sink.EncodeTerminate(_ctbAddrInRs == _sps.PicSizeInCtbsY - 1 ? 1 : 0);
            workspace.RecordCount = start;
            if (rx is 1 && !contextsWpp.IsEmpty)
            {
                _cabac.SaveContexts(contextsWpp);
            }
        }
    }

    /// <summary>
    /// Writes the coding tree units of a row of the picture from their records, after the picture is deblocked: the
    /// sample adaptive offset of each coding tree block is decided from the samples that the process modifies, and its
    /// syntax precedes the coding quadtree.
    /// </summary>
    /// <param name="ry">The row of coding tree blocks.</param>
    /// <param name="contextsWpp">Where the context variables are stored after the second coding tree unit of the row; empty without wavefront.</param>
    public void WriteCodingTreeUnits(int ry, Span<byte> contextsWpp = default)
    {
        for (var rx = 0; rx < _picWidthInCtbsY; rx++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            BeginCodingTreeUnit(rx, ry);
            if (_header.SliceSaoLumaFlag || _header.SliceSaoChromaFlag)
            {
                SearchSao(rx, ry);
                _sink.Mode = HevcBinSinkMode.Encode;
                WriteSao(rx, ry);
            }

            _sink.Mode = HevcBinSinkMode.Encode;
            WriteCodingQuadtree(rx << _ctbLog2SizeY, ry << _ctbLog2SizeY, _ctbLog2SizeY, 0);
            _sink.EncodeTerminate(_ctbAddrInRs == _sps.PicSizeInCtbsY - 1 ? 1 : 0);
            if (rx is 1 && !contextsWpp.IsEmpty)
            {
                _cabac.SaveContexts(contextsWpp);
            }
        }
    }

    private void BeginCodingTreeUnit(int rx, int ry)
    {
        _ctbAddrInRs = (ry * _picWidthInCtbsY) + rx;
        _ctbAddrInTs = _ctbAddrRsToTs[_ctbAddrInRs];
        _sliceIndex[_ctbAddrInRs] = _sliceIdx;
    }

    private readonly void SaveContexts(int slot) => _cabac.SaveContexts(_workspace.GetContexts(slot));

    private readonly void RestoreContexts(int slot) => _cabac.LoadContexts(_workspace.GetContexts(slot));

    // A value of the coding unit: the one of the description of the coding unit being searched, which is recorded, or the
    // next record
    private readonly int Record(int value)
    {
        var workspace = _workspace;
        if (_storing)
        {
            workspace.AppendRecord(value);
            return value;
        }

        return workspace.Records[workspace.RecordPosition++];
    }

    // Clause 7.3.8.3 "Sample adaptive offset syntax": the parameters of the coding tree block are those of the picture
    // state. They are merged with the ones of the coding tree block to the left, or above, when they are the same.
    private void WriteSao(int rx, int ry)
    {
        var contexts = _cabac.Contexts;
        var sao = _state.Sao.Slice(_ctbAddrInRs * HevcPictureState.SaoValuesPerCtb, HevcPictureState.SaoValuesPerCtb);
        if (rx > 0)
        {
            var saoMergeLeftFlag = sao.SequenceEqual(_state.Sao.Slice((_ctbAddrInRs - 1) * HevcPictureState.SaoValuesPerCtb, HevcPictureState.SaoValuesPerCtb));
            _sink.EncodeDecision(ref contexts[HevcContextLayout.SaoMergeFlag], saoMergeLeftFlag ? 1 : 0);
            if (saoMergeLeftFlag)
                return;
        }

        if (ry > 0)
        {
            var saoMergeUpFlag = sao.SequenceEqual(_state.Sao.Slice((_ctbAddrInRs - _picWidthInCtbsY) * HevcPictureState.SaoValuesPerCtb, HevcPictureState.SaoValuesPerCtb));
            _sink.EncodeDecision(ref contexts[HevcContextLayout.SaoMergeFlag], saoMergeUpFlag ? 1 : 0);
            if (saoMergeUpFlag)
                return;
        }

        WriteSaoOffsets(sao);
    }

    // The syntax elements of the sample adaptive offset of a coding tree block that is not merged
    private readonly void WriteSaoOffsets(ReadOnlySpan<sbyte> sao)
    {
        var contexts = _cabac.Contexts;
        var cMax = (1 << (Math.Min(_bitDepth, 10) - 5)) - 1;
        for (var cIdx = 0; cIdx < (_chromaArrayType is not 0 ? 3 : 1); cIdx++)
        {
            if (!(cIdx is 0 ? _header.SliceSaoLumaFlag : _header.SliceSaoChromaFlag))
                continue;

            var values = sao.Slice(cIdx * 6, 6);
            if (cIdx is not 2)
            {
                // sao_type_idx_luma, sao_type_idx_chroma: a truncated Rice binarization with cMax equal to 2
                _sink.EncodeDecision(ref contexts[HevcContextLayout.SaoTypeIdx], values[0] is 0 ? 0 : 1);
                if (values[0] is not 0)
                {
                    _sink.EncodeBypass(values[0] - 1);
                }
            }

            if (values[0] is 0)
                continue;

            for (var i = 0; i < 4; i++)
            {
                // sao_offset_abs: a truncated unary binarization of bypass bins
                var saoOffsetAbs = Math.Abs(values[2 + i]);
                Debug.Assert(saoOffsetAbs <= cMax);
                for (var k = 0; k < saoOffsetAbs; k++)
                {
                    _sink.EncodeBypass(1);
                }

                if (saoOffsetAbs < cMax)
                {
                    _sink.EncodeBypass(0);
                }
            }

            if (values[0] is 1)
            {
                for (var i = 0; i < 4; i++)
                {
                    if (values[2 + i] is not 0)
                    {
                        // sao_offset_sign
                        _sink.EncodeBypass(values[2 + i] < 0 ? 1 : 0);
                    }
                }

                // sao_band_position
                _sink.EncodeBypassBits((uint)values[1], 5);
            }
            else
            {
                // sao_eo_class_luma, sao_eo_class_chroma: the signs of the offsets are inferred
                Debug.Assert(values[2] >= 0 && values[3] >= 0 && values[4] <= 0 && values[5] <= 0);
                if (cIdx is not 2)
                {
                    _sink.EncodeBypassBits((uint)values[1], 2);
                }
            }
        }
    }

    // Clause 7.3.8.4 "Coding quadtree syntax", from the records
    private void WriteCodingQuadtree(int x0, int y0, int log2CbSize, int cqtDepth)
    {
        var size = 1 << log2CbSize;
        bool splitCuFlag;
        if (x0 + size <= _sps.PicWidthInLumaSamples && y0 + size <= _sps.PicHeightInLumaSamples && log2CbSize > _minCbLog2SizeY)
        {
            splitCuFlag = Record(0) is not 0;
            _sink.EncodeDecision(ref _cabac.Contexts[HevcContextLayout.SplitCuFlag + GetSplitCuFlagCtxInc(x0, y0, cqtDepth)], splitCuFlag ? 1 : 0);
        }
        else
        {
            splitCuFlag = log2CbSize > _minCbLog2SizeY;
        }

        if (!splitCuFlag)
        {
            WriteCodingUnit(x0, y0, log2CbSize, cqtDepth);
            return;
        }

        var x1 = x0 + (size >> 1);
        var y1 = y0 + (size >> 1);
        WriteCodingQuadtree(x0, y0, log2CbSize - 1, cqtDepth + 1);
        if (x1 < _sps.PicWidthInLumaSamples)
        {
            WriteCodingQuadtree(x1, y0, log2CbSize - 1, cqtDepth + 1);
        }

        if (y1 < _sps.PicHeightInLumaSamples)
        {
            WriteCodingQuadtree(x0, y1, log2CbSize - 1, cqtDepth + 1);
        }

        if (x1 < _sps.PicWidthInLumaSamples && y1 < _sps.PicHeightInLumaSamples)
        {
            WriteCodingQuadtree(x1, y1, log2CbSize - 1, cqtDepth + 1);
        }
    }

    // Clause 7.3.8.5 "Coding unit syntax". The state of the picture becomes the one of a decoder that has read the coding
    // unit, apart from its samples, which the search reconstructs.
    private void WriteCodingUnit(int x0, int y0, int log2CbSize, int cqtDepth)
    {
        var workspace = _workspace;
        var contexts = _cabac.Contexts;
        var nCbS = 1 << log2CbSize;
        _log2CbSize = log2CbSize;
        _xCb = x0;
        _yCb = y0;
        _ctDepth = cqtDepth;

        // The first record: cu_skip_flag, the prediction mode, part_mode and rqt_root_cbf
        var head = Record(_storing ? (workspace.Skip ? 1 : 0) | (_isIntra ? 2 : 0) | (workspace.PartMode << 4) | (workspace.RqtRootCbf ? 0x100 : 0) : 0);
        var skip = (head & 1) is not 0;
        _isIntra = (head & 2) is not 0;
        var partMode = (head >> 4) & 15;
        var rqtRootCbf = (head & 0x100) is not 0;
        _cuTransquantBypassFlag = _decisions.Lossless;
        if (_pps.TransquantBypassEnabledFlag)
        {
            _sink.EncodeDecision(ref contexts[HevcContextLayout.CuTransquantBypassFlag], _cuTransquantBypassFlag ? 1 : 0);
        }

        var isInterSlice = _refs is not null;
        if (isInterSlice)
        {
            _sink.EncodeDecision(ref contexts[HevcContextLayout.CuSkipFlag + GetCuSkipFlagCtxInc(x0, y0)], skip ? 1 : 0);
            if (!skip)
            {
                _sink.EncodeDecision(ref contexts[HevcContextLayout.PredModeFlag], _isIntra ? 1 : 0);
            }
        }

        var flags = (byte)(cqtDepth << HevcPictureState.CuDepthShift);
        if (_isIntra)
        {
            flags |= HevcPictureState.CuIntra;
        }
        else if (skip)
        {
            flags |= HevcPictureState.CuSkip;
        }

        if (_cuTransquantBypassFlag)
        {
            flags |= HevcPictureState.CuTransquantBypass;
        }

        // part_mode (Table 9-45)
        if (!_isIntra)
        {
            if (!skip)
            {
                WritePartModeInter(log2CbSize, partMode);
            }
        }
        else if (log2CbSize == _minCbLog2SizeY)
        {
            _sink.EncodeDecision(ref contexts[HevcContextLayout.PartMode], partMode is HevcConstants.Part2Nx2N ? 1 : 0);
        }

        _partMode = partMode;
        _intraSplitFlag = _isIntra && partMode is HevcConstants.PartNxN ? 1 : 0;
        _interSplitFlag = 0;
        ClearEdgeFlags(x0, y0, log2CbSize);
        SetCodingUnit(x0, y0, log2CbSize, flags);
        MarkTransformEdges(x0, y0, log2CbSize);
        UpdateQpY(x0, y0);
        if (_isIntra && isInterSlice)
        {
            ClearMotion(x0, y0, log2CbSize);
        }

        if (!_isIntra)
        {
            var mergeFlag = WritePredictionUnits(x0, y0, nCbS, partMode, skip);
            if (skip)
                return;

            // rqt_root_cbf
            if (!(partMode is HevcConstants.Part2Nx2N && mergeFlag))
            {
                _sink.EncodeDecision(ref contexts[HevcContextLayout.RqtRootCbf], rqtRootCbf ? 1 : 0);
            }
            else
            {
                Debug.Assert(rqtRootCbf, "A merged coding unit without residual is skipped.");
            }

            if (!rqtRootCbf)
                return;

            _maxTrafoDepth = _sps.MaxTransformHierarchyDepthInter;
            _interSplitFlag = _maxTrafoDepth is 0 && partMode is not HevcConstants.Part2Nx2N ? 1 : 0;
            WriteTransformTree(x0, y0, x0, y0, log2CbSize, 0, 0, parentCbfCb: 3, parentCbfCr: 3);
            return;
        }

        WriteIntraPredictionModes(x0, y0, nCbS, partMode);
        _maxTrafoDepth = _sps.MaxTransformHierarchyDepthIntra + _intraSplitFlag;
        WriteTransformTree(x0, y0, x0, y0, log2CbSize, 0, 0, parentCbfCb: 3, parentCbfCr: 3);
    }

    // The flags of the deblocking filter of the blocks of a coding unit, which the methods that write it set again
    private readonly void ClearEdgeFlags(int x0, int y0, int log2Size)
    {
        var count = 1 << (log2Size - 2);
        var index = ((y0 >> 2) * _widthIn4x4) + (x0 >> 2);
        for (var y = 0; y < count; y++)
        {
            _edgeFlags.Slice(index + (y * _widthIn4x4), count).Clear();
        }
    }

    // prev_intra_luma_pred_flag, mpm_idx, rem_intra_luma_pred_mode and intra_chroma_pred_mode of the coding unit syntax:
    // the inverse of clauses 8.4.2 and 8.4.3
    private void WriteIntraPredictionModes(int x0, int y0, int nCbS, int partMode)
    {
        var workspace = _workspace;
        var contexts = _cabac.Contexts;
        var count = partMode is HevcConstants.PartNxN ? 4 : 1;
        var pbSize = partMode is HevcConstants.PartNxN ? nCbS >> 1 : nCbS;
        var lumaModes = (uint)(ushort)Record(_storing ? (int)(workspace.LumaModes & 0xFFFF) : 0);
        lumaModes |= (uint)(ushort)Record(_storing ? (int)(workspace.LumaModes >> 16) : 0) << 16;
        Span<int> candModeList = stackalloc int[3];
        Span<int> mpmIdx = stackalloc int[4];
        Span<int> remIntraLumaPredMode = stackalloc int[4];
        for (var i = 0; i < count; i++)
        {
            var xPb = x0 + ((i & 1) * pbSize);
            var yPb = y0 + ((i >> 1) * pbSize);
            var intraPredModeY = (int)(lumaModes >> (i * 8)) & 0xFF;
            DeriveCandModeList(xPb, yPb, candModeList);
            GetIntraLumaPredModeSyntax(intraPredModeY, candModeList, out mpmIdx[i], out remIntraLumaPredMode[i]);

            // The mode of a prediction block is a neighbour of the ones that follow it
            SetIntraPredModeY(xPb, yPb, pbSize, intraPredModeY);
        }

        for (var i = 0; i < count; i++)
        {
            _sink.EncodeDecision(ref contexts[HevcContextLayout.PrevIntraLumaPredFlag], mpmIdx[i] >= 0 ? 1 : 0);
        }

        for (var i = 0; i < count; i++)
        {
            if (mpmIdx[i] >= 0)
            {
                // mpm_idx: a truncated Rice binarization with cMax equal to 2
                _sink.EncodeBypass(mpmIdx[i] is 0 ? 0 : 1);
                if (mpmIdx[i] is not 0)
                {
                    _sink.EncodeBypass(mpmIdx[i] - 1);
                }
            }
            else
            {
                _sink.EncodeBypassBits((uint)remIntraLumaPredMode[i], 5);
            }
        }

        _intraChromaPredMode = 0;
        _intraPredModeC = 0;
        if (_chromaArrayType is 0)
            return;

        var chromaModes = Record(_storing ? (int)(workspace.ChromaModes & 0xFF) | (int)((workspace.ChromaModes >> 8) & 0xFF) << 3 | (int)((workspace.ChromaModes >> 16) & 0xFF) << 6 | (int)((workspace.ChromaModes >> 24) & 0xFF) << 9 : 0);
        var chromaCount = _chromaArrayType is 3 ? count : 1;
        for (var i = 0; i < chromaCount; i++)
        {
            // intra_chroma_pred_mode (Table 9-46)
            var intraChromaPredMode = (chromaModes >> (i * 3)) & 7;
            _sink.EncodeDecision(ref contexts[HevcContextLayout.IntraChromaPredMode], intraChromaPredMode is 4 ? 0 : 1);
            if (intraChromaPredMode is not 4)
            {
                _sink.EncodeBypassBits((uint)intraChromaPredMode, 2);
            }

            SetIntraPredModeC(x0, y0, pbSize, i, intraChromaPredMode);
        }

        if (chromaCount is 1)
        {
            // One mode for the coding unit
            _intraChromaPredMode *= 0x01010101;
            _intraPredModeC *= 0x01010101;
        }
    }

    // intra_chroma_pred_mode and IntraPredModeC of the prediction block i of the coding unit
    private void SetIntraPredModeC(int x0, int y0, int pbSize, int i, int intraChromaPredMode)
    {
        var xPb = x0 + ((i & 1) * pbSize);
        var yPb = y0 + ((i >> 1) * pbSize);
        int intraPredModeY = _intraPredModeY[((yPb >> 2) * _widthIn4x4) + (xPb >> 2)];
        var intraPredModeC = GetIntraPredModeC(intraChromaPredMode, intraPredModeY);
        _intraChromaPredMode = (_intraChromaPredMode & ~(0xFFu << (i * 8))) | ((uint)intraChromaPredMode << (i * 8));
        _intraPredModeC = (_intraPredModeC & ~(0xFFu << (i * 8))) | ((uint)intraPredModeC << (i * 8));
    }

    // The syntax elements of an intra prediction mode (clause 8.4.2, steps 4 and 5): mpm_idx when the mode is a candidate,
    // which prev_intra_luma_pred_flag tells; rem_intra_luma_pred_mode otherwise, and mpm_idx is -1
    private static void GetIntraLumaPredModeSyntax(int intraPredModeY, ReadOnlySpan<int> candModeList, out int mpmIdx, out int remIntraLumaPredMode)
    {
        mpmIdx = candModeList.IndexOf(intraPredModeY);
        remIntraLumaPredMode = intraPredModeY;
        if (mpmIdx >= 0)
            return;

        // The mode is the rank of the mode among the ones that are not candidates
        foreach (var candidate in candModeList)
        {
            if (candidate < intraPredModeY)
            {
                remIntraLumaPredMode--;
            }
        }
    }

    // Clause 7.3.8.8 "Transform tree syntax"
    private void WriteTransformTree(int x0, int y0, int xBase, int yBase, int log2TrafoSize, int trafoDepth, int blkIdx, int parentCbfCb, int parentCbfCr)
    {
        var workspace = _workspace;
        var contexts = _cabac.Contexts;
        var cell = GetCell(x0, y0);
        bool splitTransformFlag;
        if (log2TrafoSize <= _sps.MaxTbLog2SizeY && log2TrafoSize > _minTbLog2SizeY && trafoDepth < _maxTrafoDepth && !(_intraSplitFlag is 1 && trafoDepth is 0))
        {
            splitTransformFlag = Record(_storing && workspace.GetMap(HevcEncoderWorkspace.MapTrafoDepth)[cell] > trafoDepth ? 1 : 0) is not 0;
            _sink.EncodeDecision(ref contexts[HevcContextLayout.SplitTransformFlag + 5 - log2TrafoSize], splitTransformFlag ? 1 : 0);
        }
        else
        {
            // Clause 7.4.9.8
            splitTransformFlag = log2TrafoSize > _sps.MaxTbLog2SizeY || (_intraSplitFlag is 1 && trafoDepth is 0) || (_interSplitFlag is 1 && trafoDepth is 0);
        }

        var cbfCb = 0;
        var cbfCr = 0;
        if ((log2TrafoSize > 2 && _chromaArrayType is not 0) || _chromaArrayType is 3)
        {
            // The flags of a node are those of the chroma blocks under it
            var two = _chromaArrayType is 2 && (!splitTransformFlag || log2TrafoSize is 3);
            var cbf = _storing ? GetChromaCbf(x0, y0, log2TrafoSize) : 0;
            if (trafoDepth is 0 || parentCbfCb is not 0)
            {
                cbfCb = Record(two ? cbf & 3 : (cbf & 3) is not 0 ? 1 : 0);
                _sink.EncodeDecision(ref contexts[HevcContextLayout.CbfChroma + trafoDepth], cbfCb & 1);
                if (two)
                {
                    _sink.EncodeDecision(ref contexts[HevcContextLayout.CbfChroma + trafoDepth], cbfCb >> 1);
                }
            }

            if (trafoDepth is 0 || parentCbfCr is not 0)
            {
                cbfCr = Record(two ? cbf >> 2 : (cbf >> 2) is not 0 ? 1 : 0);
                _sink.EncodeDecision(ref contexts[HevcContextLayout.CbfChroma + trafoDepth], cbfCr & 1);
                if (two)
                {
                    _sink.EncodeDecision(ref contexts[HevcContextLayout.CbfChroma + trafoDepth], cbfCr >> 1);
                }
            }
        }
        else if (trafoDepth > 0 && log2TrafoSize is 2)
        {
            cbfCb = parentCbfCb;
            cbfCr = parentCbfCr;
        }

        if (splitTransformFlag)
        {
            var x1 = x0 + (1 << (log2TrafoSize - 1));
            var y1 = y0 + (1 << (log2TrafoSize - 1));
            WriteTransformTree(x0, y0, x0, y0, log2TrafoSize - 1, trafoDepth + 1, 0, cbfCb, cbfCr);
            WriteTransformTree(x1, y0, x0, y0, log2TrafoSize - 1, trafoDepth + 1, 1, cbfCb, cbfCr);
            WriteTransformTree(x0, y1, x0, y0, log2TrafoSize - 1, trafoDepth + 1, 2, cbfCb, cbfCr);
            WriteTransformTree(x1, y1, x0, y0, log2TrafoSize - 1, trafoDepth + 1, 3, cbfCb, cbfCr);
            return;
        }

        // cbf_luma is 1 for the transform unit that is a whole coding unit coded in inter prediction mode without a
        // chroma residual
        var cbfLuma = true;
        if (_isIntra || trafoDepth is not 0 || (cbfCb | cbfCr) is not 0)
        {
            cbfLuma = Record(_storing ? workspace.GetMap(HevcEncoderWorkspace.MapCbfLuma)[cell] : 0) is not 0;
            _sink.EncodeDecision(ref contexts[HevcContextLayout.CbfLuma + (trafoDepth is 0 ? 1 : 0)], cbfLuma ? 1 : 0);
        }
        else
        {
            Debug.Assert(!_storing || workspace.GetMap(HevcEncoderWorkspace.MapCbfLuma)[cell] is not 0, "A coding unit without residual has no transform tree.");
        }

        WriteTransformUnit(x0, y0, xBase, yBase, log2TrafoSize, blkIdx, cbfLuma, cbfCb, cbfCr);
    }

    // The index in the maps of the coding unit being written of the block of 4x4 luma samples at ( x, y )
    private readonly int GetCell(int x, int y) => (((y - _yCb) >> 2) * HevcEncoderWorkspace.CellStride) + ((x - _xCb) >> 2);

    // cbf_cb (bits 0 and 1) and cbf_cr (bits 2 and 3) of the chroma blocks of a node of the transform tree of the coding
    // unit being searched
    private readonly int GetChromaCbf(int x0, int y0, int log2TrafoSize)
    {
        var map = _workspace.GetMap(HevcEncoderWorkspace.MapCbfChroma);
        var count = 1 << (log2TrafoSize - 2);
        var cell = GetCell(x0, y0);
        var cbf = 0;
        for (var y = 0; y < count; y++)
        {
            foreach (var value in map.Slice(cell + (y * HevcEncoderWorkspace.CellStride), count))
            {
                cbf |= value;
            }
        }

        return cbf;
    }

    // Clause 7.3.8.10 "Transform unit syntax"
    private void WriteTransformUnit(int x0, int y0, int xBase, int yBase, int log2TrafoSize, int blkIdx, bool cbfLuma, int cbfCb, int cbfCr)
    {
        var log2TrafoSizeC = Math.Max(2, log2TrafoSize - (_chromaArrayType is 3 ? 0 : 1));
        MarkTransformEdges(x0, y0, log2TrafoSize);
        if (cbfLuma)
        {
            var predModeIntraY = _isIntra ? _intraPredModeY[((y0 >> 2) * _widthIn4x4) + (x0 >> 2)] : -1;
            WriteBlock(0, x0, y0, log2TrafoSize, predModeIntraY, GetCell(x0, y0), 0);
            if ((_edgeFlags[((y0 >> 2) * _widthIn4x4) + (x0 >> 2)] & HevcPictureState.CodedLuma) is 0)
            {
                MarkCodedLuma(x0, y0, log2TrafoSize);
            }
        }

        if (_chromaArrayType is 0 || (cbfCb | cbfCr) is 0)
            return;

        var subWidthC = _sps.SubWidthC;
        var subHeightC = _sps.SubHeightC;
        var blockCount = _chromaArrayType is 2 ? 2 : 1;
        if (log2TrafoSize > 2 || _chromaArrayType is 3)
        {
            var pbIdx = _intraSplitFlag is 0 ? 0 : (((y0 >> (_log2CbSize - 1)) & 1) << 1) | ((x0 >> (_log2CbSize - 1)) & 1);
            var predModeIntraC = _isIntra ? (int)(_intraPredModeC >> (pbIdx * 8)) & 0xFF : -1;
            for (var cIdx = 1; cIdx <= 2; cIdx++)
            {
                var cbf = cIdx is 1 ? cbfCb : cbfCr;
                for (var tIdx = 0; tIdx < blockCount; tIdx++)
                {
                    if (((cbf >> tIdx) & 1) is not 0)
                    {
                        WriteBlock(cIdx, x0 / subWidthC, (y0 / subHeightC) + (tIdx << log2TrafoSizeC), log2TrafoSizeC, predModeIntraC, GetCell(x0, y0), tIdx);
                    }
                }
            }
        }
        else if (blkIdx is 3)
        {
            // The chroma blocks of the four 4x4 luma blocks
            var predModeIntraC = _isIntra ? (int)_intraPredModeC & 0xFF : -1;
            for (var cIdx = 1; cIdx <= 2; cIdx++)
            {
                var cbf = cIdx is 1 ? cbfCb : cbfCr;
                for (var tIdx = 0; tIdx < blockCount; tIdx++)
                {
                    if (((cbf >> tIdx) & 1) is not 0)
                    {
                        WriteBlock(cIdx, xBase / subWidthC, (yBase / subHeightC) + (tIdx << 2), 2, predModeIntraC, GetCell(xBase, yBase), tIdx);
                    }
                }
            }
        }
    }

    // The residual of a block of a transform unit: transform_skip_flag and the levels are those of the coding unit being
    // searched, or of the records. A record has the number of levels up to the last one that is not zero, in the order
    // of the rows, then these levels.
    private void WriteBlock(int cIdx, int xTb, int yTb, int log2TrafoSize, int predModeIntra, int cell, int tIdx)
    {
        var workspace = _workspace;
        var nTbS = 1 << log2TrafoSize;
        var block = workspace.GetScratch(4)[..(nTbS * nTbS)];
        bool transformSkipFlag;
        if (_storing)
        {
            // The levels of the coding unit being searched are at the location of the samples of the block in the coding
            // unit
            var subWidth = cIdx is 0 ? 1 : _sps.SubWidthC;
            var subHeight = cIdx is 0 ? 1 : _sps.SubHeightC;
            var levels = workspace.GetLevels(cIdx);
            var offset = ((yTb - (_yCb / subHeight)) * HevcEncoderWorkspace.BlockStride) + xTb - (_xCb / subWidth);
            transformSkipFlag = cIdx is 0
                ? workspace.GetMap(HevcEncoderWorkspace.MapTransformSkipLuma)[cell] is not 0
                : ((workspace.GetMap(HevcEncoderWorkspace.MapTransformSkipChroma)[cell] >> (((cIdx - 1) * 2) + tIdx)) & 1) is not 0;
            var count = 0;
            for (var y = nTbS - 1; y >= 0 && count is 0; y--)
            {
                var last = levels.Slice(offset + (y * HevcEncoderWorkspace.BlockStride), nTbS).LastIndexOfAnyExcept((short)0);
                if (last >= 0)
                {
                    count = (y * nTbS) + last + 1;
                }
            }

            Debug.Assert(count > 0, "A block that is coded has a level.");
            workspace.AppendRecord(count | (transformSkipFlag ? 0x4000 : 0));
            var records = workspace.EnsureRecords(nTbS * nTbS);
            for (var y = 0; y < nTbS; y++)
            {
                var row = levels.Slice(offset + (y * HevcEncoderWorkspace.BlockStride), nTbS);
                row.CopyTo(records[(y * nTbS)..]);
                for (var x = 0; x < nTbS; x++)
                {
                    block[(y * nTbS) + x] = row[x];
                }
            }

            workspace.RecordCount += count;
        }
        else
        {
            var head = workspace.Records[workspace.RecordPosition++];
            var count = head & 0x3FFF;
            transformSkipFlag = (head & 0x4000) is not 0;
            if (_sink.Mode is not HevcBinSinkMode.None)
            {
                var records = workspace.Records.Slice(workspace.RecordPosition, count);
                for (var i = 0; i < count; i++)
                {
                    block[i] = records[i];
                }

                block[count..].Clear();
            }

            workspace.RecordPosition += count;
        }

        if (_sink.Mode is not HevcBinSinkMode.None)
        {
            WriteResidualCoding(block, log2TrafoSize, cIdx, predModeIntra, transformSkipFlag);
        }
    }

    /// <summary>
    /// Writes the transform coefficient levels of a transform block (clause 7.3.8.11 "Residual coding syntax").
    /// </summary>
    /// <param name="levels"><c>TransCoeffLevel</c>: the rows of the block, one of which at least is not zero.</param>
    /// <param name="log2TrafoSize">The base 2 logarithm of the size of the block.</param>
    /// <param name="cIdx">The colour component of the block.</param>
    /// <param name="predModeIntra">The intra prediction mode of the block; or -1.</param>
    /// <param name="transformSkipFlag"><c>transform_skip_flag</c>.</param>
    private void WriteResidualCoding(scoped ReadOnlySpan<int> levels, int log2TrafoSize, int cIdx, int predModeIntra, bool transformSkipFlag)
    {
        var sink = _sink;
        var contexts = _cabac.Contexts;
        if (_pps.TransformSkipEnabledFlag && !_cuTransquantBypassFlag && log2TrafoSize <= _pps.Log2MaxTransformSkipSize)
        {
            sink.EncodeDecision(ref contexts[HevcContextLayout.TransformSkipFlag + (cIdx is 0 ? 0 : 1)], transformSkipFlag ? 1 : 0);
        }
        else
        {
            Debug.Assert(!transformSkipFlag);
        }

        // The last significant coefficient: the first one in the reverse order of the scan
        var scanIdx = GetScanIdx(log2TrafoSize, cIdx, predModeIntra);
        var subBlockScan = HevcScanOrder.Get(log2TrafoSize - 2, scanIdx);
        var positionScan = HevcScanOrder.Get(2, scanIdx);
        var lastSubBlock = -1;
        var lastScanPos = -1;
        for (var i = subBlockScan.Length - 1; i >= 0 && lastSubBlock < 0; i--)
        {
            var origin = (HevcScanOrder.Y(subBlockScan[i]) << (2 + log2TrafoSize)) + (HevcScanOrder.X(subBlockScan[i]) << 2);
            for (var n = 15; n >= 0; n--)
            {
                if (levels[origin + (HevcScanOrder.Y(positionScan[n]) << log2TrafoSize) + HevcScanOrder.X(positionScan[n])] is not 0)
                {
                    lastSubBlock = i;
                    lastScanPos = n;
                    break;
                }
            }
        }

        Debug.Assert(lastSubBlock >= 0, "A block that is coded has a level.");
        var lastSignificantCoeffX = (HevcScanOrder.X(subBlockScan[lastSubBlock]) << 2) + HevcScanOrder.X(positionScan[lastScanPos]);
        var lastSignificantCoeffY = (HevcScanOrder.Y(subBlockScan[lastSubBlock]) << 2) + HevcScanOrder.Y(positionScan[lastScanPos]);
        if (scanIdx is HevcScanOrder.Vertical)
        {
            // Equation 7-78
            (lastSignificantCoeffX, lastSignificantCoeffY) = (lastSignificantCoeffY, lastSignificantCoeffX);
        }

        // last_sig_coeff_x_prefix, last_sig_coeff_y_prefix (clause 9.3.4.2.3), then the suffixes
        GetLastSigCoeffPrefixContext(log2TrafoSize, cIdx, out var ctxOffset, out var ctxShift);
        var cMax = (log2TrafoSize << 1) - 1;
        GetLastSigCoeffSyntax(lastSignificantCoeffX, out var lastSigCoeffXPrefix, out var lastSigCoeffXSuffix, out var lengthX);
        GetLastSigCoeffSyntax(lastSignificantCoeffY, out var lastSigCoeffYPrefix, out var lastSigCoeffYSuffix, out var lengthY);
        for (var k = 0; k < lastSigCoeffXPrefix; k++)
        {
            sink.EncodeDecision(ref contexts[HevcContextLayout.LastSigCoeffXPrefix + (k >> ctxShift) + ctxOffset], 1);
        }

        if (lastSigCoeffXPrefix < cMax)
        {
            sink.EncodeDecision(ref contexts[HevcContextLayout.LastSigCoeffXPrefix + (lastSigCoeffXPrefix >> ctxShift) + ctxOffset], 0);
        }

        for (var k = 0; k < lastSigCoeffYPrefix; k++)
        {
            sink.EncodeDecision(ref contexts[HevcContextLayout.LastSigCoeffYPrefix + (k >> ctxShift) + ctxOffset], 1);
        }

        if (lastSigCoeffYPrefix < cMax)
        {
            sink.EncodeDecision(ref contexts[HevcContextLayout.LastSigCoeffYPrefix + (lastSigCoeffYPrefix >> ctxShift) + ctxOffset], 0);
        }

        if (lengthX > 0)
        {
            sink.EncodeBypassBits((uint)lastSigCoeffXSuffix, lengthX);
        }

        if (lengthY > 0)
        {
            sink.EncodeBypassBits((uint)lastSigCoeffYSuffix, lengthY);
        }

        var subBlocksPerSide = 1 << (log2TrafoSize - 2);
        Span<byte> codedSubBlockFlag = stackalloc byte[64];
        Span<int> subBlock = stackalloc int[16];
        Span<byte> significant = stackalloc byte[16];
        Span<int> absLevel = stackalloc int[16];
        var signHidingAllowed = _pps.SignDataHidingEnabledFlag && !_cuTransquantBypassFlag;
        var greater1Ctx = 1;
        for (var i = lastSubBlock; i >= 0; i--)
        {
            var xS = HevcScanOrder.X(subBlockScan[i]);
            var yS = HevcScanOrder.Y(subBlockScan[i]);
            var rightCsbf = xS < subBlocksPerSide - 1 ? codedSubBlockFlag[(yS * 8) + xS + 1] : 0;
            var belowCsbf = yS < subBlocksPerSide - 1 ? codedSubBlockFlag[((yS + 1) * 8) + xS] : 0;

            // The levels of the sub-block, in the order of the scan
            var origin = (yS << (2 + log2TrafoSize)) + (xS << 2);
            var any = 0;
            for (var n = 0; n < 16; n++)
            {
                subBlock[n] = levels[origin + (HevcScanOrder.Y(positionScan[n]) << log2TrafoSize) + HevcScanOrder.X(positionScan[n])];
                any |= subBlock[n];
            }

            var inferSbDcSigCoeffFlag = false;
            if (i < lastSubBlock && i > 0)
            {
                // coded_sub_block_flag (clause 9.3.4.2.4)
                var ctxInc = Math.Min(rightCsbf + belowCsbf, 1) + (cIdx is 0 ? 0 : 2);
                sink.EncodeDecision(ref contexts[HevcContextLayout.CodedSubBlockFlag + ctxInc], any is not 0 ? 1 : 0);
                if (any is 0)
                    continue;

                inferSbDcSigCoeffFlag = true;
            }

            codedSubBlockFlag[(yS * 8) + xS] = 1;

            // sig_coeff_flag (clause 9.3.4.2.5)
            var prevCsbf = rightCsbf + (belowCsbf << 1);
            var numSig = 0;
            var first = 15;
            if (i == lastSubBlock)
            {
                significant[numSig++] = (byte)lastScanPos;
                first = lastScanPos - 1;
            }

            ReadOnlySpan<byte> sigCtxs = SigCtxTable.AsSpan(GetSigCtxIndex(log2TrafoSize, cIdx, scanIdx, (xS | yS) is not 0, prevCsbf), 16);
            for (var n = first; n >= 0; n--)
            {
                if (n is 0 && inferSbDcSigCoeffFlag)
                {
                    // The sub-block is coded and none of its other coefficients is significant
                    significant[numSig++] = 0;
                    break;
                }

                int sigCtx = sigCtxs[n];
                var sigCoeffFlag = subBlock[n] is not 0;
                sink.EncodeDecision(ref contexts[HevcContextLayout.SigCoeffFlag + (cIdx is 0 ? sigCtx : 27 + sigCtx)], sigCoeffFlag ? 1 : 0);
                if (sigCoeffFlag)
                {
                    significant[numSig++] = (byte)n;
                    inferSbDcSigCoeffFlag = false;
                }
            }

            if (numSig is 0)
                continue;

            // coeff_abs_level_greater1_flag (clause 9.3.4.2.6): the first 8 significant coefficients of the sub-block
            var ctxSet = i is 0 || cIdx > 0 ? 0 : 2;
            if (greater1Ctx is 0)
            {
                ctxSet++;
            }

            greater1Ctx = 1;
            var lastGreater1Index = -1;
            var greater1Count = Math.Min(numSig, 8);
            var signs = 0u;
            var sumAbsLevel = 0;
            for (var k = 0; k < numSig; k++)
            {
                var level = subBlock[significant[k]];
                absLevel[k] = Math.Abs(level);
                sumAbsLevel += absLevel[k];
                signs = (signs << 1) | (level < 0 ? 1u : 0u);
            }

            for (var k = 0; k < greater1Count; k++)
            {
                var ctxInc = (ctxSet * 4) + Math.Min(3, greater1Ctx) + (cIdx is 0 ? 0 : 16);
                var flag = absLevel[k] > 1 ? 1 : 0;
                sink.EncodeDecision(ref contexts[HevcContextLayout.CoeffAbsLevelGreater1Flag + ctxInc], flag);
                if (flag is not 0)
                {
                    greater1Ctx = 0;
                    if (lastGreater1Index < 0)
                    {
                        lastGreater1Index = k;
                    }
                }
                else if (greater1Ctx > 0)
                {
                    greater1Ctx++;
                }
            }

            // coeff_abs_level_greater2_flag (clause 9.3.4.2.7): the first coefficient that is greater than 1
            if (lastGreater1Index >= 0)
            {
                sink.EncodeDecision(ref contexts[HevcContextLayout.CoeffAbsLevelGreater2Flag + ctxSet + (cIdx is 0 ? 0 : 4)], absLevel[lastGreater1Index] > 2 ? 1 : 0);
            }

            // coeff_sign_flag: the sign of the last coefficient of the sub-block is the parity of the sum of the levels when
            // the first and the last one are more than 3 scan positions apart
            var signHidden = signHidingAllowed && significant[0] - significant[numSig - 1] > 3;
            if (signHidden)
            {
                Debug.Assert(((sumAbsLevel & 1) is 1) == ((signs & 1) is 1), "The parity of the levels is not the hidden sign.");
                signs >>= 1;
            }

            sink.EncodeBypassBits(signs, signHidden ? numSig - 1 : numSig);

            // coeff_abs_level_remaining (clause 9.3.3.11)
            var cLastAbsLevel = 0;
            var cLastRiceParam = 0;
            for (var k = 0; k < numSig; k++)
            {
                var baseLevel = k < 8 ? (k == lastGreater1Index ? 3 : 2) : 1;
                if (absLevel[k] < baseLevel)
                    continue;

                // Equations 9-24 and 9-25
                var cRiceParam = Math.Min(cLastRiceParam + (cLastAbsLevel > 3 * (1 << cLastRiceParam) ? 1 : 0), 4);
                WriteCoeffAbsLevelRemaining(absLevel[k] - baseLevel, cRiceParam);
                cLastAbsLevel = absLevel[k];
                cLastRiceParam = cRiceParam;
            }
        }
    }

    // Equations 7-74 to 7-77: the prefix and the suffix of the position of the last significant coefficient
    private static void GetLastSigCoeffSyntax(int position, out int prefix, out int suffix, out int length)
    {
        if (position < 4)
        {
            prefix = position;
            suffix = 0;
            length = 0;
            return;
        }

        var log2 = 31 - int.LeadingZeroCount(position);
        prefix = (2 * log2) + ((position >> (log2 - 1)) & 1);
        length = (prefix >> 1) - 1;
        suffix = position - ((1 << length) * (2 + (prefix & 1)));
    }

    // The binarization of coeff_abs_level_remaining (clause 9.3.3.11)
    private readonly void WriteCoeffAbsLevelRemaining(int value, int cRiceParam)
    {
        var prefix = value >> cRiceParam;
        if (prefix <= 3)
        {
            _sink.EncodeBypassBits((uint)((1 << (prefix + 1)) - 2), prefix + 1);
            _sink.EncodeBypassBits((uint)(value - (prefix << cRiceParam)), cRiceParam);
            return;
        }

        // A prefix of more than 3 bins, then a suffix of prefix - 3 + cRiceParam bins
        prefix = 4;
        while (value >= ((1L << (prefix - 2)) + 2) << cRiceParam)
        {
            prefix++;
        }

        _sink.EncodeBypassBits((uint)((1L << (prefix + 1)) - 2), prefix + 1);
        _sink.EncodeBypassBits((uint)(value - (((1 << (prefix - 3)) + 2) << cRiceParam)), prefix - 3 + cRiceParam);
    }
}
