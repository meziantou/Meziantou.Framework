using System.Diagnostics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Encodes pictures as an HEVC bitstream (Rec. ITU-T H.265): the parameter sets, which are final when the encoder is
/// created, then one access unit for each picture, made of one slice segment.
/// </summary>
/// <remarks>
/// <para>
/// The encoder shares the state and the processes of the decoder: the parameter sets and the slice segment headers are
/// written, then read back by the parsers of the decoder; the blocks are predicted, scaled, transformed and filtered by
/// the methods that decode them; and the syntax elements of the slice segment data are written beside the methods that
/// read them (<see cref="HevcSliceCoder"/>). <see cref="Reconstruction"/> is therefore the picture that a decoder
/// outputs.
/// </para>
/// <para>
/// A lossless stream bypasses the scaling and the transform of every coding unit (<c>cu_transquant_bypass_flag</c>),
/// with the tools of the version 1 of the specification only; a lossy one uses these tools as well. The profile is the
/// narrowest one that has the format of the pictures (<see cref="HevcSequenceParameters"/>).
/// </para>
/// </remarks>
internal sealed class HevcEncoder : IDisposable
{
    private readonly AllocationScope _scope;
    private readonly HevcEncoderSettings _settings;
    private readonly HevcEncoderDecisions _decisions;
    private readonly HevcSequenceParameterSet _sps;
    private readonly HevcPictureParameterSet _pps;
    private readonly HevcCabacDecoder _contexts = new();
    private readonly HevcBinSink _sink = new();
    private readonly HevcPicture?[] _pictures = new HevcPicture?[2];
    private readonly bool _deferred;
    private PooledPayloadWriter? _parameterSets;
    private PooledPayloadWriter? _rbsp;
    private PooledPayloadWriter? _payload;
    private HevcPictureLayout? _layout;
    private HevcPictureState? _state;
    private HevcSliceScratch? _scratch;
    private HevcEncoderWorkspace? _workspace;
    private HevcReferencePictureLists? _references;
    private HevcSliceSegmentHeader? _header;
    private HevcPicture? _reconstruction;
    private readonly int _vpsLength;
    private readonly int _spsLength;
    private readonly int _ppsLength;
    private int _current;
    private int _row;
    private int _frameIndex = -1;
    private int _picOrderCnt;
    private int _nalUnitType;
    private Phase _phase;

    private enum Phase
    {
        Idle,
        Start,
        Search,
        Filter,
        Write,
        Finish,
        Done,
    }

