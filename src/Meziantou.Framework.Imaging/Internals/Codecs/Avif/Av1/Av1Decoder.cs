namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes a sequence of AV1 open bitstream units into frames (AV1 specification, section 7 "Decoding process"). The decoder
/// keeps the state that frames depend on (the sequence header and the 8 reference frames) between the temporal units it is
/// given, and decodes on the calling thread.
/// </summary>
/// <remarks>
/// <para>After an exception the state of the decoder is undefined: it must be disposed.</para>
/// <para>
/// Scalability (section 7.18.1 "Output process"): the units outside the selected operating point are dropped. When the
/// operating point has several spatial layers, the decoder outputs one frame per temporal unit, the shown frame of the
/// highest spatial layer that the temporal unit holds, which is what a player displays; <see cref="OutputAllLayers"/>
/// outputs every shown frame instead, and <see cref="MaxSpatialLayer"/> leaves the higher layers out.
/// </para>
/// </remarks>
internal sealed class Av1Decoder : IDisposable
{
    private readonly AllocationScope _scope;
    private readonly int _operatingPoint;
    private readonly InputResourceTracker? _tracker;
    private readonly Av1FrameCodingState _coding;
    private readonly Av1SymbolDecoder _symbols = new();
    private Av1SequenceHeader? _sequence;
    private Av1FrameBuffer? _heldOutput;
    private int _heldSpatialId;
    private bool _seenFrameHeader;
    private int _tileNum;
    private byte[] _data = [];
    private int _position;
    private int _end;

