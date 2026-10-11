namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Encodes the frames of an AV1 coded video sequence into open bitstream units (AV1 specification, sections 5 and 6). The
/// caller fills the planes of <see cref="GetSourcePlane"/>, starts a frame with <see cref="BeginFrame"/>, then calls
/// <see cref="Encode"/> until it returns <see langword="true"/>: <see cref="Payload"/> is then the temporal unit of the
/// frame, without temporal delimiter, which is the form of an item or of a sample of an AVIF file.
/// </summary>
/// <remarks>
/// <para>
/// The encoder shares the state of the decoder (<see cref="Av1FrameCodingState"/>) and its reconstruction and filtering
/// processes: <see cref="Reconstruction"/> is, by construction, the frame that a decoder outputs for
/// <see cref="Payload"/>.
/// </para>
/// <para>
/// Every buffer is rented when the encoder is created, apart from the buffers that grow with the encoded data. After an
/// exception the state of the encoder is undefined: it must be disposed.
/// </para>
/// </remarks>
internal sealed class Av1Encoder : IDisposable
{
    private readonly AllocationScope _scope;
    private readonly Av1EncoderSettings _settings;
    private readonly Av1SequenceHeader _sequence;
    private readonly byte[] _sequenceHeader;
    private readonly Av1FrameCodingState _coding;
    private readonly Av1EncoderWorkspace _workspace;
    private readonly Av1SymbolEncoder _symbols = new();
    private readonly PooledPayloadWriter _tileData;
    private readonly PooledPayloadWriter _frameData;
    private readonly PooledPayloadWriter _payload;
    private readonly Av1EncoderDecisions _decisions;
    private readonly int _quantizerIndex;
    private Av1FrameHeader? _syntax;
    private Av1FrameBuffer? _reconstruction;
    private int[] _tileSizes = [];
    private Phase _phase = Phase.Idle;
    private int _tile;
    private int _row;

    private enum Phase
    {
        Idle,
        Start,
        Tiles,
        Finish,
        Done,
    }

