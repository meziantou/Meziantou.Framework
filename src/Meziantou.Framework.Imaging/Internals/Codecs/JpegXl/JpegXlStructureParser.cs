using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Incremental JPEG XL walker: a bare codestream (ISO/IEC 18181-1), or the boxes of a container (ISO/IEC 18181-2) whose
/// codestream boxes (<c>jxlc</c>, or <c>jxlp</c> parts) are given to a <see cref="JpegXlCodestreamWalker"/> in whatever
/// slices they arrive. It never decodes pixels: it validates the structure and the headers, collects the metadata, and in
/// <see cref="StructureWalk.Decode"/> mode hands the sections of each frame to a <see cref="JpegXlDecodeObserver"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Header walk: stops once the image header (and the ICC profile) is read and the next bytes are those of the frames. In a
/// container, the metadata boxes that precede the frames are read first.
/// </description></item>
/// <item><description>Full scan: reads the header and the table of contents of every frame, skips their sections, counts the displayed frames and reads every box.</description></item>
/// <item><description>Decode: like a full scan, but the sections of each frame go to the observer, which may stop early.</description></item>
/// </list>
/// <para>
/// A frame is displayed when it is the last one, or when the image is an animation and the frame has a duration: the
/// other regular frames are layers, composed with the frame that follows them, and the preview frame, the LF frames and
/// the reference-only frames are never shown.
/// </para>
/// <para>
/// Box rules: the signature box then the file type box (brand <c>jxl </c>) come first; one <c>jxlc</c> box, or <c>jxlp</c>
/// boxes numbered from 0 the last of which is flagged; the first <c>Exif</c> box and the first <c>xml </c> box, compressed
/// (<c>brob</c>) or not, are metadata, adopted when valid; every other box is skipped. A box whose size is 0 extends to the
/// end of the input.
/// </para>
/// </remarks>
internal sealed class JpegXlStructureParser : ImageParser<ImageInfo>
{
    private readonly ImageCodecContext _context;
    private readonly StructureWalk _walk;
    private readonly JpegXlDecodeObserver? _observer;
    private readonly DecodedMetadataBuilder _metadata;
    private readonly JpegXlCodestreamWalker _walker;

    private State _state;
    private bool _isContainer;
    private bool _fileTypeSeen;
    private bool _codestreamBoxSeen;
    private bool _lastPartSeen;
    private uint _partCount;
    private bool _headerReached;
    private bool _exifSeen;
    private bool _xmpSeen;
    private int _frameCount;
    private int _checkpoint;
    private ImageInfo? _result;

    // Box being streamed
    private BoxKind _boxKind;
    private uint _boxType;
    private long _boxRemaining;
    private bool _boxExtendsToEnd;
    private JpegXlCodestreamBuffer? _boxBuffer;

    public JpegXlStructureParser(ImageCodecContext context, StructureWalk walk, JpegXlDecodeObserver? observer = null)
        : base(ImageFormat.JpegXl)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (walk == StructureWalk.Decode)
        {
            ArgumentNullException.ThrowIfNull(observer);
        }

