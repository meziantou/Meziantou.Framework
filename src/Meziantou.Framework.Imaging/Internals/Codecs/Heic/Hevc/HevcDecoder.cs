namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes the NAL units of an HEVC bitstream into pictures (Rec. ITU-T H.265, clause 8 "Decoding process"). The decoder
/// keeps the state that pictures depend on (the parameter sets and the decoded picture buffer) between the access units it
/// is given, and decodes on the calling thread.
/// </summary>
/// <remarks>
/// <para>
/// The access units are given one at a time, in decoding order, with <see cref="SetInput"/>; after each one, and after
/// <see cref="EndOfSequence"/>, <see cref="DecodeNextOutputPicture"/> is called until it returns <see langword="null"/>.
/// The pictures come out in output order over the whole bitstream, which is not the decoding order: the pictures that
/// wait for their turn are in the decoded picture buffer, whose size is the one of the sequence parameter set. A picture
/// that is returned belongs to the caller, which releases it with <see cref="HevcPicture.Release"/>; its samples stay
/// valid until then, whatever the decoder does.
/// </para>
/// <para>
/// With <see cref="MaxDegreeOfParallelism"/> above 1, the substreams of a slice segment of a picture of at least
/// <see cref="RowBands.MinPixelsForParallelism"/> samples that start at an entry point (its tiles, and its rows of coding
/// tree blocks of the wavefront) are decoded by that many workers at once (<see cref="HevcSubstreamWorkers"/>): the
/// pictures and the errors are those of the decoding on the calling thread. The in-loop filters run on the calling thread.
/// </para>
/// <para>After an exception the state of the decoder is undefined: it must be disposed.</para>
/// <para>
/// The decoder decodes the base layer: the NAL units of the other layers (<c>nuh_layer_id</c> greater than 0) are
/// ignored, and <see cref="HasEnhancementLayers"/> tells that some were met. A picture is complete, and nothing is
/// concealed: a picture whose slice segments do not cover every coding tree block is
/// <see cref="InvalidImageContentException"/>.
/// </para>
/// </remarks>
internal sealed class HevcDecoder : IDisposable
{
    private static readonly AsyncLocal<bool> TestIgnoreMinimumSizeValue = new();

    private readonly AllocationScope _scope;
    private readonly InputResourceTracker? _tracker;
    private readonly HevcParameterSetStore _parameterSets = new();
    private readonly HevcDecodedPictureBuffer _dpb = new();
    private readonly HevcCabacDecoder _cabac = new();
    private readonly HevcRbspBuffer _rbsp;
    private readonly HevcSeiMessages _sei = new();
    private HevcSliceScratch? _scratch;
    private HevcSubstreamWorkers? _substreamWorkers;
    private HevcPictureLayout? _layout;
    private HevcPictureState? _state;
    private HevcPicture? _current;
    private HevcSliceSegmentHeader? _previousSliceSegment;
    private HevcReferencePictureLists? _references;
    private HevcSequenceParameterSet? _sequenceSps;
    private HevcTools _tools;
    private byte[] _data = [];
    private int _position;
    private int _end;
    private int _nalLengthSize = 4;
    private int _accessUnitIndex = -1;
    private bool _firstPicture = true;
    private bool _afterEndOfSequence;
    private bool _noRaslOutputFlag;
    private bool _endOfSequence;
    private int _lastOutputAccessUnitIndex = -1;
    private int _prevPicOrderCntLsb;
    private int _prevPicOrderCntMsb;
    private bool _disposed;