    /// <summary>Creates an encoder whose buffers are rented from <paramref name="scope"/>.</summary>
    /// <exception cref="ImageResourceLimitException">The buffers exceed the live-allocation limit.</exception>
    public Av1Encoder(AllocationScope scope, in Av1EncoderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.Width, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(settings.Width, 65536);
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.Height, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(settings.Height, 65536);
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.Quality, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(settings.Quality, 100);
        if (!settings.StillPicture)
            throw new NotSupportedException("AV1 sequences are not encoded yet.");

        _scope = scope;
        _settings = settings;
        _decisions = Av1EncoderDecisions.Create(settings.Effort);
        if (!settings.Lossless)
        {
            // The Lagrange multiplier of the high resolution approximation, 0.12 times the square of the quantizer step.
            // The coefficients of AV1 have 3 fractional bits, so that the step of a quantizer q is q / 8 sample units
            _quantizerIndex = GetQuantizerIndex(settings.Quality);
            long quantizer = Av1Reconstruction.GetAcQuantizer(_quantizerIndex, settings.BitDepth);
            _decisions.Lambda = Math.Max(1, (123 * quantizer * quantizer) / 64);
            _decisions.QuantizerRounding = 96;
        }

        var parameters = new Av1SequenceParameters(
            settings.Width,
            settings.Height,
            settings.BitDepth,
            settings.Monochrome,
            settings.Monochrome ? 1 : settings.SubsamplingX,
            settings.Monochrome ? 1 : settings.SubsamplingY,
            settings.ColorPrimaries,
            settings.TransferCharacteristics,
            settings.MatrixCoefficients,
            settings.FullRange,
            settings.ChromaSamplePosition,
            settings.StillPicture,
            Use128x128Superblock: false,
            EnableCdef: false,
            EnableRestoration: false,
            Av1Levels.GetLevelIndex(settings.Width, settings.Height));

        _coding = new Av1FrameCodingState(scope);
        _tileData = new PooledPayloadWriter(scope);
        _frameData = new PooledPayloadWriter(scope);
        _payload = new PooledPayloadWriter(scope);
        try
        {
            // The model of the sequence is the one a decoder derives from the header that is written
            Av1HeaderWriter.WriteSequenceHeader(_payload, parameters);
            _sequenceHeader = _payload.WrittenSpan.ToArray();
            _payload.Reset();
            _sequence = Av1SequenceHeader.Parse(_sequenceHeader, operatingPoint: 0);
            _workspace = new Av1EncoderWorkspace(scope, _sequence, settings.Width, settings.Height);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Gets the sequence header of the encoded frames.</summary>
    public Av1SequenceHeader Sequence => _sequence;

    /// <summary>Gets the payload of the sequence header OBU: what the AV1 configuration of an AVIF file describes.</summary>
    public ReadOnlySpan<byte> SequenceHeaderPayload => _sequenceHeader;

    /// <summary>Gets the units of the last encoded frame; valid until the next frame starts.</summary>
    public ReadOnlyMemory<byte> Payload => _phase is Phase.Done ? _payload.WrittenMemory : throw new InvalidOperationException("No frame is encoded.");

    /// <summary>Gets the last encoded frame as a decoder reconstructs it; valid until the next frame starts.</summary>
    public Av1FrameBuffer Reconstruction => _reconstruction ?? throw new InvalidOperationException("No frame is encoded.");

    /// <summary>
    /// Gets the plane <paramref name="index"/> of the frame to encode (0 for Y, 1 for U, 2 for V): the caller writes the
    /// <see cref="Av1Plane.Width"/> x <see cref="Av1Plane.Height"/> samples of the frame before <see cref="BeginFrame"/>.
    /// </summary>
    public Av1Plane GetSourcePlane(int index) => _workspace.GetSource(index);

    /// <summary>Starts the encoding of the frame whose samples are in the source planes.</summary>
    public void BeginFrame()
    {
        if (_phase is not (Phase.Idle or Phase.Done))
            throw new InvalidOperationException("The previous frame is not complete.");

        _reconstruction?.Release();
        _reconstruction = null;
        for (var plane = 0; plane < _sequence.PlaneCount; plane++)
        {
            ExtendPlane(_workspace.GetSource(plane));
        }

        _phase = Phase.Start;
    }

    /// <summary>Encodes a part of the frame: a row of superblocks, or the end of the frame.</summary>
    /// <returns><see langword="true"/> when the frame is complete.</returns>
    /// <exception cref="ImageResourceLimitException">A buffer exceeds the live-allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation is canceled.</exception>
    public bool Encode(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (_phase)
        {
            case Phase.Start:
                StartFrame();
                _phase = Phase.Tiles;
                return false;
            case Phase.Tiles:
                if (EncodeSuperblockRow(cancellationToken))
                    _phase = Phase.Finish;

                return false;
            case Phase.Finish:
                FinishFrame(cancellationToken);
                _phase = Phase.Done;
                return true;
            default:
                throw new InvalidOperationException("No frame is being encoded.");
        }
    }

    public void Dispose()
    {
        _reconstruction?.Release();
        _reconstruction = null;
        _coding.Dispose();
        _workspace?.Dispose();
        _tileData.Dispose();
        _frameData.Dispose();
        _payload.Dispose();
        _phase = Phase.Idle;
    }

    /// <summary>
    /// Returns the <c>base_q_idx</c> of a quality, 0 (the coarsest quantizer) to 100 (the finest one that is not lossless):
    /// the quantizer index 0 with no delta is the lossless coding, which is selected on its own.
    /// </summary>
    public static int GetQuantizerIndex(int quality) => Math.Clamp(((100 - quality) * 255 + 50) / 100, 1, 255);

    // The samples beyond the frame, which the blocks that cross its right and bottom edges predict and code: they repeat
    // the last column and the last row, which costs the fewest bits
    private static void ExtendPlane(Av1Plane plane)
    {
        var samples = plane.Samples;
        var stride = plane.Stride;
        for (var y = 0; y < plane.Height; y++)
        {
            var row = samples.Slice(y * stride, stride);
            row[plane.Width..].Fill(row[plane.Width - 1]);
        }

        var last = samples.Slice((plane.Height - 1) * stride, stride);
        for (var y = plane.Height; y < plane.AllocatedHeight; y++)
        {
            last.CopyTo(samples.Slice(y * stride, stride));
        }
    }

    private void StartFrame()
    {
        var limits = Av1FrameHeaderParser.ComputeTileLimits(_sequence.Use128x128Superblock, 2 * ((_settings.Width + 7) >> 3), 2 * ((_settings.Height + 7) >> 3));
        var syntax = new Av1FrameHeader
        {
            FrameType = Av1Constants.KeyFrame,
            ShowFrame = true,
            TileColsLog2 = limits.MinLog2TileCols,
            TileRowsLog2 = Math.Max(limits.MinLog2Tiles - limits.MinLog2TileCols, 0),
            TileSizeBytes = 4,
            BaseQIdx = _quantizerIndex,
            TxMode = Av1Constants.TxModeLargest,
        };

        _syntax = syntax;
        var header = ParseFrameHeader(syntax);
        _coding.BeginFrame(_sequence, header);
        if (_tileSizes.Length < header.TileCount)
            _tileSizes = new int[header.TileCount];

        _tileData.Reset();
        _tile = 0;
        BeginTile(header);
    }

    // Writes the header and reads it back: the derived variables of the frame are the ones of a decoder
    private Av1FrameHeader ParseFrameHeader(Av1FrameHeader syntax)
    {
        _frameData.Reset();
        var writer = new Av1BitWriter(_frameData);
        Av1HeaderWriter.WriteFrameHeader(ref writer, _sequence, syntax);
        writer.WriteTrailingBits();
        var data = _frameData.WrittenSpan;
        var reader = new Av1BitReader(data);
        var obu = new Av1ObuHeader(Av1Constants.ObuFrameHeader, HasExtension: false, TemporalId: 0, SpatialId: 0, HeaderLength: 0, data.Length);
        return Av1FrameHeaderParser.Parse(ref reader, _sequence, obu, _coding.Slots, _coding.RefValid);
    }

    private void BeginTile(Av1FrameHeader header)
    {
        _coding.BeginTile();
        _symbols.Initialize(_tileData, header.DisableCdfUpdate);
        _row = header.MiRowStarts[_tile / header.TileCols];
    }

    // Returns whether the last row of the last tile is encoded
    private bool EncodeSuperblockRow(CancellationToken cancellationToken)
    {
        var frame = _coding.CurrentFrame!;
        var header = frame.Header;
        var tileRow = _tile / header.TileCols;
        var tileCol = _tile % header.TileCols;
        var coder = new Av1TileCoder(frame, _coding.State, _workspace, _symbols, _coding.Scratch!, _coding.References, _coding.InterScratch, _coding.TileContext, cancellationToken);
        coder.SetDecisions(_decisions);
        coder.EncodeSuperblockRow(tileRow, tileCol, _row);
        _row += _sequence.Use128x128Superblock ? 32 : 16;
        if (_row < header.MiRowStarts[tileRow + 1])
            return false;

        _tileSizes[_tile] = _symbols.Finish();
        _coding.EndTile(header, _tile);
        _tile++;
        if (_tile < header.TileCount)
        {
            BeginTile(header);
            return false;
        }

        _coding.EndTiles(header);
        return true;
    }

    private void FinishFrame(CancellationToken cancellationToken)
    {
        var frame = _coding.CurrentFrame!;
        var header = frame.Header;
        _coding.PostFilter.Apply(_scope, frame, _coding.State, Av1InLoopFilters.All, cancellationToken);
        _reconstruction = _coding.StoreFrame();

        // The size of a tile is coded with the fewest bytes that hold the largest one
        var syntax = _syntax!;
        var tileCount = header.TileCount;
        var tileSizeBytes = 1;
        for (var i = 0; i < tileCount - 1; i++)
        {
            while (tileSizeBytes < 4 && (uint)(_tileSizes[i] - 1) >= 1u << (8 * tileSizeBytes))
            {
                tileSizeBytes++;
            }
        }

        syntax.TileSizeBytes = tileSizeBytes;

        // Section 5.10 "Frame OBU syntax": the frame header, then the tile group (section 5.11.1)
        _frameData.Reset();
        var writer = new Av1BitWriter(_frameData);
        Av1HeaderWriter.WriteFrameHeader(ref writer, _sequence, syntax);
        writer.ByteAlign();
        if (tileCount > 1)
        {
            writer.WriteFlag(false); // tile_start_and_end_present_flag
            writer.ByteAlign();
        }

        var tiles = _tileData.WrittenSpan;
        var offset = 0;
        for (var i = 0; i < tileCount; i++)
        {
            if (i < tileCount - 1)
                writer.WriteLittleEndian((uint)(_tileSizes[i] - 1), tileSizeBytes); // tile_size_minus_1

            _frameData.Write(tiles.Slice(offset, _tileSizes[i]));
            offset += _tileSizes[i];
        }

        _payload.Reset();
        Av1ObuWriter.Write(_payload, Av1Constants.ObuSequenceHeader, _sequenceHeader);
        Av1ObuWriter.Write(_payload, Av1Constants.ObuFrame, _frameData.WrittenSpan);
    }
}