    /// <summary>Creates an encoder.</summary>
    /// <param name="scope">The memory of the operation.</param>
    /// <param name="settings">The format of the pictures, and how they are encoded.</param>
    /// <exception cref="ArgumentOutOfRangeException">A setting is out of range.</exception>
    /// <exception cref="ImageResourceLimitException">The buffers exceed the live-allocation limit.</exception>
    public HevcEncoder(AllocationScope scope, in HevcEncoderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.Width, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(settings.Width, HevcSequenceParameterSet.MaxPictureDimension - 7);
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.Height, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(settings.Height, HevcSequenceParameterSet.MaxPictureDimension - 7);
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.Quality, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(settings.Quality, 100);
        ArgumentOutOfRangeException.ThrowIfNegative(settings.KeyFrameInterval);
        if (settings.BitDepth is not (8 or 10 or 12))
            throw new ArgumentOutOfRangeException(nameof(settings), "The bit depth is 8, 10 or 12.");

        if (settings.ChromaFormatIdc is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(settings), "The chroma format is 4:0:0, 4:2:0, 4:2:2 or 4:4:4.");

        _scope = scope;
        _settings = settings;
        _decisions = HevcEncoderDecisions.Create(settings);
        var sequence = !settings.StillPicture && settings.KeyFrameInterval is not 1;
        var tools = HevcCodingTools.StrongIntraSmoothing;
        if (_decisions.SampleAdaptiveOffset)
        {
            tools |= HevcCodingTools.SampleAdaptiveOffset;
        }

        if (sequence && _decisions.AsymmetricPartitions)
        {
            tools |= HevcCodingTools.AsymmetricMotionPartitions;
        }

        if (sequence && _decisions.TemporalMvp)
        {
            tools |= HevcCodingTools.TemporalMvp;
        }

        var parameters = HevcSequenceParameters.Create(settings, tools) with
        {
            CtbLog2SizeY = _decisions.CtbLog2Size,
            MaxTransformHierarchyDepthIntra = _decisions.MaxTransformHierarchyDepthIntra,
            MaxTransformHierarchyDepthInter = _decisions.MaxTransformHierarchyDepthInter,
        };
        if (!parameters.IsValid)
            throw new ArgumentOutOfRangeException(nameof(settings), "The settings are not those of a stream that the HEVC encoder writes.");

        Parameters = parameters;
        _deferred = _decisions.SampleAdaptiveOffset;
        try
        {
            // The parameter sets, written then read back: the encoder works with what a decoder reads
            _parameterSets = new PooledPayloadWriter(scope);
            _rbsp = new PooledPayloadWriter(scope);
            _payload = new PooledPayloadWriter(scope);
            HevcHeaderWriter.WriteVideoParameterSet(_rbsp, parameters);
            _vpsLength = HevcNalWriter.WriteNalUnit(_parameterSets, HevcConstants.NalVps, 0, _rbsp.WrittenSpan);
            _rbsp.Reset();
            HevcHeaderWriter.WriteSequenceParameterSet(_rbsp, parameters);
            _sps = HevcSequenceParameterSet.ParseRbsp(_rbsp.WrittenSpan);
            _spsLength = HevcNalWriter.WriteNalUnit(_parameterSets, HevcConstants.NalSps, 0, _rbsp.WrittenSpan);
            _rbsp.Reset();
            var pictureParameters = new HevcPictureParameters(_decisions.SliceQp)
            {
                TransquantBypassEnabled = _decisions.Lossless,
                DeblockingFilterDisabled = _decisions.Lossless,
                SignDataHidingEnabled = _decisions.SignDataHiding,
                TransformSkipEnabled = _decisions.TransformSkip,
            };
            HevcHeaderWriter.WritePictureParameterSet(_rbsp, _sps, pictureParameters);
            _pps = HevcPictureParameterSet.ParseRbsp(_rbsp.WrittenSpan);
            _ppsLength = HevcNalWriter.WriteNalUnit(_parameterSets, HevcConstants.NalPps, 0, _rbsp.WrittenSpan);
            _rbsp.Reset();

            _layout = HevcPictureLayout.Create(scope, _sps, _pps);
            _state = HevcPictureState.Create(scope, _sps);
            _scratch = new HevcSliceScratch(scope);
            _workspace = new HevcEncoderWorkspace(scope, _sps, settings.Width, settings.Height);
            for (var i = 0; i < (sequence ? 2 : 1); i++)
            {
                var picture = new HevcPicture(_sps);
                picture.Allocate(scope);
                _pictures[i] = picture;
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Gets the parameters of the sequence: its profile and its level, the size of the coded pictures and the size of the pictures that a decoder outputs.</summary>
    public HevcSequenceParameters Parameters { get; }

    /// <summary>Gets the NAL unit of the video parameter set: its header and its payload, with the emulation prevention bytes.</summary>
    public ReadOnlyMemory<byte> VideoParameterSet => GetParameterSets().Slice(0, _vpsLength);

    /// <summary>Gets the NAL unit of the sequence parameter set.</summary>
    public ReadOnlyMemory<byte> SequenceParameterSet => GetParameterSets().Slice(_vpsLength, _spsLength);

    /// <summary>Gets the NAL unit of the picture parameter set.</summary>
    public ReadOnlyMemory<byte> PictureParameterSet => GetParameterSets().Slice(_vpsLength + _spsLength, _ppsLength);

    /// <summary>
    /// Gets the access unit of the picture: the NAL unit of its slice segment, preceded by its length on
    /// <see cref="HevcNalWriter.NalLengthSize"/> bytes, without the parameter sets. The memory is valid until the next
    /// picture begins.
    /// </summary>
    public ReadOnlyMemory<byte> Payload => _phase is Phase.Done && _payload is not null ? _payload.WrittenMemory : throw new InvalidOperationException("No picture is encoded.");

    /// <summary>Gets a value indicating whether the last picture is an instantaneous decoding refresh picture, which refers to no other picture.</summary>
    public bool IsKeyFrame { get; private set; }

    /// <summary>
    /// Gets the picture that a decoder outputs for the last picture: its planes have the size of the coded picture, which
    /// includes the samples that the conformance window crops.
    /// </summary>
    public HevcPicture Reconstruction => _phase is Phase.Done && _reconstruction is not null ? _reconstruction : throw new InvalidOperationException("No picture is encoded.");

    /// <summary>
    /// Gets the plane <paramref name="plane"/> of the picture to encode (0 for Y, 1 for Cb, 2 for Cr): the caller writes
    /// the <see cref="Av1Plane.Width"/> x <see cref="Av1Plane.Height"/> samples of the picture before
    /// <see cref="BeginFrame"/>. The encoder pads the plane to the size of the coded picture.
    /// </summary>
    public Av1Plane GetSourcePlane(int plane) => GetWorkspace().GetSourcePlane(plane);

    /// <summary>Starts the encoding of the picture whose samples are in the source planes.</summary>
    public void BeginFrame()
    {
        if (_phase is not (Phase.Idle or Phase.Done))
            throw new InvalidOperationException("The previous picture is not complete.");

        if (_settings.StillPicture && _frameIndex >= 0)
            throw new InvalidOperationException("A still picture has a single picture.");

        var workspace = GetWorkspace();
        for (var plane = 0; plane < _sps.ComponentCount; plane++)
        {
            ExtendPlane(workspace.GetSourcePlane(plane), plane);
        }

        _row = 0;
        _phase = Phase.Start;
    }

    /// <summary>
    /// Forgets the pictures that were encoded, so that the next one starts a new stream with the same parameter sets: an
    /// instantaneous decoding refresh picture, coded as by a new encoder (the tiles of a grid are coded one after the
    /// other by one encoder). The payload and the reconstruction of the last picture are no longer valid.
    /// </summary>
    public void Reset()
    {
        if (_phase is not (Phase.Idle or Phase.Done))
            throw new InvalidOperationException("The previous picture is not complete.");

        _reconstruction = null;
        _references = null;
        _header = null;
        _current = 0;
        _frameIndex = -1;
        _picOrderCnt = 0;
        _phase = Phase.Idle;
    }

    /// <summary>Encodes a part of the picture: a row of coding tree units, or one of the steps that start and finish the picture.</summary>
    /// <returns><see langword="true"/> when the picture is complete.</returns>
    /// <exception cref="ImageResourceLimitException">A buffer exceeds the live-allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation is canceled.</exception>
    public bool Encode(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (_phase)
        {
            case Phase.Start:
                StartPicture();
                _phase = Phase.Search;
                return false;

            case Phase.Search:
                CreateSliceCoder(cancellationToken).SearchCodingTreeUnits(_row, write: !_deferred);
                if (++_row == _sps.PicHeightInCtbsY)
                {
                    _row = 0;
                    _phase = _deferred ? Phase.Filter : Phase.Finish;
                }

                return false;

            case Phase.Filter:
                // The sample adaptive offset is decided from the deblocked picture
                HevcDeblockingFilter.Apply(_pictures[_current]!, _layout!, _state!, cancellationToken);
                _contexts.InitializeContexts(_header!.InitType, _header.SliceQpY);
                _sink.Encoder.Initialize(_rbsp!);
                _workspace!.RecordPosition = 0;
                _phase = Phase.Write;
                return false;

            case Phase.Write:
                CreateSliceCoder(cancellationToken).WriteCodingTreeUnits(_row);
                if (++_row == _sps.PicHeightInCtbsY)
                {
                    _phase = Phase.Finish;
                }

                return false;

            case Phase.Finish:
                FinishPicture(cancellationToken);
                _phase = Phase.Done;
                return true;

            default:
                throw new InvalidOperationException("No picture is being encoded.");
        }
    }

    public void Dispose()
    {
        _phase = Phase.Idle;
        _reconstruction = null;
        for (var i = 0; i < _pictures.Length; i++)
        {
            _pictures[i]?.Release();
            _pictures[i] = null;
        }

        _workspace?.Dispose();
        _workspace = null;
        _scratch?.Dispose();
        _scratch = null;
        _state?.Dispose();
        _state = null;
        _layout?.Dispose();
        _layout = null;
        _payload?.Dispose();
        _payload = null;
        _rbsp?.Dispose();
        _rbsp = null;
        _parameterSets?.Dispose();
        _parameterSets = null;
    }

    private ReadOnlyMemory<byte> GetParameterSets() => (_parameterSets ?? throw new ObjectDisposedException(nameof(HevcEncoder))).WrittenMemory;

    private HevcEncoderWorkspace GetWorkspace() => _workspace ?? throw new ObjectDisposedException(nameof(HevcEncoder));

    private HevcSliceCoder CreateSliceCoder(CancellationToken cancellationToken)
    {
        return new HevcSliceCoder(_layout!, _header!, _pictures[_current]!, _state!, _contexts, _scratch!, _references, _workspace!, _sink, _decisions, cancellationToken);
    }

    // The samples beyond the picture, up to the size of the coded picture, repeat its last column and its last row
    private void ExtendPlane(Av1Plane plane, int cIdx)
    {
        var codedWidth = _sps.PicWidthInLumaSamples / (cIdx is 0 ? 1 : _sps.SubWidthC);
        var codedHeight = _sps.PicHeightInLumaSamples / (cIdx is 0 ? 1 : _sps.SubHeightC);
        var samples = plane.Samples;
        for (var y = 0; y < plane.Height; y++)
        {
            var row = samples.Slice(y * plane.Stride, codedWidth);
            row[plane.Width..].Fill(row[plane.Width - 1]);
        }

        var last = samples.Slice((plane.Height - 1) * plane.Stride, codedWidth);
        for (var y = plane.Height; y < codedHeight; y++)
        {
            last.CopyTo(samples[(y * plane.Stride)..]);
        }
    }

    // The slice segment header of the picture, and the state of a picture that a decoder starts
    private void StartPicture()
    {
        var workspace = _workspace!;
        var state = _state!;
        var rbsp = _rbsp!;
        var index = _frameIndex + 1;
        var interval = _settings.KeyFrameInterval;

        // An interval of 0 is the one of a sequence whose only key frame is the first picture
        var isKeyFrame = index is 0 || _settings.StillPicture || (interval > 0 && index % interval is 0);
        var picOrderCnt = isKeyFrame ? 0 : _picOrderCnt + 1;
        var previous = _pictures[_current];
        var current = isKeyFrame || _pictures[1] is null ? _current : 1 - _current;
        var picture = _pictures[current]!;
        picture.PicOrderCntVal = picOrderCnt;

        var slice = isKeyFrame ? HevcSliceParameters.CreateKeyFrame(_decisions.SliceQp) : HevcSliceParameters.CreateInterFrame(picOrderCnt, _decisions.SliceQp);
        slice = slice with
        {
            SliceSaoLuma = _sps.SampleAdaptiveOffsetEnabledFlag,
            SliceSaoChroma = _sps.SampleAdaptiveOffsetEnabledFlag && _sps.ChromaArrayType is not 0,
            SliceTemporalMvpEnabled = !isKeyFrame && _sps.SpsTemporalMvpEnabledFlag,
            FiveMinusMaxNumMergeCand = isKeyFrame ? 0 : 5 - _decisions.MergeCandidates,
        };
        rbsp.Reset();
        HevcHeaderWriter.WriteSliceSegmentHeader(rbsp, _sps, _pps, slice);
        var nal = HevcNalUnitHeader.Read([(byte)(slice.NalUnitType << 1), 1]);
        var header = HevcSliceSegmentHeader.Parse(rbsp.WrittenSpan, nal, _layout!, sliceHeader: null);
        Debug.Assert(header.DataOffset == rbsp.Length);

        // Clause 8.3.4: the only reference picture of a P slice is the previous picture
        HevcReferencePictureLists? references = null;
        if (!isKeyFrame)
        {
            references = new HevcReferencePictureLists();
            references.Add(0, previous!, isLongTerm: false);
            references.Complete(picOrderCnt);
            if (header.SliceTemporalMvpEnabledFlag)
            {
                references.CollocatedPicture = previous;
            }
        }

        state.BeginPicture();
        state.Slices.Add(header);
        state.SliceReferences.Add(references);
        if (references is not null)
        {
            state.EnsureMotion();
        }

        state.LastQpY = header.SliceQpY;
        _contexts.InitializeContexts(header.InitType, header.SliceQpY);
        _sink.Rate = 0;
        _sink.Encoder.BinCountsInNalUnits = 0;
        if (!_deferred)
        {
            _sink.Encoder.Initialize(rbsp);
        }

        workspace.RecordCount = 0;
        workspace.RecordPosition = 0;
        _header = header;
        _references = references;
        _current = current;
        _picOrderCnt = picOrderCnt;
        _nalUnitType = slice.NalUnitType;
        _reconstruction = null;
        IsKeyFrame = isKeyFrame;
    }

    // The in-loop filters of the decoder, then the NAL unit of the slice segment
    private void FinishPicture(CancellationToken cancellationToken)
    {
        var picture = _pictures[_current]!;
        var layout = _layout!;
        var state = _state!;
        var rbsp = _rbsp!;
        var payload = _payload!;
        Debug.Assert(!_sink.Encoder.IsActive, "The slice segment data end with end_of_slice_segment_flag.");
        if (_deferred)
        {
            HevcSampleAdaptiveOffset.Apply(_scope, picture, layout, state, cancellationToken);
        }
        else
        {
            HevcDeblockingFilter.Apply(picture, layout, state, cancellationToken);
        }

        if (_sps.SpsTemporalMvpEnabledFlag)
        {
            // The motion of the picture, for the pictures that refer to it: a picture of the encoder is used again
            picture.AllocateCollocatedMotion(_scope).Clear();
            state.StoreCollocatedMotion(picture, _sps);
        }

        // Clause 9.3.5.7: cabac_zero_words after a slice segment that has many bins for its size
        var rawMinCuBits = (1 << (2 * _sps.MinCbLog2SizeY)) * (_sps.BitDepth + (2 * _sps.BitDepth / (_sps.SubWidthC * _sps.SubHeightC)));
        var picSizeInMinCbsY = (long)(_sps.PicWidthInLumaSamples >> _sps.MinCbLog2SizeY) * (_sps.PicHeightInLumaSamples >> _sps.MinCbLog2SizeY);
        var zeroWords = HevcCabacEncoder.GetCabacZeroWordCount(_sink.Encoder.BinCountsInNalUnits, rbsp.Length + HevcNalUnitHeader.Length, rawMinCuBits, picSizeInMinCbsY);
        while (zeroWords > 0)
        {
            var count = (int)Math.Min(zeroWords, 1 << 20);
            rbsp.WriteZeros(2 * count);
            zeroWords -= count;
        }

        payload.Reset();
        HevcNalWriter.WriteLengthPrefixed(payload, _nalUnitType, 0, rbsp.WrittenSpan);
        rbsp.Reset();
        _reconstruction = picture;
        _frameIndex++;
    }
}