    /// <summary>Creates a decoder whose buffers are rented from <paramref name="scope"/>.</summary>
    /// <param name="scope">The allocation scope of the operation.</param>
    /// <param name="tracker">The per-input limit accounting, which the pixels of the pictures that are not output are charged to; or <see langword="null"/>.</param>
    public HevcDecoder(AllocationScope scope, InputResourceTracker? tracker = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _scope = scope;
        _tracker = tracker;
        _rbsp = new HevcRbspBuffer(scope);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the pictures decoded on the current asynchronous flow use the workers they are
    /// given whatever their size, instead of the minimum size of <see cref="RowBands.MinPixelsForParallelism"/> samples.
    /// Tests decode small streams by several workers.
    /// </summary>
    internal static bool TestIgnoreMinimumSize
    {
        get => TestIgnoreMinimumSizeValue.Value;
        set => TestIgnoreMinimumSizeValue.Value = value;
    }

    /// <summary>
    /// Gets or sets the largest number of workers that decode the substreams of a slice segment (tiles and rows of the
    /// wavefront that start at an entry point); 1, the default, decodes them on the calling thread.
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = 1;

    /// <summary>Gets or sets the in-loop filters applied to the pictures: a diagnostic, <see cref="HevcInLoopFilters.All"/> otherwise.</summary>
    public HevcInLoopFilters InLoopFilters { get; set; } = HevcInLoopFilters.All;

    /// <summary>Gets the coding tools used by the pictures decoded so far: a diagnostic.</summary>
    public HevcTools Tools => _tools | (_state?.Tools ?? HevcTools.None);

    /// <summary>Gets whether the input had NAL units of another layer than the base layer, which were ignored.</summary>
    public bool HasEnhancementLayers { get; private set; }

    /// <summary>
    /// Gets the greatest number of pictures that precede a picture in decoding order and follow it in output order
    /// (<c>sps_max_num_reorder_pics</c> of the highest sub-layer of the active sequence parameter set): after that many
    /// access units plus one, <see cref="DecodeNextOutputPicture"/> has returned a picture, unless the pictures are not
    /// output. 0 before the first picture.
    /// </summary>
    public int MaxNumReorderPictures => _layout is { } layout ? layout.Sps.MaxNumReorderPics[layout.Sps.HighestTid] : 0;

    /// <summary>Gets the mastering display colour volume of the last SEI message that has one, or <see langword="null"/>.</summary>
    public HevcMasteringDisplayColourVolume? MasteringDisplayColourVolume => _sei.MasteringDisplayColourVolume;

    /// <summary>Gets the content light level of the last SEI message that has one, or <see langword="null"/>.</summary>
    public HevcContentLightLevel? ContentLightLevel => _sei.ContentLightLevel;

    /// <summary>Gets the alpha channel information of the last SEI message that has one, or <see langword="null"/>.</summary>
    public HevcAlphaChannelInfo? AlphaChannelInfo => _sei.AlphaChannelInfo;

    /// <summary>
    /// Gives the decoder a parameter set or a supplemental enhancement information NAL unit that is not part of an access
    /// unit, such as the ones of the decoder configuration record of a file.
    /// </summary>
    /// <param name="nalUnit">A VPS, SPS, PPS or SEI NAL unit: its two-byte header, then its payload with its emulation prevention bytes, without length.</param>
    /// <exception cref="InvalidImageContentException">The NAL unit is of another type, or it is malformed or truncated.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The parameter set uses a feature that is not supported.</exception>
    public void AddParameterSet(ReadOnlySpan<byte> nalUnit)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var header = HevcNalUnitHeader.Read(nalUnit);
        if (header.NalUnitType is not (HevcConstants.NalVps or HevcConstants.NalSps or HevcConstants.NalPps or HevcConstants.NalPrefixSei or HevcConstants.NalSuffixSei))
            throw HevcErrors.Invalid("a NAL unit of a decoder configuration is not a parameter set.");

        if (header.NuhLayerId > 0)
        {
            HasEnhancementLayers = true;
            return;
        }

        ReadNonVclNalUnit(header, _rbsp.Load(nalUnit[HevcNalUnitHeader.Length..]));
        NoteEmulationPrevention();
    }

    /// <summary>
    /// Takes the parameter sets and the supplemental enhancement information that another decoder was given with
    /// <see cref="AddParameterSet"/>, without reading them again: the decoders of the pictures that share a decoder
    /// configuration record (the tiles of a grid) start from the same state, whose cost does not depend on the size of
    /// the record.
    /// </summary>
    /// <param name="source">A decoder that was given no access unit.</param>
    public void CopyParameterSetsFrom(HevcDecoder source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(source._disposed, source);
        _parameterSets.CopyFrom(source._parameterSets);
        _sei.MasteringDisplayColourVolume = source._sei.MasteringDisplayColourVolume;
        _sei.ContentLightLevel = source._sei.ContentLightLevel;
        _sei.AlphaChannelInfo = source._sei.AlphaChannelInfo;
        HasEnhancementLayers |= source.HasEnhancementLayers;
        _tools |= source._tools;
    }

