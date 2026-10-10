using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Incremental WebP RIFF walker (WebP container specification): the RIFF header, the simple lossy (<c>VP8 </c>) and lossless
/// (<c>VP8L</c>) layouts, and the extended layout (<c>VP8X</c>, <c>ICCP</c>, <c>ANIM</c>, <c>ANMF</c> frames with their
/// <c>ALPH</c>/bitstream sub-chunks, <c>EXIF</c>, <c>XMP </c>, unknown chunks). It never decodes compressed pixel data: it
/// validates the structure and the bitstream headers, and in <see cref="StructureWalk.Decode"/> mode buffers the payloads of
/// each image for a <see cref="WebPDecodeObserver"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Header walk: stops at the bitstream chunk of a still image (its 10-byte VP8 or 5-byte VP8L header is read) or at the first
/// <c>ANMF</c> chunk of an animation (the frame count is then unknown).
/// </description></item>
/// <item><description>Full scan: walks to the end of the RIFF data, charges every image to <see cref="ImageResourceLimits.MaxFrames"/> and counts the frames.</description></item>
/// <item><description>Decode: like a full scan, but each image's payloads go to the observer, which charges frames itself and may stop early.</description></item>
/// </list>
/// <para>
/// Structure defects are <see cref="InvalidImageContentException"/>: a RIFF size too small or chunks extending past the RIFF
/// data or their frame, a first chunk other than <c>VP8 </c>/<c>VP8L</c>/<c>VP8X</c>, a repeated <c>VP8X</c>, an empty or
/// oversized canvas, an <c>ICCP</c> or <c>ANIM</c> chunk after image data, an animation without <c>ANIM</c> before its frames or
/// without frames, image chunks outside <c>ANMF</c> in an animation or <c>ANMF</c> chunks in a still image, several images or
/// alpha chunks, frames outside the canvas, bitstream dimensions different from the canvas or frame, and invalid bitstream
/// headers. Unknown chunks (and an <c>ANIM</c> chunk without the animation flag) are skipped; data after the RIFF size is not
/// read. In the simple layouts every chunk after the image is skipped. The first <c>ICCP</c>, <c>EXIF</c> (a leading
/// <c>Exif\0\0</c> is removed) and <c>XMP </c> chunks of an extended file are metadata, adopted when valid.
/// </para>
/// </remarks>
internal sealed class WebPStructureParser : ImageParser<ImageInfo>
{
    /// <summary>The length of the RIFF header: <c>RIFF</c>, the size and <c>WEBP</c>.</summary>
    public const int RiffHeaderLength = 12;

    /// <summary>The length of a chunk header: FourCC and payload size.</summary>
    public const int ChunkHeaderLength = 8;

    /// <summary>The length of the <c>ANMF</c> frame header before its sub-chunks.</summary>
    public const int FrameHeaderLength = 16;

    /// <summary>The length of the <c>VP8X</c> payload defined by the specification.</summary>
    public const int ExtendedHeaderLength = 10;

    // Flags of the VP8X chunk
    public const byte AnimationFlag = 0x02;
    public const byte XmpFlag = 0x04;
    public const byte ExifFlag = 0x08;
    public const byte AlphaFlag = 0x10;
    public const byte IccFlag = 0x20;

    private readonly ImageCodecContext _context;
    private readonly StructureWalk _walk;
    private readonly WebPDecodeObserver? _observer;
    private readonly DecodedMetadataBuilder _metadata;

    private State _state;
    private long _position;
    private long _riffEnd;
    private bool _firstChunkSeen;
    private bool _extended;
    private bool _afterImageData;
    private bool _iccSeen;
    private bool _exifSeen;
    private bool _xmpSeen;
    private bool _animSeen;
    private bool _imageSeen;
    private bool _headerReported;
    private bool _returnNow;
    private bool _previousDisposeToBackground;
    private bool _anyAlpha;
    private bool _transparencyPossible;
    private int _frameCount;
    private ImageInfo? _result;

    // Chunk being streamed
    private ChunkKind _chunkKind;
    private long _remaining;
    private bool _padPending;
    private WebPPayloadBuffer? _payload;

    // Frame being assembled (ANMF frame, or the still image)
    private bool _inFrame;
    private long _frameEnd;
    private bool _framePadPending;
    private WebPFrameInfo _frame;
    private bool _frameHasBitstream;
    private bool _frameAlphaSeen;
    private WebPPayloadBuffer? _alpha;
    private WebPPayloadBuffer? _bitstream;