        _context = context;
        _walk = walk;
        _observer = walk == StructureWalk.Decode ? observer : null;
        _metadata = new DecodedMetadataBuilder(context.Tracker, ImageFormat.JpegXl);
        _walker = new JpegXlCodestreamWalker(context.Scope, context.Tracker);
    }

    private enum State
    {
        Start,
        BoxHeader,
        Payload,
        Done,
    }

    private enum BoxKind
    {
        Skip,
        Codestream,
        Metadata,
    }

    /// <summary>Gets the image header, once it is read.</summary>
    public JpegXlImageHeader Header => _walker.ImageHeader ?? throw new InvalidOperationException("The JPEG XL image header was not parsed.");

    /// <summary>Gets the size of the image, as it is stored (the orientation is not applied).</summary>
    public Size Size => new((int)Header.Width, (int)Header.Height);

    /// <summary>Gets the metadata collected so far.</summary>
    public ImageMetadata Metadata => _metadata.Metadata;

    /// <summary>Gets the animation parameters, or <see langword="null"/> for a still image.</summary>
    public AnimationMetadata? Animation { get; private set; }

    /// <summary>Gets the default pixel format of the image.</summary>
    public PixelFormat DefaultPixelFormat { get; private set; }

    public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
    {
        consumed = 0;
        while (true)
        {
            var remaining = buffer[consumed..];
            ParseStatus? status;
            switch (_state)
            {
                case State.Start:
                    status = ProcessStart(remaining, ref consumed);
                    break;

                case State.BoxHeader:
                    status = ProcessBoxHeader(remaining, isEndOfInput, ref consumed);
                    break;

                case State.Payload:
                    status = _boxKind switch
                    {
                        BoxKind.Codestream => ProcessCodestream(remaining, ref consumed),
                        BoxKind.Metadata => ProcessMetadataBox(remaining, isEndOfInput, ref consumed),
                        _ => ProcessSkippedBox(remaining, ref consumed),
                    };
                    break;

                default:
                    return ParseStatus.Complete;
            }

            if (status is { } result)
                return result;
        }
    }

    public override ImageInfo GetResult() => _result ?? throw new InvalidOperationException("The JPEG XL structure was not parsed.");

    /// <summary>Determines whether a frame is an image the viewer shows.</summary>
    public bool IsDisplayed(JpegXlFrameHeader frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return frame.IsRegular && (frame.IsLast || (Header.HasAnimation && frame.Duration > 0));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _walker.Dispose();
            _boxBuffer?.Dispose();
            _boxBuffer = null;
        }

        base.Dispose(disposing);
    }

    private static InvalidImageContentException Invalid(string message) => JpegXlFormat.Invalid(message);

    private bool TryYield() => _walk == StructureWalk.Decode && _context.TryConsumeYield();

    private ParseStatus? ProcessStart(ReadOnlySpan<byte> remaining, ref int consumed)
    {
        if (remaining.Length < 2)
            return ParseStatus.NeedMoreData(2);

        if (remaining.StartsWith(JpegXlFormat.CodestreamSignature))
        {
            // A bare codestream: everything is the codestream, and it ends with its last frame
            _isContainer = false;
            _boxKind = BoxKind.Codestream;
            _boxRemaining = long.MaxValue;
            _boxExtendsToEnd = true;
            _state = State.Payload;
            return null;
        }

        var signature = JpegXlFormat.ContainerSignature;
        if (remaining.Length < signature.Length)
            return ParseStatus.NeedMoreData(signature.Length);

        if (!remaining.StartsWith(signature))
            throw Invalid("the file starts with neither a codestream nor the signature box.");

        consumed += signature.Length;
        _isContainer = true;
        _state = State.BoxHeader;
        return null;
    }

    private ParseStatus? ProcessBoxHeader(ReadOnlySpan<byte> remaining, bool isEndOfInput, ref int consumed)
    {
        if (remaining.Length < JpegXlFormat.BoxHeaderLength)
        {
            if (!_walker.IsComplete)
                return ParseStatus.NeedMoreData(JpegXlFormat.BoxHeaderLength);

            // The codestream is complete: the boxes end with the input
            if (remaining.IsEmpty && isEndOfInput)
                return Finish();

            return isEndOfInput ? ParseStatus.NeedMoreData(JpegXlFormat.BoxHeaderLength) : ParseStatus.NeedMoreDataOrEnd(JpegXlFormat.BoxHeaderLength);
        }

        var size = BinaryPrimitives.ReadUInt32BigEndian(remaining);
        var type = BinaryPrimitives.ReadUInt32BigEndian(remaining[4..]);
        var headerLength = JpegXlFormat.BoxHeaderLength;
        long payloadLength;
        var extendsToEnd = false;
        if (size == 1)
        {
            headerLength = JpegXlFormat.LargeBoxHeaderLength;
            if (remaining.Length < headerLength)
                return ParseStatus.NeedMoreData(headerLength);

            var largeSize = BinaryPrimitives.ReadUInt64BigEndian(remaining[8..]);
            if (largeSize < (ulong)headerLength || largeSize > long.MaxValue)
                throw Invalid("a box has a size that does not hold its header.");

            payloadLength = (long)largeSize - headerLength;
        }
        else if (size == 0)
        {
            extendsToEnd = true;
            payloadLength = long.MaxValue;
        }
        else if (size < headerLength)
        {
            throw Invalid("a box has a size that does not hold its header.");
        }
        else
        {
            payloadLength = size - headerLength;
        }

        if (!_fileTypeSeen)
        {
            // The file type box follows the signature box: its brand is 'jxl '
            if (type != JpegXlFormat.FileTypeBox || payloadLength < 8)
                throw Invalid("the signature box is not followed by a file type box.");

            if (remaining.Length < headerLength + 4)
                return ParseStatus.NeedMoreData(headerLength + 4);

            if (BinaryPrimitives.ReadUInt32BigEndian(remaining[headerLength..]) != JpegXlFormat.Brand)
                throw Invalid("the brand of the file type box is not 'jxl '.");

            _fileTypeSeen = true;
            return BeginBox(BoxKind.Skip, type, payloadLength, extendsToEnd, headerLength, ref consumed);
        }

        switch (type)
        {
            case JpegXlFormat.CodestreamBox:
                if (_codestreamBoxSeen || _partCount > 0)
                    throw Invalid("the container has more than one codestream.");

                _codestreamBoxSeen = true;
                return BeginBox(BoxKind.Codestream, type, payloadLength, extendsToEnd, headerLength, ref consumed);

            case JpegXlFormat.PartialCodestreamBox:
            {
                if (_codestreamBoxSeen || _lastPartSeen)
                    throw Invalid("the container has a part of the codestream after its end.");

                if (payloadLength < 4)
                    throw Invalid("a part of the codestream has no index.");

                if (remaining.Length < headerLength + 4)
                    return ParseStatus.NeedMoreData(headerLength + 4);

                var index = BinaryPrimitives.ReadUInt32BigEndian(remaining[headerLength..]);
                if ((index & ~JpegXlFormat.LastPartFlag) != _partCount)
                    throw Invalid("the parts of the codestream are not in order.");

                _partCount++;
                _lastPartSeen = (index & JpegXlFormat.LastPartFlag) != 0;
                return BeginBox(BoxKind.Codestream, type, extendsToEnd ? payloadLength : payloadLength - 4, extendsToEnd, headerLength + 4, ref consumed);
            }

            case JpegXlFormat.ExifBox when !_exifSeen:
                _exifSeen = true;
                return BeginMetadataBox(type, payloadLength, extendsToEnd, headerLength, ref consumed);

            case JpegXlFormat.XmlBox when !_xmpSeen:
                _xmpSeen = true;
                return BeginMetadataBox(type, payloadLength, extendsToEnd, headerLength, ref consumed);

            case JpegXlFormat.BrotliBox:
                return BeginMetadataBox(type, payloadLength, extendsToEnd, headerLength, ref consumed);

            default:
                return BeginBox(BoxKind.Skip, type, payloadLength, extendsToEnd, headerLength, ref consumed);
        }
    }

    private ParseStatus? BeginMetadataBox(uint type, long payloadLength, bool extendsToEnd, int headerLength, ref int consumed)
    {
        if (!extendsToEnd)
        {
            if (payloadLength > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(_context.Limits);

            // Uncompressed metadata is retained as it is: charge it before buffering it
            if (type != JpegXlFormat.BrotliBox)
            {
                _metadata.Charge(payloadLength);
            }
        }

        _boxBuffer?.Dispose();
        _boxBuffer = new JpegXlCodestreamBuffer(_context.Scope);
        return BeginBox(BoxKind.Metadata, type, payloadLength, extendsToEnd, headerLength, ref consumed);
    }

    private ParseStatus? BeginBox(BoxKind kind, uint type, long payloadLength, bool extendsToEnd, int headerLength, ref int consumed)
    {
        consumed += headerLength;
        _boxKind = kind;
        _boxType = type;
        _boxRemaining = payloadLength;
        _boxExtendsToEnd = extendsToEnd;
        _state = State.Payload;
        return null;
    }

    private ParseStatus? ProcessSkippedBox(ReadOnlySpan<byte> remaining, ref int consumed)
    {
        if (_boxExtendsToEnd)
        {
            // The last box of the file: nothing of it is read
            if (!_walker.IsComplete)
                throw Invalid("the container ends before its codestream.");

            return Finish();
        }

        if (_boxRemaining == 0)
        {
            _state = State.BoxHeader;
            return null;
        }

        if (remaining.IsEmpty)
            return ParseStatus.NeedMoreData(1);

        var count = (int)Math.Min(remaining.Length, _boxRemaining);
        consumed += count;
        _boxRemaining -= count;
        return null;
    }

    private ParseStatus? ProcessMetadataBox(ReadOnlySpan<byte> remaining, bool isEndOfInput, ref int consumed)
    {
        var buffer = _boxBuffer!;
        var count = (int)Math.Min(remaining.Length, _boxRemaining);
        if (count > 0)
        {
            if (_boxExtendsToEnd && _boxType != JpegXlFormat.BrotliBox)
            {
                _metadata.Charge(count);
            }

            buffer.Append(remaining[..count]);
            consumed += count;
            if (!_boxExtendsToEnd)
            {
                _boxRemaining -= count;
            }
        }

        if (_boxExtendsToEnd)
        {
            if (!isEndOfInput)
                return ParseStatus.NeedMoreDataOrEnd(1);
        }
        else if (_boxRemaining > 0)
        {
            return ParseStatus.NeedMoreData(1);
        }

        AdoptMetadata(_boxType, buffer.Span, alreadyCharged: true);
        buffer.Dispose();
        _boxBuffer = null;
        if (_boxExtendsToEnd)
        {
            if (!_walker.IsComplete)
                throw Invalid("the container ends before its codestream.");

            return Finish();
        }

        _state = State.BoxHeader;
        return null;
    }

    private void AdoptMetadata(uint type, ReadOnlySpan<byte> data, bool alreadyCharged)
    {
        switch (type)
        {
            case JpegXlFormat.ExifBox:
            {
                // The payload starts with the offset of the TIFF header, from the end of that field
                if (data.Length < 4)
                    return;

                var offset = BinaryPrimitives.ReadUInt32BigEndian(data);
                if (offset > data.Length - 4)
                    return;

                // The orientation of a JPEG XL image is the one of its header: the EXIF tag is not consulted
                _metadata.TryAdoptExif(data[(4 + (int)offset)..], adoptOrientation: false, alreadyCharged);
                break;
            }

            case JpegXlFormat.XmlBox:
                _metadata.TryAdoptXmp(data, alreadyCharged);
                break;

            case JpegXlFormat.BrotliBox:
            {
                if (data.Length < 4)
                    throw Invalid("a compressed box does not name its type.");

                var innerType = BinaryPrimitives.ReadUInt32BigEndian(data);
                if (innerType is JpegXlFormat.BrotliBox or JpegXlFormat.SignatureBox or JpegXlFormat.FileTypeBox or JpegXlFormat.LevelBox or JpegXlFormat.CodestreamBox or JpegXlFormat.PartialCodestreamBox or JpegXlFormat.FrameIndexBox)
                    throw Invalid($"a compressed box holds a '{JpegXlFormat.ToText(innerType)}' box, which cannot be compressed.");

                var wanted = innerType switch
                {
                    JpegXlFormat.ExifBox => !_exifSeen,
                    JpegXlFormat.XmlBox => !_xmpSeen,
                    _ => false,
                };

                if (!wanted)
                    return;

                if (innerType == JpegXlFormat.ExifBox)
                {
                    _exifSeen = true;
                }
                else
                {
                    _xmpSeen = true;
                }

                if (BoundedBrotliDecoder.TryDecompress(data[4..], _context.Tracker) is { } decompressed)
                {
                    AdoptMetadata(innerType, decompressed, alreadyCharged: true);
                }

                break;
            }
        }
    }

    private ParseStatus? ProcessCodestream(ReadOnlySpan<byte> remaining, ref int consumed)
    {
        if (_walker.IsComplete)
        {
            // A bare codestream ends with its last frame; in a container, the rest of the box is ignored and the other boxes are read
            if (!_isContainer)
                return Finish();

            _boxKind = BoxKind.Skip;
            return null;
        }

        var piece = remaining[..(int)Math.Min(remaining.Length, _boxRemaining)];
        if (_walker.ImageHeader is not null && !_headerReached && (_boxRemaining > 0 || _walker.HasPendingBytes))
        {
            // What follows the image header in the codestream is at hand: no metadata box can precede the frames any more
            _headerReached = true;
            if (_walk == StructureWalk.Header)
            {
                _result = CreateInfo(ImageIdentifyMode.Header);
                _state = State.Done;
                return ParseStatus.Complete;
            }

            if (_walk == StructureWalk.Decode)
            {
                _context.Sequential?.OnHeader(CreateInfo(ImageIdentifyMode.Header));
                _observer!.OnHeaderComplete(this);
                if (TryYield())
                    return ParseStatus.Complete;
            }
        }

        var streamEvent = _walker.Feed(piece, out var taken);
        consumed += taken;
        if (!_boxExtendsToEnd)
        {
            _boxRemaining -= taken;
        }

        switch (streamEvent)
        {
            case JpegXlCodestreamEvent.ImageHeader:
                OnImageHeader();
                return null;

            case JpegXlCodestreamEvent.FrameHeader:
                OnFrameHeader();
                return null;

            case JpegXlCodestreamEvent.FrameComplete:
                return OnFrameComplete();

            case JpegXlCodestreamEvent.PayloadCheckpoint:
                _observer!.OnFramePartial(this, _walker.FrameHeader!, _walker.Toc!, _walker.Payload!, _checkpoint++);
                return TryYield() ? ParseStatus.Complete : null;
        }

        // The walker took everything it was given and needs more
        if (_boxRemaining == 0)
        {
            _state = State.BoxHeader;
            return null;
        }

        return taken < remaining.Length ? null : ParseStatus.NeedMoreData(1);
    }

    private void OnImageHeader()
    {
        var header = Header;
        _context.Limits.EnsureCanvasWithinLimits(header.Width, header.Height);
        JpegXlFeaturePolicy.EnsureSupported(header);

        var encoding = header.ColorEncoding;
        var isGray = encoding.IsGray;
        var hasAlpha = header.AlphaChannelIndex >= 0;
        DefaultPixelFormat = DefaultPixelFormats.ForJpegXl(isGray, hasAlpha, JpegXlFeaturePolicy.IsSixteenBit(header));
        _metadata.Metadata.Orientation = (ExifOrientation)header.Orientation;
        if (encoding.WantIcc)
        {
            // The profile was charged when it was decoded
            var adopted = _walker.IccProfile is { } icc && _metadata.TryAdoptIccProfile(icc, isGray);
            if (header.IsXybEncoded && !(adopted && SrgbProfileRecognition.IsSrgb(_metadata.Metadata.IccProfile!, DefaultPixelFormat)))
                throw JpegXlFormat.Unsupported("The JPEG XL image is stored in the XYB color space and its ICC profile is not sRGB: decoding it requires a color conversion to that profile.", "XYB image with a color profile other than sRGB");
        }
        else if (encoding.IsLinearSrgb)
        {
            _metadata.Metadata.TransferFunction = ColorTransferFunction.Linear;
        }
        else
        {
            // Encodings that ITU-T H.273 cannot express (custom primaries, custom white point, power function) are
            // described by a profile written for them
            _metadata.Metadata.Cicp = JpegXlFeaturePolicy.GetCicp(encoding, out var hasCodePoints);
            if (!hasCodePoints)
            {
                _metadata.Metadata.IccProfile = JpegXlFeaturePolicy.CreateProfile(encoding);
            }
        }

        if (header.HasAnimation)
        {
            Animation = new AnimationMetadata { TotalPlays = header.LoopCount == 0 ? null : (int)header.LoopCount };
        }
    }

    private void OnFrameHeader()
    {
        var frame = _walker.FrameHeader!;
        if (!_walker.IsPreviewFrame)
        {
            // Every frame is checked, shown or not: a frame may be larger than the image
            _context.Limits.EnsureCanvasWithinLimits(frame.Width, frame.Height);
        }

        _walker.BeginPayload(buffer: _walk == StructureWalk.Decode && !_walker.IsPreviewFrame);
        _checkpoint = 0;
        if (_walk == StructureWalk.Decode && !_walker.IsPreviewFrame && _context.Sequential is not null && IsDisplayed(frame))
        {
            // A sequential reader may ask for the frame before it is complete
            var checkpoints = _observer!.GetPayloadCheckpoints(this, frame, _walker.Toc!);
            if (checkpoints.Length > 0)
            {
                _walker.SetPayloadCheckpoints(checkpoints);
            }
        }
    }

    private ParseStatus? OnFrameComplete()
    {
        var frame = _walker.FrameHeader!;
        if (!_walker.IsPreviewFrame)
        {
            var isDisplayed = IsDisplayed(frame);
            if (_walk == StructureWalk.Decode)
            {
                var continues = _observer!.OnFrame(this, frame, _walker.Toc!, _walker.Payload!);
                _walker.ReleasePayload();
                if (isDisplayed)
                {
                    _frameCount++;
                }

                if (!continues)
                    return Finish();

                if (isDisplayed && !_walker.IsComplete && TryYield())
                    return ParseStatus.Complete;
            }
            else if (isDisplayed)
            {
                _context.Tracker.ChargeScannedFrame();
                _frameCount++;
            }
        }

        return null;
    }

    private ParseStatus Finish()
    {
        _state = State.Done;
        _result = CreateInfo(ImageIdentifyMode.FullScan);
        _observer?.OnEnd(this);
        return ParseStatus.Complete;
    }

    private ImageInfo CreateInfo(ImageIdentifyMode mode)
    {
        var header = Header;
        var isGray = header.ColorEncoding.IsGray;
        var hasAlpha = header.AlphaChannelIndex >= 0;
        var colorModel = (isGray, hasAlpha) switch
        {
            (true, false) => ImageColorModel.Grayscale,
            (true, true) => ImageColorModel.GrayscaleAlpha,
            (false, false) => ImageColorModel.Rgb,
            (false, true) => ImageColorModel.Rgba,
        };

        // The frames of an animation are only counted by a full scan: a frame without duration is a layer of the next one
        int? frameCount = !header.HasAnimation ? 1 : mode == ImageIdentifyMode.Header ? null : _frameCount;
        return new ImageInfo(
            ImageFormat.JpegXl,
            Size,
            DefaultPixelFormat,
            colorModel,
            header.BitDepth.BitsPerSample,
            frameCount,
            header.HasAnimation,
            hasPosterFrame: false,
            mayHaveTransparency: hasAlpha,
            Animation,
            _metadata.Metadata,
            mode);
    }
}