    /// <summary>
    /// Sets the access unit to decode: <paramref name="length"/> bytes of <paramref name="data"/> from
    /// <paramref name="offset"/>, a sequence of NAL units that are each preceded by their length. They are read by the
    /// following calls of <see cref="DecodeNextOutputPicture"/>; the data must stay unchanged until they are consumed, and
    /// are not modified.
    /// </summary>
    /// <param name="data">The array that holds the access unit.</param>
    /// <param name="offset">The position of the access unit in the array.</param>
    /// <param name="length">The number of bytes of the access unit.</param>
    /// <param name="nalLengthSize">The number of bytes of the length of a NAL unit: 1, 2 or 4.</param>
    public void SetInput(byte[] data, int offset, int length, int nalLengthSize)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, data.Length - length);
        if (nalLengthSize is not (1 or 2 or 4))
            throw new ArgumentOutOfRangeException(nameof(nalLengthSize), nalLengthSize, "The length of a NAL unit has 1, 2 or 4 bytes.");

        if (_endOfSequence)
            throw new InvalidOperationException("The end of the sequence was signaled.");

        _data = data;
        _position = offset;
        _end = offset + length;
        _nalLengthSize = nalLengthSize;
        _accessUnitIndex++;
    }

    /// <summary>
    /// Tells the decoder that there is no more input: the pictures that wait for their turn in the decoded picture buffer
    /// are output by the following calls of <see cref="DecodeNextOutputPicture"/>.
    /// </summary>
    public void EndOfSequence()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _endOfSequence = true;
    }

    /// <summary>
    /// Decodes the NAL units of the input up to the next picture to output, in output order (clause C.5.2).
    /// </summary>
    /// <returns>The picture, which the caller releases; or <see langword="null"/> when the input is consumed and no picture is ready.</returns>
    /// <exception cref="InvalidImageContentException">The bitstream is malformed or truncated.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The bitstream uses a feature that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">A picture exceeds a resource limit.</exception>
    public HevcPicture? DecodeNextOutputPicture(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (true)
        {
            if (_dpb.TakeOutput() is { } output)
            {
                if (output.AccessUnitIndex < _lastOutputAccessUnitIndex)
                {
                    _tools |= HevcTools.PictureReordering;
                }

                _lastOutputAccessUnitIndex = Math.Max(_lastOutputAccessUnitIndex, output.AccessUnitIndex);
                return output;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!HevcNalUnitHeader.TryReadNext(_data.AsSpan(0, _end), _nalLengthSize, ref _position, out var offset, out var length))
            {
                // The access unit is consumed: its picture is complete
                if (_current is not null)
                {
                    FinishPicture(cancellationToken);
                    continue;
                }

                if (!_endOfSequence)
                    return null;

                _dpb.Flush();
                if (!_dpb.HasOutput)
                    return null;

                continue;
            }

            ReadNalUnit(offset, length, cancellationToken);
        }
    }

    private void ReadNalUnit(int offset, int length, CancellationToken cancellationToken)
    {
        var header = HevcNalUnitHeader.Read(_data.AsSpan(offset, length));
        if (header.NuhLayerId > 0)
        {
            HasEnhancementLayers = true;
            return;
        }

        if (header.NalUnitType is HevcConstants.NalEos or HevcConstants.NalEob)
        {
            // The last picture of the coded video sequence is complete. Clause 8.1.3: the picture that follows is an IRAP
            // picture with NoRaslOutputFlag equal to 1, which decides what becomes of the pictures that wait for their
            // turn (clause C.5.2.2): the end of sequence NAL unit does not output them
            if (_current is not null)
            {
                FinishPicture(cancellationToken);
            }

            _afterEndOfSequence = true;
            if (header.NalUnitType is HevcConstants.NalEob)
            {
                // The picture that follows is the first one of another bitstream: every picture of this one is output
                _dpb.Flush();
                _dpb.Clear();
                _firstPicture = true;
            }

            return;
        }

        if (!header.IsVcl)
        {
            ReadNonVclNalUnit(header, _rbsp.Load(_data.AsSpan(offset + HevcNalUnitHeader.Length, length - HevcNalUnitHeader.Length)));
            NoteEmulationPrevention();
            return;
        }

        // The reserved types of VCL NAL units are ignored (clause 7.4.2.2)
        if (header.NalUnitType is > HevcConstants.NalRaslR and < HevcConstants.NalBlaWLp or > HevcConstants.NalCra)
            return;

        // Clause 8.1.3: the RASL pictures associated with an IRAP picture with NoRaslOutputFlag equal to 1 are not output
        // and cannot be decoded, since they refer to pictures that are not in the bitstream
        if (header.IsRasl && _noRaslOutputFlag)
            return;

        _rbsp.Load(_data, offset + HevcNalUnitHeader.Length, length - HevcNalUnitHeader.Length);
        NoteEmulationPrevention();
        ReadSliceSegment(header, cancellationToken);
    }

    private void NoteEmulationPrevention()
    {
        if (_rbsp.EmulationPreventionByteCount > 0)
        {
            _tools |= HevcTools.EmulationPrevention;
        }
    }

    private void ReadNonVclNalUnit(in HevcNalUnitHeader header, ReadOnlySpan<byte> rbsp)
    {
        switch (header.NalUnitType)
        {
            case HevcConstants.NalVps:
                _parameterSets.AddVps(rbsp);
                break;

            case HevcConstants.NalSps:
                _parameterSets.AddSps(rbsp);
                break;

            case HevcConstants.NalPps:
                _parameterSets.AddPps(rbsp);
                break;

            case HevcConstants.NalPrefixSei:
                HevcSeiReader.Read(rbsp, isPrefix: true, _layout?.Sps.ComponentCount ?? 0, _sei);
                break;

            case HevcConstants.NalSuffixSei:
                // The hash of a suffix SEI NAL unit is the one of the picture whose slice segments precede it
                _sei.DecodedPictureHash = null;
                HevcSeiReader.Read(rbsp, isPrefix: false, _current?.PlaneCount ?? 0, _sei);
                if (_current is not null && _sei.DecodedPictureHash is not null)
                {
                    _current.DecodedPictureHash = _sei.DecodedPictureHash;
                }

                break;

            default:
                // Access unit delimiters, filler data and the reserved and unspecified types do not take part in the
                // decoding of the pictures
                break;
        }
    }

    // Clause 7.3.2.9 "Slice segment layer RBSP syntax"
    private void ReadSliceSegment(in HevcNalUnitHeader nal, CancellationToken cancellationToken)
    {
        var rbsp = _rbsp.Span;
        var picParameterSetId = HevcSliceSegmentHeader.ReadPicParameterSetId(rbsp, nal, out var firstSliceSegmentInPicFlag);
        if (firstSliceSegmentInPicFlag)
        {
            if (_current is not null)
            {
                FinishPicture(cancellationToken);
            }

            ActivateParameterSets(picParameterSetId);
        }
        else if (_current is null)
        {
            throw HevcErrors.Invalid("a picture does not start with its first slice segment.");
        }

        var layout = _layout!;
        if (!ReferenceEquals(_parameterSets.GetPps(picParameterSetId), layout.Pps) || !ReferenceEquals(_parameterSets.GetSps(layout.Pps.SeqParameterSetId), layout.Sps))
            throw HevcErrors.Invalid("the parameter sets change within a picture.");

        var header = HevcSliceSegmentHeader.Parse(rbsp, nal, layout, firstSliceSegmentInPicFlag ? null : _previousSliceSegment);
        if (firstSliceSegmentInPicFlag)
        {
            BeginPicture(nal, header);
        }

        var state = _state!;
        if (!header.DependentSliceSegmentFlag)
        {
            _references = header.SliceType is HevcConstants.SliceI ? null : ConstructReferencePictureLists(header);
            state.Slices.Add(header);
            state.SliceReferences.Add(_references);
            if (state.Slices.Count > 1)
            {
                state.Tools |= HevcTools.MultipleSlices;
            }
        }

        _previousSliceSegment = header;
        var coder = new HevcSliceCoder(layout, header, _current!, state, _cabac, _scratch!, _rbsp, _references, cancellationToken, GetSubstreamWorkers(layout, header), MaxDegreeOfParallelism);
        coder.DecodeSliceSegment();
    }

    // The workers of the substreams of a slice segment, for a picture that is large enough to share between them
    private HevcSubstreamWorkers? GetSubstreamWorkers(HevcPictureLayout layout, HevcSliceSegmentHeader header)
    {
        var sps = layout.Sps;
        if (MaxDegreeOfParallelism <= 1 || header.EntryPointOffsetMinus1.Length is 0)
            return null;

        if (!TestIgnoreMinimumSize && (long)sps.PicWidthInLumaSamples * sps.PicHeightInLumaSamples < RowBands.MinPixelsForParallelism)
            return null;

        return _substreamWorkers ??= new HevcSubstreamWorkers(_scope);
    }

    // Clause 8.3.4 "Decoding process for reference picture lists construction"
    private HevcReferencePictureLists ConstructReferencePictureLists(HevcSliceSegmentHeader header)
    {
        var current = _current!;
        var state = _state!;
        var lists = new HevcReferencePictureLists();
        var before = _dpb.StCurrBefore;
        var after = _dpb.StCurrAfter;
        var longTerm = _dpb.LtCurr;
        var numPicTotalCurr = before.Count + after.Count + longTerm.Count;
        if (numPicTotalCurr is 0)
            throw HevcErrors.Invalid("a P or B slice has no reference picture.");

        for (var list = 0; list < (header.SliceType is HevcConstants.SliceB ? 2 : 1); list++)
        {
            var count = (list is 0 ? header.NumRefIdxL0ActiveMinus1 : header.NumRefIdxL1ActiveMinus1) + 1;
            var modified = list is 0 ? header.RefPicListModificationFlagL0 : header.RefPicListModificationFlagL1;
            var entries = list is 0 ? header.ListEntryL0 : header.ListEntryL1;
            var first = list is 0 ? before : after;
            var second = list is 0 ? after : before;
            for (var rIdx = 0; rIdx < count; rIdx++)
            {
                // RefPicListTemp0 and RefPicListTemp1 (Equations 8-8 and 8-10) are the three subsets one after the other,
                // as many times as it takes
                var index = modified ? entries[rIdx] : rIdx;
                index %= numPicTotalCurr;
                HevcPicture? picture;
                var isLongTerm = false;
                if (index < first.Count)
                {
                    picture = first[index];
                }
                else if (index < first.Count + second.Count)
                {
                    picture = second[index - first.Count];
                }
                else
                {
                    picture = longTerm[index - first.Count - second.Count];
                    isLongTerm = true;
                }

                // Nothing is concealed: a picture that a slice refers to is in the decoded picture buffer
                if (picture is null)
                    throw HevcErrors.Invalid("a slice refers to a picture that is not in the decoded picture buffer.");

                if (!ReferenceEquals(picture.Sps, current.Sps))
                    throw HevcErrors.Invalid("a slice refers to a picture of another coded video sequence.");

                lists.Add(list, picture, isLongTerm);
                state.Tools |= isLongTerm ? HevcTools.LongTermReferences : HevcTools.None;
            }

            state.Tools |= (modified ? HevcTools.ReferenceListModification : HevcTools.None) | (count > 1 ? HevcTools.MultipleReferences : HevcTools.None);
        }

        lists.Complete(current.PicOrderCntVal);
        if (header.SliceTemporalMvpEnabledFlag)
        {
            // Clause 8.5.3.2.8: ColPic
            var collocatedList = header.SliceType is HevcConstants.SliceB && !header.CollocatedFromL0Flag ? 1 : 0;
            if (header.CollocatedRefIdx >= lists.GetCount(collocatedList))
                throw HevcErrors.Invalid("collocated_ref_idx is out of range.");

            lists.CollocatedPicture = lists.GetPicture(collocatedList, header.CollocatedRefIdx);
        }

        state.EnsureMotion();
        return lists;
    }

    // Clause 7.4.2.4.2: the parameter sets that the first slice segment of a picture refers to become the active ones
    private void ActivateParameterSets(int picParameterSetId)
    {
        var pps = _parameterSets.GetPps(picParameterSetId) ?? throw HevcErrors.Invalid("a slice refers to a picture parameter set that is not in the bitstream.");
        var sps = _parameterSets.GetSps(pps.SeqParameterSetId) ?? throw HevcErrors.Invalid("a picture parameter set refers to a sequence parameter set that is not in the bitstream.");
        if (_parameterSets.GetVps(sps.VideoParameterSetId) is null)
            throw HevcErrors.Invalid("a sequence parameter set refers to a video parameter set that is not in the bitstream.");

        if (_layout is not null && ReferenceEquals(_layout.Pps, pps) && ReferenceEquals(_layout.Sps, sps))
            return;

        // Clause 7.4.3.2.1: field_seq_flag tells that the pictures are fields, which are not frames to output as they are
        if (sps.Vui is { FieldSeqFlag: true })
            throw HevcErrors.Unsupported("field sequences");

        // The canvas limits apply to the pictures that the decoder outputs. A coded picture is a whole number of minimum
        // coding blocks (clause 7.4.3.2.1), and its conformance window removes that padding: the limit of an image of 1080
        // rows must not refuse its pictures of 1088 rows. A window that removes more than the padding hides samples that
        // are decoded all the same: the whole picture then counts
        var minCbSizeY = 1 << sps.MinCbLog2SizeY;
        _scope.Limits.EnsureCanvasWithinLimits(
            sps.PicWidthInLumaSamples - sps.OutputWidth < minCbSizeY ? sps.OutputWidth : sps.PicWidthInLumaSamples,
            sps.PicHeightInLumaSamples - sps.OutputHeight < minCbSizeY ? sps.OutputHeight : sps.PicHeightInLumaSamples);
        var previousSps = _layout?.Sps;
        _layout?.Dispose();
        _layout = null;
        _layout = HevcPictureLayout.Create(_scope, sps, pps);
        if (!ReferenceEquals(previousSps, sps) || _state is null)
        {
            if (_state is not null)
            {
                _tools |= _state.Tools;
                _state.Dispose();
                _state = null;
            }

            _state = HevcPictureState.Create(_scope, sps);
        }

        _scratch ??= new HevcSliceScratch(_scope);
    }

    // Clause 8.1.3 "Decoding process for a coded picture with nuh_layer_id equal to 0", before the slice segments
    private void BeginPicture(in HevcNalUnitHeader nal, HevcSliceSegmentHeader header)
    {
        var sps = _layout!.Sps;
        if ((_firstPicture || _afterEndOfSequence) && !nal.IsIrap)
            throw HevcErrors.Invalid("the first picture of a coded video sequence is not an intra random access point picture.");

        // NoRaslOutputFlag: 1 for the IDR and BLA pictures, for the first picture and for the one that follows an end of
        // sequence NAL unit
        var isIrapWithNoRaslOutputFlag = nal.IsIrap && (nal.NalUnitType is not HevcConstants.NalCra || _firstPicture || _afterEndOfSequence);
        if (nal.IsIrap)
        {
            _noRaslOutputFlag = isIrapWithNoRaslOutputFlag;
        }

        // Clause 7.4.2.4.2: a sequence parameter set is activated by an IRAP picture with NoRaslOutputFlag equal to 1, for
        // the whole coded video sequence
        if (isIrapWithNoRaslOutputFlag)
        {
            _sequenceSps = sps;
        }
        else if (!ReferenceEquals(_sequenceSps, sps))
        {
            throw HevcErrors.Invalid("the sequence parameter set changes within a coded video sequence.");
        }

        // Clause 8.3.1 "Decoding process for picture order count"
        var maxPicOrderCntLsb = 1 << sps.Log2MaxPicOrderCntLsb;
        var picOrderCntMsb = 0;
        if (!isIrapWithNoRaslOutputFlag)
        {
            if (header.SlicePicOrderCntLsb < _prevPicOrderCntLsb && _prevPicOrderCntLsb - header.SlicePicOrderCntLsb >= maxPicOrderCntLsb / 2)
            {
                picOrderCntMsb = _prevPicOrderCntMsb + maxPicOrderCntLsb;
            }
            else if (header.SlicePicOrderCntLsb > _prevPicOrderCntLsb && header.SlicePicOrderCntLsb - _prevPicOrderCntLsb > maxPicOrderCntLsb / 2)
            {
                picOrderCntMsb = _prevPicOrderCntMsb - maxPicOrderCntLsb;
            }
            else
            {
                picOrderCntMsb = _prevPicOrderCntMsb;
            }
        }

        var picOrderCntVal = picOrderCntMsb + header.SlicePicOrderCntLsb;
        if (nal.TemporalId is 0 && !nal.IsRasl && !nal.IsRadl && !nal.IsSubLayerNonReference)
        {
            // prevTid0Pic
            _prevPicOrderCntLsb = header.SlicePicOrderCntLsb;
            _prevPicOrderCntMsb = picOrderCntMsb;
        }

        // Clauses 8.3.2 and C.5.2.2: the reference picture set, then the output and the removal of pictures
        _dpb.ApplyReferencePictureSet(picOrderCntVal, header, sps, isIrapWithNoRaslOutputFlag);
        var noOutputOfPriorPicsFlag = nal.NalUnitType is HevcConstants.NalCra || header.NoOutputOfPriorPicsFlag;
        _dpb.BeforePicture(sps, isIrapWithNoRaslOutputFlag && !_firstPicture, noOutputOfPriorPicsFlag);
        if (_dpb.TakeDiscardedPixels() is > 0 and var discardedPixels)
        {
            // The pictures that are decoded and never output are not charged by the sink of the displayed pictures
            _tracker?.ChargeHiddenPixels(discardedPixels);
        }

        var picture = new HevcPicture(sps)
        {
            PicOrderCntVal = picOrderCntVal,
            AccessUnitIndex = _accessUnitIndex,

            // Clause 8.1.3: a RASL picture associated with an IRAP picture with NoRaslOutputFlag equal to 1 is not output
            PicOutputFlag = header.PicOutputFlag,
        };
        picture.Allocate(_scope);
        _current = picture;
        _state!.BeginPicture();
        _firstPicture = false;
        _afterEndOfSequence = false;
        _previousSliceSegment = null;
        _references = null;
        if (!picture.PicOutputFlag)
        {
            // A picture that is not output is not charged by the sink of the displayed pictures
            _tracker?.ChargeHiddenPixels((long)sps.PicWidthInLumaSamples * sps.PicHeightInLumaSamples);
        }
    }

    // Clause 8.1.3, after the slice segments: the in-loop filters (clause 8.7), then the picture is stored in the decoded
    // picture buffer (clause C.5.2.3)
    private void FinishPicture(CancellationToken cancellationToken)
    {
        var picture = _current!;
        var layout = _layout!;
        var state = _state!;
        if (state.NextCtbAddrInTs != layout.Sps.PicSizeInCtbsY)
            throw HevcErrors.Invalid("a picture is incomplete.");

        if (InLoopFilters.HasFlag(HevcInLoopFilters.Deblocking))
        {
            HevcDeblockingFilter.Apply(picture, layout, state, cancellationToken);
        }

        if (InLoopFilters.HasFlag(HevcInLoopFilters.Sao))
        {
            HevcSampleAdaptiveOffset.Apply(_scope, picture, layout, state, cancellationToken);
        }

        if (layout.Sps.SpsTemporalMvpEnabledFlag)
        {
            state.StoreCollocatedMotion(picture, layout.Sps);
        }

        _current = null;
        _references = null;
        _dpb.AfterPicture(picture, layout.Sps);
        picture.Release();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _current?.Release();
        _current = null;
        _dpb.Dispose();
        _layout?.Dispose();
        _layout = null;
        _state?.Dispose();
        _state = null;
        _scratch?.Dispose();
        _scratch = null;
        _substreamWorkers?.Dispose();
        _substreamWorkers = null;
        _rbsp.Dispose();
    }
}