    /// <summary>Creates a decoder whose buffers are rented from <paramref name="scope"/>.</summary>
    /// <param name="scope">The allocation scope of the operation.</param>
    /// <param name="tracker">The per-input limit accounting, which the pixels of the frames that are not shown are charged to; or <see langword="null"/>.</param>
    /// <param name="operatingPoint">The operating point to decode (<c>choose_operating_point()</c>).</param>
    public Av1Decoder(AllocationScope scope, InputResourceTracker? tracker = null, int operatingPoint = 0)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _scope = scope;
        _coding = new Av1FrameCodingState(scope);
        _tracker = tracker;
        _operatingPoint = operatingPoint;
    }

    /// <summary>Gets or sets the post-processing filters applied to the frames: a diagnostic, <see cref="Av1InLoopFilters.All"/> otherwise.</summary>
    public Av1InLoopFilters InLoopFilters { get; set; } = Av1InLoopFilters.All;

    /// <summary>
    /// Gets or sets the largest number of workers of the post-processing filters of a frame (<see cref="Av1PostFilter.Apply"/>):
    /// the frames are the same whatever the value. Defaults to 1, the calling thread only.
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = 1;

    /// <summary>
    /// Gets or sets whether every shown frame of the operating point is output, whatever its spatial layer: the output of a
    /// conformance test. Otherwise a temporal unit outputs the frame of its highest spatial layer only.
    /// </summary>
    public bool OutputAllLayers { get; set; }

    /// <summary>
    /// Gets or sets the highest spatial layer to decode (0 to 3): the units of the operating point that belong to a higher
    /// layer are dropped, so that the frame of this layer is the one that is output.
    /// </summary>
    public int MaxSpatialLayer { get; set; } = Av1ObuHeader.MaxSpatialId;

    /// <summary>Gets the spatial layer of the frame that <see cref="DecodeNextOutputFrame"/> returned last.</summary>
    public int OutputSpatialId { get; private set; }

    /// <summary>Gets the coding tools used by the blocks decoded so far: a diagnostic.</summary>
    public Av1Tools Tools => _coding.State.Tools;

    /// <summary>Gets the active sequence header, or <see langword="null"/> before the first one is decoded.</summary>
    public Av1SequenceHeader? Sequence => _sequence;

    /// <summary>
    /// Sets the units to decode: <paramref name="length"/> bytes of <paramref name="data"/> from <paramref name="offset"/>,
    /// which are read by the following calls of <see cref="DecodeNextOutputFrame"/>. The data must stay unchanged until they
    /// are consumed.
    /// </summary>
    public void SetInput(byte[] data, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, data.Length - length);
        _data = data;
        _position = offset;
        _end = offset + length;
    }

    /// <summary>
    /// Decodes the units of the input up to the next frame to output: a frame with <c>show_frame</c> equal to 1, or a frame
    /// shown by <c>show_existing_frame</c>. The frames that are not shown are decoded on the way, and so are the shown
    /// frames of the lower spatial layers of a temporal unit, unless <see cref="OutputAllLayers"/> is set.
    /// </summary>
    /// <returns>The frame, which the caller releases; or <see langword="null"/> at the end of the input.</returns>
    /// <exception cref="InvalidImageContentException">The bitstream is malformed or truncated.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The bitstream uses a feature that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">A frame exceeds a resource limit.</exception>
    public Av1FrameBuffer? DecodeNextOutputFrame(CancellationToken cancellationToken)
    {
        while (_position < _end)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var obu = Av1ObuHeader.Read(_data.AsSpan(_position, _end - _position));
            var isOutside = _sequence is not null && obu.IsOutsideOperatingPoint(_sequence.OperatingPointIdc, MaxSpatialLayer);

            // Section 7.5 "Ordering of OBUs": the units of a frame belong to one temporal unit, so a temporal delimiter
            // cannot come between the header and the last tile group of a frame. This is checked before the held frame is
            // returned, so that the error is not deferred to a call that the caller may never make.
            if (obu.Type is Av1Constants.ObuTemporalDelimiter && _seenFrameHeader)
                throw Av1Errors.Invalid("a temporal delimiter starts a temporal unit before the tiles of the frame are complete.");

            // The frame of the highest spatial layer is known when the temporal unit ends: the unit that starts the next
            // one is read by the next call
            if (_heldOutput is not null && !isOutside && StartsTemporalUnit(obu))
                return TakeHeldOutput();

            var payloadOffset = _position + obu.HeaderLength;
            _position += obu.TotalLength;
            if (isOutside)
                continue;

            Av1FrameBuffer? output = null;
            switch (obu.Type)
            {
                case Av1Constants.ObuSequenceHeader:
                    ReadSequenceHeader(_data.AsSpan(payloadOffset, obu.PayloadLength));
                    break;
                case Av1Constants.ObuFrameHeader:
                    if (_seenFrameHeader)
                        throw Av1Errors.Invalid("a frame header starts before the tiles of the previous frame are complete.");

                    output = ReadFrameHeader(obu, payloadOffset, isFrameObu: false, cancellationToken);
                    break;
                case Av1Constants.ObuRedundantFrameHeader:
                    if (!_seenFrameHeader)
                        throw Av1Errors.Invalid("a redundant frame header has no frame header to repeat.");

                    break;
                case Av1Constants.ObuFrame:
                    if (_seenFrameHeader)
                        throw Av1Errors.Invalid("a frame header starts before the tiles of the previous frame are complete.");

                    output = ReadFrameHeader(obu, payloadOffset, isFrameObu: true, cancellationToken);
                    break;
                case Av1Constants.ObuTileGroup:
                    if (!_seenFrameHeader)
                        throw Av1Errors.Invalid("a tile group has no frame header.");

                    output = ReadTileGroup(payloadOffset, obu.PayloadLength, cancellationToken);
                    break;
                case Av1Constants.ObuTileList:
                    throw Av1Errors.Unsupported("large scale tile decoding");
                default:
                    // Metadata, padding and reserved units do not take part in the decoding of the frames. A temporal delimiter
                    // resets SeenFrameHeader, which is already 0: its placement is checked above
                    break;
            }

            if (output is null)
                continue;

            if (OutputAllLayers || DecodedSpatialLayerCount <= 1)
            {
                OutputSpatialId = obu.SpatialId;
                return output;
            }

            // A higher spatial layer of the temporal unit replaces the frame of the layers below, which is decoded without
            // being shown: its pixels are charged like those of a hidden frame
            var lower = _heldOutput;
            _heldOutput = output;
            _heldSpatialId = obu.SpatialId;
            if (lower is not null)
            {
                var pixels = (long)lower.Header.UpscaledWidth * lower.Header.FrameHeight;
                lower.Release();
                _tracker?.ChargeHiddenPixels(pixels);
            }
        }

        if (_seenFrameHeader)
            throw Av1Errors.Truncated();

        return TakeHeldOutput();
    }

    // The number of spatial layers of the selected operating point that are decoded
    private int DecodedSpatialLayerCount => _sequence is null ? 0 : int.PopCount((_sequence.OperatingPointIdc >> 8) & ((2 << MaxSpatialLayer) - 1) & 0xF);

    // Section 7.5 "Ordering of OBUs": a temporal unit starts with a temporal delimiter, and its units are in non-decreasing
    // order of spatial layer. A layer has one shown frame in a temporal unit, its last one, so a frame of the layer of the
    // held frame, or of a layer below, belongs to the next temporal unit (samples and items have no temporal delimiter)
    private bool StartsTemporalUnit(in Av1ObuHeader obu)
    {
        return obu.Type is Av1Constants.ObuTemporalDelimiter
            || ((obu.Type is Av1Constants.ObuFrameHeader or Av1Constants.ObuFrame) && obu.SpatialId <= _heldSpatialId);
    }

    private Av1FrameBuffer? TakeHeldOutput()
    {
        var output = _heldOutput;
        _heldOutput = null;
        if (output is not null)
            OutputSpatialId = _heldSpatialId;

        return output;
    }

    private void ReadSequenceHeader(ReadOnlySpan<byte> payload)
    {
        var sequence = Av1SequenceHeader.Parse(payload, _operatingPoint);
        if (_sequence is not null && !_sequence.IsSameSequence(sequence))
        {
            // Section 7.5 "Ordering of OBUs": a new coded video sequence starts with a temporal unit, so it cannot start
            // between the header and the tiles of a frame, whose reference frames are the buffers of the slots
            if (_seenFrameHeader)
                throw Av1Errors.Invalid("a sequence header starts a new sequence before the tiles of the frame are complete.");

            // A new coded video sequence does not refer to the frames of the previous one
            _coding.ReleaseSlots();
        }

        _sequence = sequence;
    }

    // Sections 5.9.1 "General frame header OBU syntax" and 5.10 "Frame OBU syntax"
    private Av1FrameBuffer? ReadFrameHeader(in Av1ObuHeader obu, int payloadOffset, bool isFrameObu, CancellationToken cancellationToken)
    {
        var sequence = _sequence ?? throw Av1Errors.Invalid("a frame header has no sequence header.");
        var payload = _data.AsSpan(payloadOffset, obu.PayloadLength);
        var reader = new Av1BitReader(payload);
        var header = Av1FrameHeaderParser.Parse(ref reader, sequence, obu, _coding.Slots, _coding.RefValid);
        if (header.ShowExistingFrame)
        {
            if (isFrameObu)
                throw Av1Errors.Invalid("a frame OBU shows an existing frame.");

            reader.ReadTrailingBits();
            return ShowExistingFrame(header);
        }

        BeginFrame(sequence, header);
        _seenFrameHeader = true;
        _tileNum = 0;
        if (!isFrameObu)
        {
            reader.ReadTrailingBits();
            return null;
        }

        reader.ByteAlign();
        var headerBytes = reader.BytePosition;
        if (headerBytes > obu.PayloadLength)
            throw Av1Errors.Truncated();

        return ReadTileGroup(payloadOffset + headerBytes, obu.PayloadLength - headerBytes, cancellationToken);
    }

    private Av1FrameBuffer ShowExistingFrame(Av1FrameHeader header)
    {
        var frame = _coding.Slots[header.FrameToShowMapIdx] ?? throw Av1Errors.Invalid("a frame refers to an empty reference slot.");
        if (header.FrameType is Av1Constants.KeyFrame)
        {
            // Section 7.21 "Reference frame loading process", then the reference frame update process with every slot
            for (var i = 0; i < Av1Constants.NumRefFrames; i++)
            {
                _coding.SetSlot(i, frame);
            }
        }

        frame.AddReference();
        return frame;
    }

    private void BeginFrame(Av1SequenceHeader sequence, Av1FrameHeader header)
    {
        _scope.Limits.EnsureCanvasWithinLimits(header.UpscaledWidth, header.FrameHeight);

        // A frame that is not shown is not charged by the sink of the displayed frames
        if (!header.ShowFrame)
            _tracker?.ChargeHiddenPixels((long)header.UpscaledWidth * header.FrameHeight);

        _coding.BeginFrame(sequence, header);
    }

    // Section 5.11.1 "General tile group OBU syntax"
    private Av1FrameBuffer? ReadTileGroup(int offset, int length, CancellationToken cancellationToken)
    {
        var frame = _coding.CurrentFrame!;
        var header = frame.Header;
        var tileCount = header.TileCount;
        var reader = new Av1BitReader(_data.AsSpan(offset, length));
        var tileStartAndEndPresent = tileCount > 1 && reader.ReadFlag();
        var tgStart = 0;
        var tgEnd = tileCount - 1;
        if (tileStartAndEndPresent)
        {
            var tileBits = header.TileColsLog2 + header.TileRowsLog2;
            tgStart = (int)reader.ReadBits(tileBits);
            tgEnd = (int)reader.ReadBits(tileBits);
        }

        if (tgStart != _tileNum || tgEnd < tgStart || tgEnd >= tileCount)
            throw Av1Errors.Invalid("the tiles of a frame are not in order.");

        reader.ByteAlign();
        var position = offset + reader.BytePosition;
        var end = offset + length;
        if (position > end)
            throw Av1Errors.Truncated();

        for (; _tileNum <= tgEnd; _tileNum++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int tileSize;
            if (_tileNum == tgEnd)
            {
                tileSize = end - position;
            }
            else
            {
                if (end - position < header.TileSizeBytes)
                    throw Av1Errors.Truncated();

                var sizeReader = new Av1BitReader(_data.AsSpan(position, header.TileSizeBytes));
                var tileSizeMinus1 = sizeReader.ReadLittleEndian(header.TileSizeBytes);
                position += header.TileSizeBytes;
                if (tileSizeMinus1 >= (uint)(end - position))
                    throw Av1Errors.Truncated();

                tileSize = (int)tileSizeMinus1 + 1;
            }

            if (tileSize is 0)
                throw Av1Errors.Truncated();

            DecodeTile(frame, position, tileSize, cancellationToken);
            position += tileSize;
        }

        if (tgEnd != tileCount - 1)
            return null;

        _coding.EndTiles(header);
        _seenFrameHeader = false;
        return DecodeFrameWrapup(frame, cancellationToken);
    }

    private void DecodeTile(Av1FrameBuffer frame, int position, int tileSize, CancellationToken cancellationToken)
    {
        var header = frame.Header;
        var tileRow = _tileNum / header.TileCols;
        var tileCol = _tileNum % header.TileCols;

        // init_symbol: the tile decodes with a copy of the frame context, and with the default intra frame y mode distributions
        var tileContext = _coding.BeginTile();
        _symbols.Initialize(_data, position, tileSize, header.DisableCdfUpdate);

        var decoder = new Av1TileCoder(frame, _coding.State, _symbols, _coding.Scratch!, _coding.References, _coding.InterScratch, tileContext, cancellationToken);
        decoder.DecodeTile(tileRow, tileCol);

        _symbols.Exit();
        _coding.EndTile(header, _tileNum);
    }

    // Section 7.4 "Decode frame wrapup process"
    private Av1FrameBuffer? DecodeFrameWrapup(Av1FrameBuffer frame, CancellationToken cancellationToken)
    {
        var header = frame.Header;
        _coding.PostFilter.Apply(_scope, frame, _coding.State, InLoopFilters, cancellationToken, maxDegreeOfParallelism: MaxDegreeOfParallelism);
        _coding.StoreFrame();
        if (header.ShowFrame)
            return frame;

        frame.Release();
        return null;
    }

    public void Dispose()
    {
        _heldOutput?.Release();
        _heldOutput = null;
        _coding.Dispose();
    }
}