    public WebPStructureParser(ImageCodecContext context, StructureWalk walk, WebPDecodeObserver? observer = null)
        : base(ImageFormat.WebP)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (walk == StructureWalk.Decode)
        {
            ArgumentNullException.ThrowIfNull(observer);
        }

        _context = context;
        _walk = walk;
        _observer = walk == StructureWalk.Decode ? observer : null;
        _metadata = new DecodedMetadataBuilder(context.Tracker, ImageFormat.WebP);
    }

    private enum State
    {
        RiffHeader,
        ChunkHeader,
        Payload,
        Padding,
        Done,
    }

    private enum ChunkKind
    {
        Skip,
        Alpha,
        Bitstream,
    }

    /// <summary>Gets the canvas width.</summary>
    public int Width { get; private set; }

    /// <summary>Gets the canvas height.</summary>
    public int Height { get; private set; }

    public Size Size => new(Width, Height);

    /// <summary>Gets a value indicating whether the file uses the extended layout (<c>VP8X</c>).</summary>
    public bool IsExtended => _extended;

    /// <summary>Gets the <c>VP8X</c> flags (0 for the simple layouts).</summary>
    public byte Flags { get; private set; }

    /// <summary>Gets a value indicating whether the file is an animation (the <c>VP8X</c> animation flag).</summary>
    public bool IsAnimated => (Flags & AnimationFlag) != 0;

    /// <summary>Gets the <c>ANIM</c> loop count (0 is infinite).</summary>
    public ushort LoopCount { get; private set; }

    /// <summary>Gets the <c>ANIM</c> background color hint (B, G, R, A byte order as a little-endian value). The decoder never paints it.</summary>
    public uint BackgroundColor { get; private set; }

    /// <summary>Gets a value indicating whether the still image is lossless (VP8L).</summary>
    public bool IsLossless { get; private set; }

    /// <summary>Gets a value indicating whether the still image carries alpha (the <c>VP8X</c> alpha flag, an <c>ALPH</c> chunk or the VP8L alpha hint).</summary>
    public bool HasAlpha { get; private set; }

    /// <summary>Gets the number of images traversed so far.</summary>
    public int FrameCount => _frameCount;

    /// <summary>Gets the metadata collected so far.</summary>
    public ImageMetadata Metadata => _metadata.Metadata;

    /// <summary>Gets the animation settings, or <see langword="null"/> for a still image.</summary>
    public AnimationMetadata? Animation => IsAnimated ? new AnimationMetadata { TotalPlays = AnimationTiming.FromWebPLoopCount(LoopCount) } : null;

    /// <summary>Gets the default working representation: <see cref="PixelFormat.Rgba32"/> for animations and images with alpha, otherwise <see cref="PixelFormat.Rgb24"/>.</summary>
    public PixelFormat DefaultPixelFormat => DefaultPixelFormats.ForWebP(IsAnimated, HasAlpha);

    public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
    {
        consumed = 0;
        while (true)
        {
            var remaining = buffer[consumed..];
            switch (_state)
            {
                case State.RiffHeader:
                    if (remaining.Length < RiffHeaderLength)
                        return ParseStatus.NeedMoreData(RiffHeaderLength);

                    ProcessRiffHeader(remaining);
                    Advance(ref consumed, RiffHeaderLength);
                    _state = State.ChunkHeader;
                    break;

                case State.ChunkHeader:
                {
                    var containerEnd = _inFrame ? _frameEnd : _riffEnd;
                    if (_position == containerEnd)
                    {
                        if (_inFrame)
                        {
                            if (!EndFrameChunk(remaining, ref consumed, out var needed))
                                return needed;
                        }
                        else
                        {
                            EndRiff();
                            return ParseStatus.Complete;
                        }

                        if (_state == State.Done)
                            return ParseStatus.Complete;

                        if (TryYield())
                            return ParseStatus.Complete;

                        break;
                    }

                    if (containerEnd - _position < ChunkHeaderLength)
                        throw Invalid(_inFrame ? "An ANMF frame ends with bytes that do not form a chunk." : "The RIFF data ends with bytes that do not form a chunk.");

                    if (remaining.Length < ChunkHeaderLength)
                        return ParseStatus.NeedMoreData(ChunkHeaderLength);

                    var status = ProcessChunkHeader(remaining, containerEnd, ref consumed);
                    if (!status.IsComplete || _state == State.Done)
                        return status;

                    if (_returnNow)
                    {
                        // Yield after the header callback: the chunk header is processed again by the next call
                        _returnNow = false;
                        return ParseStatus.Complete;
                    }

                    break;
                }

                case State.Payload:
                {
                    if (_remaining == 0)
                    {
                        CompletePayload();
                        if (_state == State.Done)
                            return ParseStatus.Complete;

                        if (TryYield())
                            return ParseStatus.Complete;

                        break;
                    }

                    if (remaining.IsEmpty)
                        return ParseStatus.NeedMoreData(1);

                    var piece = remaining[..(int)Math.Min(remaining.Length, _remaining)];
                    _payload?.Append(piece);
                    _remaining -= piece.Length;
                    Advance(ref consumed, piece.Length);
                    break;
                }

                case State.Padding:
                    if (remaining.IsEmpty)
                        return ParseStatus.NeedMoreData(1);

                    Advance(ref consumed, 1);
                    _state = State.ChunkHeader;
                    break;

                default:
                    return ParseStatus.Complete;
            }
        }
    }

    public override ImageInfo GetResult() => _result ?? throw new InvalidOperationException("The WebP structure was not parsed.");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ReleaseFrameState();
            _payload?.Dispose();
            _payload = null;
        }

        base.Dispose(disposing);
    }

    private void Advance(ref int consumed, int count)
    {
        consumed += count;
        _position += count;
    }

    private bool TryYield() => _walk == StructureWalk.Decode && _context.TryConsumeYield();

    private void ProcessRiffHeader(ReadOnlySpan<byte> header)
    {
        if (!WebPCodec.MatchesSignature(header))
            throw Invalid("The WebP RIFF header is invalid.");

        var size = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        if (size < 4 + ChunkHeaderLength)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The WebP RIFF size {size} is too small to contain a chunk."));

        _riffEnd = 8L + size;
    }

    private ParseStatus ProcessChunkHeader(ReadOnlySpan<byte> remaining, long containerEnd, ref int consumed)
    {
        var fourCC = remaining[..4];
        var size = BinaryPrimitives.ReadUInt32LittleEndian(remaining[4..]);
        var payloadEnd = _position + ChunkHeaderLength + size;
        if (payloadEnd > containerEnd)
        {
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The WebP chunk '{FormatFourCC(fourCC)}' ({size} bytes) extends past the end of the {(_inFrame ? "ANMF frame" : "RIFF data")}."));
        }

        // A missing padding byte is tolerated only at the very end of the container
        var padded = (size & 1) != 0 && payloadEnd < containerEnd;
        if (_inFrame)
            return ProcessFrameSubChunk(remaining, fourCC, size, padded, ref consumed);

        if (!_firstChunkSeen)
        {
            if (fourCC.SequenceEqual("VP8X"u8))
                return ProcessExtendedHeader(remaining, size, padded, ref consumed);

            if (!fourCC.SequenceEqual("VP8 "u8) && !fourCC.SequenceEqual("VP8L"u8))
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The first WebP chunk is '{FormatFourCC(fourCC)}'; VP8, VP8L or VP8X is expected."));

            return ProcessStillBitstream(remaining, fourCC, size, padded, ref consumed);
        }

        if (!_extended)
        {
            // Simple layouts: nothing after the image is interpreted
            return Skip(size, padded, ref consumed);
        }

        if (fourCC.SequenceEqual("VP8X"u8))
            throw Invalid("The WebP file has more than one VP8X chunk.");

        if (fourCC.SequenceEqual("ICCP"u8))
        {
            if (_afterImageData)
                throw Invalid("The WebP ICCP chunk appears after the image data.");

            if (_iccSeen)
                return Skip(size, padded, ref consumed);

            // The flag is set once the whole chunk is buffered: a request for more data re-enters with the same chunk
            return BufferMetadata(remaining, size, padded, ref consumed, static (parser, data) =>
            {
                parser._iccSeen = true;

                // The profile bytes are retained: charged to MaxMetadataBytes before the copy
                parser._metadata.Charge(data.Length);
                parser._metadata.TryAdoptIccProfile(data.ToArray(), grayscaleSamples: false);
            });
        }

        if (fourCC.SequenceEqual("EXIF"u8))
        {
            if (_exifSeen)
                return Skip(size, padded, ref consumed);

            return BufferMetadata(remaining, size, padded, ref consumed, static (parser, data) =>
            {
                parser._exifSeen = true;
                parser._metadata.TryAdoptExif(data.StartsWith("Exif\0\0"u8) ? data[6..] : data);
            });
        }

        if (fourCC.SequenceEqual("XMP "u8))
        {
            if (_xmpSeen)
                return Skip(size, padded, ref consumed);

            return BufferMetadata(remaining, size, padded, ref consumed, static (parser, data) =>
            {
                parser._xmpSeen = true;
                parser._metadata.TryAdoptXmp(data);
            });
        }

        if (fourCC.SequenceEqual("ANIM"u8))
        {
            if (!IsAnimated)
                return Skip(size, padded, ref consumed); // must be ignored without the animation flag

            if (_animSeen)
                throw Invalid("The WebP file has more than one ANIM chunk.");

            if (_afterImageData)
                throw Invalid("The WebP ANIM chunk appears after the first frame.");

            if (size < 6)
                throw Invalid("The WebP ANIM chunk is shorter than 6 bytes.");

            return BufferMetadata(remaining, size, padded, ref consumed, static (parser, data) =>
            {
                parser._animSeen = true;
                parser.BackgroundColor = BinaryPrimitives.ReadUInt32LittleEndian(data);
                parser.LoopCount = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
            });
        }

        if (fourCC.SequenceEqual("ANMF"u8))
        {
            if (!IsAnimated)
                throw Invalid("The WebP file has an ANMF chunk but no animation flag.");

            if (!_animSeen)
                throw Invalid("The WebP ANMF chunk appears before the ANIM chunk.");

            return ProcessFrameHeader(remaining, size, padded, ref consumed);
        }

        if (fourCC.SequenceEqual("ALPH"u8) || fourCC.SequenceEqual("VP8 "u8) || fourCC.SequenceEqual("VP8L"u8))
        {
            if (IsAnimated)
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The animated WebP file has a '{FormatFourCC(fourCC)}' chunk outside of an ANMF frame."));

            if (_imageSeen)
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The WebP file has a '{FormatFourCC(fourCC)}' chunk after its image."));

            if (fourCC.SequenceEqual("ALPH"u8))
                return ProcessAlpha(size, padded, ref consumed);

            return ProcessStillBitstream(remaining, fourCC, size, padded, ref consumed);
        }

        return Skip(size, padded, ref consumed);
    }

    private ParseStatus ProcessExtendedHeader(ReadOnlySpan<byte> remaining, uint size, bool padded, ref int consumed)
    {
        if (size < ExtendedHeaderLength)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The WebP VP8X chunk has {size} bytes; at least 10 are expected."));

        if (remaining.Length < ChunkHeaderLength + ExtendedHeaderLength)
            return ParseStatus.NeedMoreData(ChunkHeaderLength + ExtendedHeaderLength);

        var data = remaining.Slice(ChunkHeaderLength, ExtendedHeaderLength);
        var width = ReadUInt24(data[4..]) + 1;
        var height = ReadUInt24(data[7..]) + 1;
        if ((long)width * height > uint.MaxValue)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The WebP canvas {width}x{height} has more than 2^32 - 1 pixels."));

        _context.Limits.EnsureCanvasWithinLimits(width, height);
        _firstChunkSeen = true;
        _extended = true;
        Flags = data[0];
        Width = width;
        Height = height;
        if ((Flags & AlphaFlag) != 0)
        {
            HasAlpha = true;
            _anyAlpha = true;
        }

        // The defined fields are read; the rest of the payload (future fields) is skipped
        Advance(ref consumed, ChunkHeaderLength + ExtendedHeaderLength);
        return StartSkip(size - ExtendedHeaderLength, padded);
    }

    private ParseStatus ProcessAlpha(uint size, bool padded, ref int consumed)
    {
        if (_frameAlphaSeen)
            throw Invalid("The WebP image has more than one ALPH chunk.");

        _frameAlphaSeen = true;
        _afterImageData = true;
        Advance(ref consumed, ChunkHeaderLength);
        if (_walk == StructureWalk.Decode)
        {
            _alpha?.Dispose();
            _alpha = new WebPPayloadBuffer(_context.Scope, size);
            return StartPayload(ChunkKind.Alpha, _alpha, size, padded);
        }

        return StartSkip(size, padded);
    }

    /// <summary>The VP8/VP8L chunk of a still image (simple or extended layout).</summary>
    private ParseStatus ProcessStillBitstream(ReadOnlySpan<byte> remaining, ReadOnlySpan<byte> fourCC, uint size, bool padded, ref int consumed)
    {
        var isLossless = fourCC.SequenceEqual("VP8L"u8);
        var headerLength = isLossless ? Vp8LDecoder.HeaderLength : Vp8Decoder.FrameHeaderLength;
        var needed = ChunkHeaderLength + (int)Math.Min(size, headerLength);
        if (remaining.Length < needed)
            return ParseStatus.NeedMoreData(needed);

        var (width, height, alphaHint) = ReadBitstreamHeader(remaining.Slice(ChunkHeaderLength, needed - ChunkHeaderLength), size, isLossless);
        var hasAlpha = isLossless ? alphaHint : _frameAlphaSeen;
        if (_extended)
        {
            if (width != Width || height != Height)
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The WebP bitstream is {width}x{height} but the VP8X canvas is {Width}x{Height}."));
        }
        else
        {
            _context.Limits.EnsureCanvasWithinLimits(width, height);
            Width = width;
            Height = height;
        }

        IsLossless = isLossless;
        HasAlpha |= hasAlpha;
        _anyAlpha |= hasAlpha;
        _afterImageData = true;
        if (!_headerReported)
        {
            if (_walk == StructureWalk.Header)
            {
                _result = CreateInfo(ImageIdentifyMode.Header);
                _state = State.Done;
                return ParseStatus.Complete;
            }

            if (ReportHeader())
                return ParseStatus.Complete;
        }

        _firstChunkSeen = true;
        _imageSeen = true;
        _frame = new WebPFrameInfo(0, 0, width, height, 0, AlphaBlend: false, DisposeToBackground: false, isLossless, HasAlpha);
        _frameCount++;
        if (_walk == StructureWalk.FullScan)
        {
            _context.Tracker.ChargeScannedFrame();
        }

        Advance(ref consumed, ChunkHeaderLength);
        if (_walk == StructureWalk.Decode)
        {
            _bitstream = new WebPPayloadBuffer(_context.Scope, size);
            return StartPayload(ChunkKind.Bitstream, _bitstream, size, padded);
        }

        return StartSkip(size, padded);
    }

    private ParseStatus ProcessFrameHeader(ReadOnlySpan<byte> remaining, uint size, bool padded, ref int consumed)
    {
        if (size < FrameHeaderLength)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The WebP ANMF chunk has {size} bytes; at least 16 are expected."));

        if (!_headerReported)
        {
            _afterImageData = true;
            if (_walk == StructureWalk.Header)
            {
                _result = CreateInfo(ImageIdentifyMode.Header);
                _state = State.Done;
                return ParseStatus.Complete;
            }

            if (ReportHeader())
                return ParseStatus.Complete;
        }

        if (remaining.Length < ChunkHeaderLength + FrameHeaderLength)
            return ParseStatus.NeedMoreData(ChunkHeaderLength + FrameHeaderLength);

        var data = remaining.Slice(ChunkHeaderLength, FrameHeaderLength);
        var x = 2L * ReadUInt24(data);
        var y = 2L * ReadUInt24(data[3..]);
        var width = ReadUInt24(data[6..]) + 1;
        var height = ReadUInt24(data[9..]) + 1;
        var duration = ReadUInt24(data[12..]);
        var flags = data[15];
        if (x + width > Width || y + height > Height)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The WebP frame {_frameCount} ({width}x{height} at {x},{y}) extends outside the {Width}x{Height} canvas."));

        _afterImageData = true;
        _frame = new WebPFrameInfo((int)x, (int)y, width, height, duration, AlphaBlend: (flags & 0x02) == 0, DisposeToBackground: (flags & 0x01) != 0, IsLossless: false, HasAlpha: false);
        if (_walk == StructureWalk.FullScan)
        {
            _context.Tracker.ChargeScannedFrame();
        }

        _inFrame = true;
        _frameEnd = _position + ChunkHeaderLength + size;
        _framePadPending = padded;
        _frameHasBitstream = false;
        _frameAlphaSeen = false;
        Advance(ref consumed, ChunkHeaderLength + FrameHeaderLength);
        _state = State.ChunkHeader;
        return ParseStatus.Complete;
    }

    private ParseStatus ProcessFrameSubChunk(ReadOnlySpan<byte> remaining, ReadOnlySpan<byte> fourCC, uint size, bool padded, ref int consumed)
    {
        var isAlpha = fourCC.SequenceEqual("ALPH"u8);
        var isLossy = fourCC.SequenceEqual("VP8 "u8);
        var isLossless = fourCC.SequenceEqual("VP8L"u8);
        if (!isAlpha && !isLossy && !isLossless)
            return Skip(size, padded, ref consumed); // unknown chunks may follow the bitstream

        if (_frameHasBitstream)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The WebP frame {_frameCount} has a '{FormatFourCC(fourCC)}' chunk after its bitstream."));

        if (isAlpha)
            return ProcessAlpha(size, padded, ref consumed);

        var headerLength = isLossless ? Vp8LDecoder.HeaderLength : Vp8Decoder.FrameHeaderLength;
        var needed = ChunkHeaderLength + (int)Math.Min(size, headerLength);
        if (remaining.Length < needed)
            return ParseStatus.NeedMoreData(needed);

        var (width, height, alphaHint) = ReadBitstreamHeader(remaining.Slice(ChunkHeaderLength, needed - ChunkHeaderLength), size, isLossless);
        if (width != _frame.Width || height != _frame.Height)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The WebP frame {_frameCount} bitstream is {width}x{height} but the ANMF frame is {_frame.Width}x{_frame.Height}."));

        var hasAlpha = isLossless ? alphaHint : _frameAlphaSeen;
        _frame = _frame with { IsLossless = isLossless, HasAlpha = hasAlpha };
        _anyAlpha |= hasAlpha;
        _frameHasBitstream = true;
        Advance(ref consumed, ChunkHeaderLength);
        if (_walk == StructureWalk.Decode)
        {
            _bitstream = new WebPPayloadBuffer(_context.Scope, size);
            return StartPayload(ChunkKind.Bitstream, _bitstream, size, padded);
        }

        return StartSkip(size, padded);
    }

    /// <summary>Ends an ANMF chunk once its sub-chunks are walked: the frame is complete.</summary>
    /// <returns><see langword="false"/> when more input is needed (<paramref name="needed"/>).</returns>
    private bool EndFrameChunk(ReadOnlySpan<byte> remaining, ref int consumed, out ParseStatus needed)
    {
        needed = default;
        if (!_frameHasBitstream)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The WebP frame {_frameCount} has no VP8 or VP8L bitstream."));

        if (_framePadPending)
        {
            if (remaining.IsEmpty)
            {
                needed = ParseStatus.NeedMoreData(1);
                return false;
            }

            Advance(ref consumed, 1);
            _framePadPending = false;
        }

        _inFrame = false;
        var frame = _frame;
        var frameIndex = _frameCount++;

        // Transparency capability: frames that leave the initial transparent canvas visible, or clear it again
        if (frameIndex == 0 && (frame.X != 0 || frame.Y != 0 || frame.Width != Width || frame.Height != Height))
        {
            _transparencyPossible = true;
        }

        if (frameIndex > 0 && _previousDisposeToBackground)
        {
            _transparencyPossible = true;
        }

        _previousDisposeToBackground = frame.DisposeToBackground;

        if (_walk == StructureWalk.Decode)
        {
            var alpha = frame.IsLossless ? null : _alpha;
            var proceed = _observer!.OnFrame(frame, alpha, _bitstream!);
            ReleaseFrameState();
            if (!proceed)
            {
                _state = State.Done;
            }
        }

        return true;
    }

    private void CompletePayload()
    {
        var kind = _chunkKind;
        _payload = null;
        _state = _padPending ? State.Padding : State.ChunkHeader;
        if (kind == ChunkKind.Bitstream && !_inFrame)
        {
            // The still image is complete
            var alpha = _frame.IsLossless ? null : _alpha;
            var proceed = _observer!.OnFrame(_frame, alpha, _bitstream!);
            ReleaseFrameState();
            if (!proceed)
            {
                _state = State.Done;
            }
        }
    }

    private void EndRiff()
    {
        if (!_extended || !IsAnimated)
        {
            if (!_imageSeen)
                throw Invalid("The WebP file has no image data.");
        }
        else
        {
            if (!_animSeen)
                throw Invalid("The animated WebP file has no ANIM chunk.");

            if (_frameCount == 0)
                throw Invalid("The animated WebP file has no frame.");
        }

        _state = State.Done;
        _result = CreateInfo(ImageIdentifyMode.FullScan);
        _observer?.OnEnd(this);
    }

    /// <summary>Reports the header snapshot to a sequential reader and the observer, then yields when the reader asks for it.</summary>
    /// <returns><see langword="true"/> when the walker must return to the reader now (the chunk header is processed again on the next call).</returns>
    private bool ReportHeader()
    {
        _headerReported = true;
        if (_walk != StructureWalk.Decode)
            return false;

        _context.Sequential?.OnHeader(CreateInfo(ImageIdentifyMode.Header));
        _observer!.OnHeaderComplete(this);
        _returnNow = TryYield();
        return _returnNow;
    }

    private ImageInfo CreateInfo(ImageIdentifyMode mode)
    {
        int? frameCount;
        bool? mayHaveTransparency;
        if (IsAnimated)
        {
            if (mode == ImageIdentifyMode.Header)
            {
                frameCount = null;
                mayHaveTransparency = (Flags & AlphaFlag) != 0 ? true : null;
            }
            else
            {
                frameCount = _frameCount;
                mayHaveTransparency = _anyAlpha || _transparencyPossible;
            }
        }
        else
        {
            frameCount = 1;
            mayHaveTransparency = HasAlpha;
        }

        var colorModel = IsAnimated ? ImageColorModel.Rgba : !IsLossless ? ImageColorModel.YCbCr : HasAlpha ? ImageColorModel.Rgba : ImageColorModel.Rgb;
        return new ImageInfo(
            ImageFormat.WebP,
            Size,
            DefaultPixelFormat,
            colorModel,
            bitsPerComponent: 8,
            frameCount,
            IsAnimated,
            hasPosterFrame: false,
            mayHaveTransparency,
            Animation,
            _metadata.Metadata,
            mode);
    }

    private static (int Width, int Height, bool AlphaHint) ReadBitstreamHeader(ReadOnlySpan<byte> header, uint size, bool isLossless)
    {
        if (isLossless)
        {
            var lossless = Vp8LDecoder.ReadHeader(header);
            return (lossless.Width, lossless.Height, lossless.AlphaIsUsed);
        }

        var lossy = Vp8Decoder.ReadFrameHeader(header, size);
        return (lossy.Width, lossy.Height, false);
    }

    private ParseStatus BufferMetadata(ReadOnlySpan<byte> remaining, uint size, bool padded, ref int consumed, ChunkProcessor process)
    {
        // Metadata payloads are processed in one piece (bounded by MaxEncodedBytes); adoption charges MaxMetadataBytes
        var total = ChunkHeaderLength + (long)size;
        if (total > int.MaxValue)
            throw CheckedSizes.CreateOverflowException(_context.Limits);

        if (remaining.Length < total)
            return ParseStatus.NeedMoreData((int)total);

        process(this, remaining.Slice(ChunkHeaderLength, (int)size));
        Advance(ref consumed, (int)total);
        _state = padded ? State.Padding : State.ChunkHeader;
        return ParseStatus.Complete;
    }

    private ParseStatus Skip(uint size, bool padded, ref int consumed)
    {
        Advance(ref consumed, ChunkHeaderLength);
        return StartSkip(size, padded);
    }

    private ParseStatus StartSkip(long size, bool padded) => StartPayload(ChunkKind.Skip, payload: null, size, padded);

    private ParseStatus StartPayload(ChunkKind kind, WebPPayloadBuffer? payload, long size, bool padded)
    {
        _chunkKind = kind;
        _payload = payload;
        _remaining = size;
        _padPending = padded;
        _state = State.Payload;
        return ParseStatus.Complete;
    }

    private void ReleaseFrameState()
    {
        _alpha?.Dispose();
        _alpha = null;
        _bitstream?.Dispose();
        _bitstream = null;
    }

    private delegate void ChunkProcessor(WebPStructureParser parser, ReadOnlySpan<byte> data);

    private static int ReadUInt24(ReadOnlySpan<byte> data) => data[0] | (data[1] << 8) | (data[2] << 16);

    private static string FormatFourCC(ReadOnlySpan<byte> fourCC)
    {
        Span<char> chars = stackalloc char[4];
        for (var i = 0; i < 4; i++)
        {
            chars[i] = fourCC[i] is >= 0x20 and < 0x7F ? (char)fourCC[i] : '?';
        }

        return new string(chars);
    }

    private static InvalidImageContentException Invalid(string message) => new(message, ImageFormat.WebP);
}
