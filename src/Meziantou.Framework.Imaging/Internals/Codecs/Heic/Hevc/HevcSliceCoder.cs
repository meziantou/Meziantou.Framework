namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes the data of a slice segment (Rec. ITU-T H.265, clauses 7.3.8 "Slice segment data syntax" and 9.3 "CABAC parsing
/// process for slice segment data"), and constructs the picture prior to the in-loop filter process as the syntax elements
/// are read (clauses 8.4 "Decoding process for coding units coded in intra prediction mode", 8.5 "Decoding process for
/// coding units coded in inter prediction mode" and 8.6 "Scaling, transformation and array construction process prior to
/// deblocking filter process"). The names are those of the
/// specification; a comment names the clause that a method implements.
/// </summary>
/// <remarks>
/// The substreams of a slice segment (tiles, rows of coding tree blocks of the wavefront) are decoded one after the other,
/// in the order of the bitstream: the entry points are only compared with the positions where the substreams end, and no
/// read nor allocation depends on their values. With workers (<see cref="HevcSubstreamWorkers"/>), the substreams that
/// start at an entry point inside the data are decoded concurrently from there (<c>HevcSliceCoder.Substreams.cs</c>), and
/// the entry points are then compared, in order, with the positions where the substreams end.
/// </remarks>
internal ref partial struct HevcSliceCoder
{
    private readonly HevcPictureLayout _layout;
    private readonly HevcSequenceParameterSet _sps;
    private readonly HevcPictureParameterSet _pps;
    private readonly HevcSliceSegmentHeader _header;
    private readonly HevcPicture _picture;
    private readonly HevcPictureState _state;
    private readonly HevcCabacDecoder _cabac;
    private readonly HevcSliceScratch _scratch;
    private readonly HevcRbspBuffer _rbsp;
    private readonly HevcReferencePictureLists? _refs;
    private readonly Span<HevcMotion> _motion;
    private readonly CancellationToken _cancellationToken;
    private readonly HevcSubstreamWorkers? _workers;
    private readonly int _maxWorkers;
    private readonly Span<byte> _cuFlags;
    private readonly Span<sbyte> _qpYMap;
    private readonly Span<byte> _intraPredModeY;
    private readonly Span<byte> _edgeFlags;
    private readonly Span<int> _sliceIndex;
    private readonly ReadOnlySpan<int> _ctbAddrRsToTs;
    private readonly ReadOnlySpan<int> _ctbAddrTsToRs;
    private readonly ReadOnlySpan<int> _tileId;
    private readonly int _sliceIdx;
    private readonly int _chromaArrayType;
    private readonly int _ctbLog2SizeY;
    private readonly int _minCbLog2SizeY;
    private readonly int _minTbLog2SizeY;
    private readonly int _bitDepth;
    private readonly int _widthInMinCbs;
    private readonly int _widthIn4x4;
    private readonly int _picWidthInCtbsY;
    private readonly int _end;
    private HevcTools _tools;
    private int _ctbAddrInTs;
    private int _ctbAddrInRs;
    private int _entryPointIndex;
    private long _nextSubsetSourceOffset;
    private uint _statCoeff;

    // The coding unit being decoded
    private bool _cuTransquantBypassFlag;
    private bool _isIntra;
    private int _partMode;
    private int _ctDepth;
    private int _interSplitFlag;
    private bool _explicitRdpcmFlag;
    private bool _explicitRdpcmDirFlag;
    private int _intraSplitFlag;
    private int _maxTrafoDepth;
    private int _log2CbSize;
    private int _xCb;
    private int _yCb;

    // intra_chroma_pred_mode and IntraPredModeC of the four prediction blocks of the coding unit: one byte each
    private uint _intraChromaPredMode;
    private uint _intraPredModeC;

    // The quantization parameters
    private bool _isCuQpDeltaCoded;
    private int _cuQpDeltaVal;
    private bool _isCuChromaQpOffsetCoded;
    private int _cuQpOffsetCb;
    private int _cuQpOffsetCr;
    private int _qpYPred;
    private int _qpY;
    private int _resScaleVal;

    // The encoder: its buffers, where the bins go, what it searches, and whether a coding unit is written from the
    // description of the coding unit being searched, which records it, or from its records
    private readonly HevcEncoderWorkspace _workspace = null!;
    private readonly HevcBinSink _sink = null!;
    private readonly HevcEncoderDecisions _decisions;
    private bool _storing;

    /// <summary>Creates the decoder of a slice segment of the picture being decoded.</summary>
    /// <param name="layout">The parameter sets of the picture.</param>
    /// <param name="header">The header of the slice segment.</param>
    /// <param name="picture">The picture being decoded.</param>
    /// <param name="state">The state of the picture being decoded.</param>
    /// <param name="cabac">The arithmetic decoder.</param>
    /// <param name="scratch">The working buffers.</param>
    /// <param name="rbsp">The payload of the NAL unit of the slice segment.</param>
    /// <param name="references">The reference picture lists of the slice of the slice segment; <see langword="null"/> for an I slice.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests, checked at every coding tree unit.</param>
    /// <param name="workers">The memory of the workers that decode the substreams that start at an entry point; <see langword="null"/> to decode them on the calling thread.</param>
    /// <param name="maxWorkers">The largest number of workers.</param>
    public HevcSliceCoder(HevcPictureLayout layout, HevcSliceSegmentHeader header, HevcPicture picture, HevcPictureState state, HevcCabacDecoder cabac, HevcSliceScratch scratch, HevcRbspBuffer rbsp, HevcReferencePictureLists? references, CancellationToken cancellationToken, HevcSubstreamWorkers? workers = null, int maxWorkers = 1)
    {
        _workers = workers;
        _maxWorkers = maxWorkers;
        _layout = layout;
        _sps = layout.Sps;
        _pps = layout.Pps;
        _header = header;
        _picture = picture;
        _state = state;
        _cabac = cabac;
        _scratch = scratch;
        _rbsp = rbsp;
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
        _end = rbsp.Offset + rbsp.Length;
        _tools = state.Tools;
    }

    /// <summary>
    /// Decodes the slice segment data (clause 7.3.8.1 "General slice segment data syntax"), and the trailing bits of the
    /// slice segment.
    /// </summary>
    /// <exception cref="InvalidImageContentException">The data are malformed or truncated.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The slice uses a feature that is not supported.</exception>
    public void DecodeSliceSegment()
    {
        if (_refs is not null)
        {
            _tools |= _header.SliceType is HevcConstants.SliceB ? HevcTools.BSlices : HevcTools.PSlices;
        }

        _ctbAddrInRs = _header.SliceSegmentAddress;
        _ctbAddrInTs = _ctbAddrRsToTs[_ctbAddrInRs];

        // A decoder that conceals nothing needs every coding tree block of the picture, in decoding order
        if (_ctbAddrInTs != _state.NextCtbAddrInTs)
            throw HevcErrors.Invalid("a slice segment does not start where the previous one ends.");

        var position = _rbsp.Offset + _header.DataOffset;
        _nextSubsetSourceOffset = _rbsp.ToSourceOffset(_header.DataOffset);
        _entryPointIndex = 0;
        if (!_header.DependentSliceSegmentFlag)
        {
            // Clause 8.6.1: the first quantization group of a slice is predicted from SliceQpY
            _state.LastQpY = _header.SliceQpY;
        }

        _qpY = _state.LastQpY;
        if (_workers is not null && _maxWorkers > 1 && _header.EntryPointOffsetMinus1.Length > 0)
        {
            switch (DecodeSubstreamsConcurrently(position, out var next))
            {
                case HevcSubstreamOutcome.EndOfSliceSegment:
                    _state.Tools = _tools;
                    return;

                case HevcSubstreamOutcome.EndOfSubset:
                    // The substreams that do not start at an entry point inside the data are decoded on the calling thread
                    InitializeSubstream(next, isSliceSegmentStart: false);
                    break;

                default:
                    InitializeSubstream(position, isSliceSegmentStart: true);
                    break;
            }
        }
        else
        {
            InitializeSubstream(position, isSliceSegmentStart: true);
        }

        var picSizeInCtbsY = _sps.PicSizeInCtbsY;
        while (true)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            DecodeCodingTreeUnit();
            var endOfSliceSegmentFlag = _cabac.DecodeTerminate() is not 0;

            // Clause 9.3.1: the storage process after the second coding tree block of a row of a tile
            if (_pps.EntropyCodingSyncEnabledFlag
                && (_ctbAddrInRs % _picWidthInCtbsY is 1 || (_ctbAddrInRs > 1 && _tileId[_ctbAddrInTs] != _tileId[_ctbAddrRsToTs[_ctbAddrInRs - 2]])))
            {
                _cabac.SaveContexts(_state.ContextsWpp);
                StoreStatCoeff(_state.StatCoeffWpp);
            }

            _ctbAddrInTs++;
            if (endOfSliceSegmentFlag)
                break;

            if (_ctbAddrInTs >= picSizeInCtbsY)
                throw HevcErrors.Invalid("a slice segment goes beyond the last coding tree block of the picture.");

            _ctbAddrInRs = _ctbAddrTsToRs[_ctbAddrInTs];
            var startsTile = _pps.TilesEnabledFlag && _tileId[_ctbAddrInTs] != _tileId[_ctbAddrInTs - 1];
            var startsRow = _pps.EntropyCodingSyncEnabledFlag
                && (_ctbAddrInRs % _picWidthInCtbsY is 0 || _tileId[_ctbAddrInTs] != _tileId[_ctbAddrRsToTs[_ctbAddrInRs - 1]]);
            if (startsTile || startsRow)
            {
                // end_of_subset_one_bit, then byte_alignment( ): the bit that terminates the arithmetic codeword is
                // alignment_bit_equal_to_one
                if (_cabac.DecodeTerminate() is 0)
                    throw HevcErrors.Invalid("end_of_subset_one_bit is not 1.");

                InitializeSubstream(ReadAlignmentZeroBits(), isSliceSegmentStart: false);
            }
        }

        _state.NextCtbAddrInTs = _ctbAddrInTs;
        _state.LastQpY = _qpY;
        if (_pps.DependentSliceSegmentsEnabledFlag)
        {
            // Clause 9.3.1: the storage process at the end of a slice segment
            _cabac.SaveContexts(_state.ContextsDs);
            StoreStatCoeff(_state.StatCoeffDs);
        }

        // rbsp_slice_segment_trailing_bits( ): the stop bit is the bit that terminates the arithmetic codeword; it is
        // followed by alignment bits, then by cabac_zero_words
        var trailing = ReadAlignmentZeroBits();
        if (_rbsp.Array.AsSpan(trailing, _end - trailing).ContainsAnyExcept((byte)0))
            throw HevcErrors.Invalid("data follow the trailing bits of a slice segment.");

        _state.Tools = _tools;
    }

    // Checks the bits that follow an arithmetic codeword up to the next byte, which are zero, and returns the position of
    // that byte
    private readonly int ReadAlignmentZeroBits()
    {
        var bitPosition = _cabac.BitPosition;
        var bytePosition = (int)((bitPosition + 7) >> 3);
        var padding = (int)(((long)bytePosition << 3) - bitPosition);
        if (padding > 0 && (_rbsp.Array[bytePosition - 1] & ((1 << padding) - 1)) is not 0)
            throw HevcErrors.Invalid("the alignment bits of a slice segment are not zero.");

        return bytePosition;
    }

    // Clause 9.3.2.1: the context variables and the arithmetic decoding engine at the start of the slice segment data, of
    // a tile and of a row of coding tree blocks of the wavefront
    private void InitializeSubstream(int position, bool isSliceSegmentStart)
    {
        if (!isSliceSegmentStart)
        {
            CheckEntryPoint(position);
        }

        InitializeSubstream(position, isSliceSegmentStart, _state.ContextsWpp, _state.StatCoeffWpp);
    }

    // InitializeSubstream, with the context variables that the synchronization process of the wavefront reads
    private void InitializeSubstream(int position, bool isSliceSegmentStart, ReadOnlySpan<byte> contextsWpp, ReadOnlySpan<byte> statCoeffWpp)
    {
        var firstInTile = _ctbAddrInTs is 0 || _tileId[_ctbAddrInTs] != _tileId[_ctbAddrInTs - 1];
        var firstInRow = _pps.EntropyCodingSyncEnabledFlag
            && (_ctbAddrInRs % _picWidthInCtbsY is 0 || _tileId[_ctbAddrInTs] != _tileId[_ctbAddrRsToTs[_ctbAddrInRs - 1]]);
        if (firstInTile)
        {
            InitializeContexts();
            _tools |= _pps.TilesEnabledFlag ? HevcTools.Tiles : HevcTools.None;
        }
        else if (firstInRow)
        {
            // The spatial neighbour T is the coding tree block above and to the right (Equation 9-3)
            var x0 = (_ctbAddrInRs % _picWidthInCtbsY) << _ctbLog2SizeY;
            var y0 = (_ctbAddrInRs / _picWidthInCtbsY) << _ctbLog2SizeY;
            _tools |= HevcTools.Wavefront;
            if (IsAvailable(x0, y0, x0 + (1 << _ctbLog2SizeY), y0 - (1 << _ctbLog2SizeY)))
            {
                // Clause 9.3.2.5 "Synchronization process"
                _cabac.LoadContexts(contextsWpp);
                LoadStatCoeff(statCoeffWpp);
            }
            else
            {
                InitializeContexts();
            }
        }
        else if (isSliceSegmentStart && _header.DependentSliceSegmentFlag)
        {
            _cabac.LoadContexts(_state.ContextsDs);
            LoadStatCoeff(_state.StatCoeffDs);
            _tools |= HevcTools.DependentSliceSegments;
        }
        else
        {
            InitializeContexts();
        }

        if (firstInTile || firstInRow)
        {
            // Clause 8.6.1: the first quantization group of a tile, and of a row of coding tree blocks of the wavefront, is
            // predicted from SliceQpY
            _qpY = _header.SliceQpY;
        }

        _cabac.Initialize(_rbsp.Array, position, _end);
    }

    private void InitializeContexts()
    {
        _cabac.InitializeContexts(_header.InitType, _header.SliceQpY);
        _statCoeff = 0;
    }

    // The substreams follow each other: the entry point of a substream is where the previous one ends (clause 7.4.7.1,
    // entry_point_offset_minus1). Every substream that starts in the slice segment has an entry point, which is the
    // position that the decoding has reached: anything else is an error. The entry points that are left when the slice
    // segment ends are not read: an encoder in use (kvazaar, with one slice segment per row of coding tree blocks) writes
    // in a slice segment header the entry points of the rows that follow the slice segment, which are beyond its data.
    private void CheckEntryPoint(int position)
    {
        var entryPoints = _header.EntryPointOffsetMinus1;
        if (_entryPointIndex >= entryPoints.Length)
            throw HevcErrors.Invalid("a substream of a slice segment has no entry point.");

        _nextSubsetSourceOffset += entryPoints[_entryPointIndex++] + 1;
        if (_nextSubsetSourceOffset > _rbsp.Length + _rbsp.EmulationPreventionByteCount || _rbsp.Offset + _rbsp.FromSourceOffset((int)_nextSubsetSourceOffset) != position)
            throw HevcErrors.Invalid("an entry point is not the start of a substream.");
    }

    private readonly void StoreStatCoeff(Span<byte> destination)
    {
        for (var k = 0; k < 4; k++)
        {
            destination[k] = (byte)(_statCoeff >> (k * 8));
        }
    }

    private void LoadStatCoeff(ReadOnlySpan<byte> source)
    {
        _statCoeff = source[0] | ((uint)source[1] << 8) | ((uint)source[2] << 16) | ((uint)source[3] << 24);
    }

    // Clause 7.3.8.2 "Coding tree unit syntax"
    private void DecodeCodingTreeUnit()
    {
        var rx = _ctbAddrInRs % _picWidthInCtbsY;
        var ry = _ctbAddrInRs / _picWidthInCtbsY;
        _sliceIndex[_ctbAddrInRs] = _sliceIdx;
        if (_header.SliceSaoLumaFlag || _header.SliceSaoChromaFlag)
        {
            ReadSao(rx, ry);
        }

        CodingQuadtree(rx << _ctbLog2SizeY, ry << _ctbLog2SizeY, _ctbLog2SizeY, 0);
    }

    /// <summary>
    /// The derivation process for z-scan order block availability (clause 6.4.1) for a current block of the coding tree
    /// block being decoded.
    /// </summary>
    private readonly bool IsAvailable(int xCurr, int yCurr, int xNbY, int yNbY)
    {
        if (xNbY < 0 || yNbY < 0 || xNbY >= _sps.PicWidthInLumaSamples || yNbY >= _sps.PicHeightInLumaSamples)
            return false;

        var ctbAddrNbRs = ((yNbY >> _ctbLog2SizeY) * _picWidthInCtbsY) + (xNbY >> _ctbLog2SizeY);
        if (ctbAddrNbRs == _ctbAddrInRs)
        {
            // In the coding tree block of the current block: the same slice and the same tile, and the order of the
            // blocks is the z-scan order inside the coding tree block. MinTbAddrZs (Equation 6-10) of two minimum
            // transform blocks of one coding tree block differ by their positions in it
            var mask = (1 << (_ctbLog2SizeY - _minTbLog2SizeY)) - 1;
            return HevcPictureLayout.GetZScanIndex((xNbY >> _minTbLog2SizeY) & mask, (yNbY >> _minTbLog2SizeY) & mask) <= HevcPictureLayout.GetZScanIndex((xCurr >> _minTbLog2SizeY) & mask, (yCurr >> _minTbLog2SizeY) & mask);
        }

        // In another coding tree block: it precedes the current one in tile scan, and is in its slice and in its tile
        var ctbAddrNbTs = _ctbAddrRsToTs[ctbAddrNbRs];
        // The slice of a block of another tile is not read: the workers write the blocks of other tiles at the same time
        return ctbAddrNbTs < _ctbAddrInTs && _tileId[ctbAddrNbTs] == _tileId[_ctbAddrInTs] && _sliceIndex[ctbAddrNbRs] == _sliceIdx;
    }
}
