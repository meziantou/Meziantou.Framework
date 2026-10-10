using System.Buffers.Binary;
using System.Text.Unicode;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Incremental PNG/APNG chunk walker (W3C PNG specification, APNG specification): signature, chunk lengths and types,
/// CRCs, critical-chunk order, IHDR/PLTE/tRNS validity, APNG <c>acTL</c>/<c>fcTL</c>/<c>fdAT</c> sequence numbers and
/// frame bounds, and the supported metadata chunks (<c>iCCP</c>, <c>eXIf</c>, <c>pHYs</c>, <c>tEXt</c>, <c>zTXt</c>,
/// <c>iTXt</c> including XMP). It never decompresses or allocates pixel data.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>Header walk: stops before the first <c>IDAT</c> chunk (not consumed). Every chunk before it is parsed and CRC-checked.</description></item>
/// <item><description>Full scan: walks to <c>IEND</c>, streaming <c>IDAT</c>/<c>fdAT</c>/unknown chunks through the CRC, and charges each image (poster included) to <see cref="ImageResourceLimits.MaxFrames"/>.</description></item>
/// <item><description>Decode: like a full scan, but payloads go to a <see cref="PngDecodeObserver"/>, which charges frames itself and may stop early.</description></item>
/// </list>
/// Rules: a CRC mismatch, an invalid IHDR, misplaced or duplicate critical chunks, non-consecutive <c>IDAT</c>, inconsistent
/// APNG control data and truncation are <see cref="InvalidImageContentException"/>; an unknown critical chunk is
/// <see cref="UnsupportedImageFeatureException"/>. Unknown ancillary chunks, misplaced or duplicate ancillary chunks and
/// invalid metadata payloads are skipped (streamed, never buffered). An <c>acTL</c> after the first <c>IDAT</c> is ignored
/// (the file is a static PNG, as for non-APNG decoders); <c>fcTL</c>/<c>fdAT</c> are then ignored as ancillary chunks.
/// </remarks>
internal sealed class PngStructureParser : ImageParser<ImageInfo>
{
    private const uint ChunkIhdr = 0x49484452;
    private const uint ChunkPlte = 0x504C5445;
    private const uint ChunkIdat = 0x49444154;
    private const uint ChunkIend = 0x49454E44;
    private const uint ChunkTrns = 0x74524E53;
    private const uint ChunkPhys = 0x70485973;
    private const uint ChunkIccp = 0x69434350;
    private const uint ChunkExif = 0x65584966;
    private const uint ChunkText = 0x74455874;
    private const uint ChunkZtxt = 0x7A545874;
    private const uint ChunkItxt = 0x69545874;
    private const uint ChunkActl = 0x6163544C;
    private const uint ChunkFctl = 0x6663544C;
    private const uint ChunkFdat = 0x66644154;

    private const string XmpKeyword = "XML:com.adobe.xmp";

    private readonly ImageCodecContext _context;
    private readonly StructureWalk _walk;
    private readonly PngDecodeObserver? _observer;
    private readonly DecodedMetadataBuilder _metadata;

    private State _state;
    private uint _chunkType;
    private long _chunkRemaining;
    private uint _runningCrc;
    private bool _deliverChunkData;

    private bool _seenIhdr;
    private bool _seenIdat;
    private bool _defaultImageStarted;
    private bool _inIdatRun;
    private bool _idatRunEnded;
    private bool _seenTrns;
    private bool _seenPhys;
    private bool _seenIccp;
    private bool _seenActl;
    private bool _seenExif;

    private bool _imageOpen;
    private bool _imageIsIdat;
    private bool _imageHasData;

    private uint _nextSequenceNumber;
    private int _displayedFrames;
    private PngFrameControl? _previousFrame;
    private bool _transparencyFromAnimation;
    private ImageInfo? _result;

    public PngStructureParser(ImageCodecContext context, StructureWalk walk, PngDecodeObserver? observer = null)
        : base(ImageFormat.Png)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (walk == StructureWalk.Decode)
        {
            ArgumentNullException.ThrowIfNull(observer);
        }

        _context = context;
        _walk = walk;
        _observer = walk == StructureWalk.Decode ? observer : null;
        _metadata = new DecodedMetadataBuilder(context.Tracker, ImageFormat.Png);
    }

    private enum State
    {
        Signature,
        ChunkHeader,
        ChunkData,
        ChunkCrc,
        Done,
    }

    private enum ChunkAction
    {
        Buffer,
        Stream,
        Stop,

        /// <summary>Return to the sequential reader without consuming the chunk header (it is processed again by the next call).</summary>
        Yield,
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public Size Size => new(Width, Height);

    public byte BitDepth { get; private set; }

    /// <summary>Gets the IHDR color type: 0 gray, 2 RGB, 3 palette, 4 gray+alpha, 6 RGBA.</summary>
    public byte ColorType { get; private set; }

    /// <summary>Gets the IHDR interlace method: 0 none, 1 Adam7.</summary>
    public byte InterlaceMethod { get; private set; }

    /// <summary>Gets the PLTE entries (RGB triplets), or empty.</summary>
    public ReadOnlyMemory<byte> Palette { get; private set; }

    /// <summary>Gets the raw tRNS chunk data, or <see langword="null"/> when there is none.</summary>
    public ReadOnlyMemory<byte>? Transparency { get; private set; }

    /// <summary>Gets a value indicating whether the file is an APNG (<c>acTL</c> before the first <c>IDAT</c>).</summary>
    public bool IsAnimated { get; private set; }

    /// <summary>Gets the declared number of APNG frames (<c>acTL num_frames</c>).</summary>
    public int AnimationFrameCount { get; private set; }

    /// <summary>Gets the total plays of the APNG (<see langword="null"/>: infinite).</summary>
    public int? TotalPlays { get; private set; }

    /// <summary>Gets the <c>fcTL</c> preceding the first <c>IDAT</c> (the default image is frame zero), if any.</summary>
    public PngFrameControl? FirstFrameControl { get; private set; }

    /// <summary>Gets a value indicating whether the default image is a separate poster (APNG without <c>fcTL</c> before <c>IDAT</c>).</summary>
    public bool HasSeparatePoster => IsAnimated && FirstFrameControl is null;

    /// <summary>Gets the number of displayed frames traversed so far.</summary>
    public int DisplayedFrameCount => _displayedFrames;

    /// <summary>Gets the metadata collected so far.</summary>
    public ImageMetadata Metadata => _metadata.Metadata;

    /// <summary>Gets the animation settings of an APNG, or <see langword="null"/>.</summary>
    public AnimationMetadata? Animation => IsAnimated ? new AnimationMetadata { TotalPlays = TotalPlays } : null;

    /// <summary>Gets the default working representation.</summary>
    public PixelFormat DefaultPixelFormat => DefaultPixelFormats.ForPng(ColorType, BitDepth, Transparency is not null, IsAnimated);

    /// <summary>Gets a value indicating whether the encoded samples are grayscale (color types 0 and 4).</summary>
    public bool IsGrayscale => ColorType is 0 or 4;

    public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
    {
        consumed = 0;
        while (true)
        {
            var remaining = buffer[consumed..];
            switch (_state)
            {
                case State.Signature:
                    if (remaining.Length < 8)
                        return ParseStatus.NeedMoreData(8);

                    if (!PngCodec.MatchesSignature(remaining))
                        throw Invalid("The PNG signature is invalid.");

                    consumed += 8;
                    _state = State.ChunkHeader;
                    break;

                case State.ChunkHeader:
                {
                    if (remaining.Length < 8)
                        return ParseStatus.NeedMoreData(8);

                    var length = BinaryPrimitives.ReadUInt32BigEndian(remaining);
                    var type = BinaryPrimitives.ReadUInt32BigEndian(remaining[4..]);
                    ValidateChunkHeader(length, type, remaining.Slice(4, 4));
                    var action = OnChunkHeader(type, (int)length);
                    if (action == ChunkAction.Stop)
                    {
                        _state = State.Done;
                        return ParseStatus.Complete;
                    }

                    if (action == ChunkAction.Yield)
                        return ParseStatus.Complete;

                    if (action == ChunkAction.Buffer)
                    {
                        var total = 12 + (int)length;
                        if (remaining.Length < total)
                            return ParseStatus.NeedMoreData(total);

                        var crc = Crc32.Compute(remaining.Slice(4, 4 + (int)length));
                        if (crc != BinaryPrimitives.ReadUInt32BigEndian(remaining[(8 + (int)length)..]))
                            throw CrcMismatch(type);

                        consumed += total;
                        ProcessBufferedChunk(type, remaining.Slice(8, (int)length));
                        if (_state == State.Done)
                            return ParseStatus.Complete;

                        break;
                    }

                    // Streamed chunk (IDAT, fdAT, skipped ancillary): fdAT starts with its 4-byte sequence number
                    _chunkType = type;
                    _runningCrc = Crc32.Update(Crc32.Initial, remaining.Slice(4, 4));
                    _chunkRemaining = length;
                    if (type == ChunkFdat && _deliverChunkData)
                    {
                        if (remaining.Length < 12)
                            return ParseStatus.NeedMoreData(12);

                        OnFrameDataSequence(BinaryPrimitives.ReadUInt32BigEndian(remaining[8..]));
                        _runningCrc = Crc32.Update(_runningCrc, remaining.Slice(8, 4));
                        _chunkRemaining -= 4;
                        consumed += 12;
                    }
                    else
                    {
                        consumed += 8;
                    }

                    _state = State.ChunkData;
                    break;
                }

                case State.ChunkData:
                {
                    if (_chunkRemaining == 0)
                    {
                        _state = State.ChunkCrc;
                        break;
                    }

                    if (remaining.IsEmpty)
                        return ParseStatus.NeedMoreData(1);

                    var piece = remaining[..(int)Math.Min(remaining.Length, _chunkRemaining)];
                    _runningCrc = Crc32.Update(_runningCrc, piece);
                    if (_deliverChunkData && !piece.IsEmpty)
                    {
                        _observer?.OnImageData(piece);
                    }

                    consumed += piece.Length;
                    _chunkRemaining -= piece.Length;
                    break;
                }

                case State.ChunkCrc:
                    if (remaining.Length < 4)
                        return ParseStatus.NeedMoreData(4);

                    if (Crc32.Finish(_runningCrc) != BinaryPrimitives.ReadUInt32BigEndian(remaining))
                        throw CrcMismatch(_chunkType);

                    consumed += 4;
                    _state = State.ChunkHeader;
                    break;

                default:
                    return ParseStatus.Complete;
            }
        }
    }

    public override ImageInfo GetResult() => _result ?? throw new InvalidOperationException("The PNG structure was not parsed.");

    private static void ValidateChunkHeader(uint length, uint type, ReadOnlySpan<byte> typeBytes)
    {
        if (length > int.MaxValue - 12)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The PNG chunk length {length} exceeds the PNG limit (2^31 - 1)."));

        foreach (var value in typeBytes)
        {
            if (value is not ((>= (byte)'A' and <= (byte)'Z') or (>= (byte)'a' and <= (byte)'z')))
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The PNG chunk type 0x{type:X8} is not made of ASCII letters."));
        }
    }

    private ChunkAction OnChunkHeader(uint type, int length)
    {
        _deliverChunkData = false;
        if (!_seenIhdr && type != ChunkIhdr)
            throw Invalid("The first PNG chunk must be IHDR.");

        // The end of an IDAT run ends the default image. Yield points (sequential readers) return before the chunk header
        // is consumed; every state change before them is idempotent, so the next call processes the same header again.
        if (_inIdatRun && type != ChunkIdat)
        {
            _inIdatRun = false;
            _idatRunEnded = true;
            if (!EndImage())
                return ChunkAction.Stop;

            if (TryYield())
                return ChunkAction.Yield;
        }

        switch (type)
        {
            case ChunkIhdr:
                if (_seenIhdr)
                    throw Invalid("The PNG file has several IHDR chunks.");

                if (length != 13)
                    throw Invalid("The IHDR chunk must be 13 bytes long.");

                return ChunkAction.Buffer;

            case ChunkPlte:
                if (_seenIdat)
                    throw Invalid("The PLTE chunk must precede the image data.");

                if (!Palette.IsEmpty)
                    throw Invalid("The PNG file has several PLTE chunks.");

                if (length is 0 or > 768 || length % 3 != 0)
                    throw Invalid("The PLTE chunk length must be a positive multiple of 3, at most 768.");

                return ChunkAction.Buffer;

            case ChunkIdat:
                if (!_seenIdat)
                {
                    OnFirstImageData();
                    if (_walk == StructureWalk.Header)
                        return ChunkAction.Stop;

                    if (TryYield())
                        return ChunkAction.Yield;
                }

                if (!_defaultImageStarted)
                {
                    _defaultImageStarted = true;
                    StartImage(isIdat: true);
                }
                else if (_idatRunEnded)
                {
                    throw Invalid("The IDAT chunks must be consecutive.");
                }

                _inIdatRun = true;
                _imageHasData = true;
                _deliverChunkData = true;
                return ChunkAction.Stream;

            case ChunkIend:
                if (length != 0)
                    throw Invalid("The IEND chunk must be empty.");

                if (!_seenIdat)
                    throw Invalid("The PNG file has no IDAT chunk.");

                if (_imageOpen)
                {
                    if (!EndImage())
                        return ChunkAction.Stop;

                    if (TryYield())
                        return ChunkAction.Yield;
                }

                return ChunkAction.Buffer;

            case ChunkActl:
                if (_seenIdat)
                    return ChunkAction.Stream;

                if (_seenActl)
                    throw Invalid("The APNG file has several acTL chunks.");

                if (length != 8)
                    throw Invalid("The acTL chunk must be 8 bytes long.");

                return ChunkAction.Buffer;

            case ChunkFctl:
                if (!_seenActl)
                    return ChunkAction.Stream;

                if (length != 26)
                    throw Invalid("The fcTL chunk must be 26 bytes long.");

                if (_imageOpen && !_imageIsIdat)
                {
                    if (!EndImage())
                        return ChunkAction.Stop;

                    if (TryYield())
                        return ChunkAction.Yield;
                }

                return ChunkAction.Buffer;

            case ChunkFdat:
                if (!_seenActl)
                    return ChunkAction.Stream;

                if (length < 4)
                    throw Invalid("The fdAT chunk is shorter than its sequence number.");

                if (!_seenIdat)
                    throw Invalid("An fdAT chunk precedes the IDAT chunks.");

                if (!_imageOpen || _imageIsIdat)
                    throw Invalid("An fdAT chunk is not preceded by an fcTL chunk.");

                _imageHasData = true;
                _deliverChunkData = true;
                return ChunkAction.Stream;

            case ChunkTrns:
                if (_seenIdat || _seenTrns || ColorType is 4 or 6)
                    return ChunkAction.Stream;

                if (ColorType == 3 && Palette.IsEmpty)
                    throw Invalid("The tRNS chunk must follow the PLTE chunk.");

                var expected = ColorType switch { 0 => 2, 2 => 6, _ => -1 };
                if ((expected >= 0 && length != expected) || (ColorType == 3 && length > Palette.Length / 3))
                    throw Invalid("The tRNS chunk length is invalid for the color type.");

                return ChunkAction.Buffer;

            case ChunkPhys:
                if (_seenIdat || _seenPhys)
                    return ChunkAction.Stream;

                if (length != 9)
                    throw Invalid("The pHYs chunk must be 9 bytes long.");

                return ChunkAction.Buffer;

            case ChunkIccp:
                return _seenIdat || _seenIccp ? ChunkAction.Stream : ChunkAction.Buffer;

            case ChunkExif:
                return _seenExif ? ChunkAction.Stream : ChunkAction.Buffer;

            case ChunkText:
            case ChunkZtxt:
            case ChunkItxt:
                return ChunkAction.Buffer;

            default:
                // Bit 5 of the first type byte clear (uppercase) marks a critical chunk the decoder must understand
                if ((type & 0x20000000) == 0)
                    throw new UnsupportedImageFeatureException($"The PNG file contains the unknown critical chunk '{ChunkName(type)}'.", ImageFormat.Png, "PNG critical chunk " + ChunkName(type));

                return ChunkAction.Stream;
        }
    }

    private void ProcessBufferedChunk(uint type, ReadOnlySpan<byte> data)
    {
        switch (type)
        {
            case ChunkIhdr:
                ProcessHeader(data);
                break;

            case ChunkPlte:
                if (ColorType is 0 or 4)
                    throw Invalid("A PLTE chunk is not allowed for grayscale images.");

                if (ColorType == 3 && data.Length / 3 > 1 << BitDepth)
                    throw Invalid("The palette has more entries than the bit depth allows.");

                Palette = data.ToArray();
                break;

            case ChunkTrns:
                _seenTrns = true;
                Transparency = data.ToArray();
                break;

            case ChunkActl:
                ProcessAnimationControl(data);
                break;

            case ChunkFctl:
                ProcessFrameControl(data);
                break;

            case ChunkPhys:
                _seenPhys = true;
                _metadata.TrySetResolution(ResolutionConversion.FromPngPhys(BinaryPrimitives.ReadUInt32BigEndian(data), BinaryPrimitives.ReadUInt32BigEndian(data[4..]), data[8]));
                break;

            case ChunkIccp:
                _seenIccp = true;
                ProcessIccProfile(data);
                break;

            case ChunkExif:
                _seenExif = true;
                _metadata.TryAdoptExif(data);
                break;

            case ChunkText:
                ProcessText(data);
                break;

            case ChunkZtxt:
                ProcessCompressedText(data);
                break;

            case ChunkItxt:
                ProcessInternationalText(data);
                break;

            case ChunkIend:
                OnEnd();
                break;
        }
    }

    private void ProcessHeader(ReadOnlySpan<byte> data)
    {
        _seenIhdr = true;
        var width = BinaryPrimitives.ReadUInt32BigEndian(data);
        var height = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        if (width is 0 or > int.MaxValue || height is 0 or > int.MaxValue)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The PNG dimensions {width}x{height} are invalid (1 to 2^31 - 1)."));

        var bitDepth = data[8];
        var colorType = data[9];
        var valid = colorType switch
        {
            0 => bitDepth is 1 or 2 or 4 or 8 or 16,
            2 or 4 or 6 => bitDepth is 8 or 16,
            3 => bitDepth is 1 or 2 or 4 or 8,
            _ => false,
        };

        if (!valid)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The PNG color type {colorType} with bit depth {bitDepth} is not a legal combination."));

        if (data[10] != 0)
            throw Invalid("The PNG compression method must be 0.");

        if (data[11] != 0)
            throw Invalid("The PNG filter method must be 0.");

        if (data[12] > 1)
            throw Invalid("The PNG interlace method must be 0 or 1.");

        Width = (int)width;
        Height = (int)height;
        BitDepth = bitDepth;
        ColorType = colorType;
        InterlaceMethod = data[12];
        _context.Limits.EnsureCanvasWithinLimits(Width, Height);
    }

    private void ProcessAnimationControl(ReadOnlySpan<byte> data)
    {
        _seenActl = true;
        var frameCount = BinaryPrimitives.ReadUInt32BigEndian(data);
        if (frameCount is 0 or > int.MaxValue)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The APNG frame count {frameCount} is invalid."));

        AnimationFrameCount = (int)frameCount;
        TotalPlays = AnimationTiming.FromApngNumPlays(BinaryPrimitives.ReadUInt32BigEndian(data[4..]));
    }

    private void ProcessFrameControl(ReadOnlySpan<byte> data)
    {
        CheckSequenceNumber(BinaryPrimitives.ReadUInt32BigEndian(data));
        var width = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(data[8..]);
        var x = BinaryPrimitives.ReadUInt32BigEndian(data[12..]);
        var y = BinaryPrimitives.ReadUInt32BigEndian(data[16..]);
        if (width == 0 || height == 0 || (ulong)x + width > (ulong)Width || (ulong)y + height > (ulong)Height)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The APNG frame region {width}x{height} at ({x}, {y}) is empty or outside the {Width}x{Height} canvas."));

        var dispose = data[24];
        var blend = data[25];
        if (dispose > PngFrameControl.DisposePrevious)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The APNG dispose_op {dispose} is invalid."));

        if (blend > PngFrameControl.BlendOver)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The APNG blend_op {blend} is invalid."));

        var control = new PngFrameControl(
            BinaryPrimitives.ReadUInt32BigEndian(data),
            (int)width,
            (int)height,
            (int)x,
            (int)y,
            BinaryPrimitives.ReadUInt16BigEndian(data[20..]),
            BinaryPrimitives.ReadUInt16BigEndian(data[22..]),
            dispose,
            blend);

        if (!_seenIdat)
        {
            if (FirstFrameControl is not null)
                throw Invalid("Several fcTL chunks precede the IDAT chunks.");

            if (!control.CoversCanvas(Width, Height))
                throw Invalid("The fcTL chunk of the default image must cover the whole canvas.");

            FirstFrameControl = control;
            return;
        }

        StartFrame(control);
        StartImage(isIdat: false, control);
    }

    private void OnFrameDataSequence(uint sequenceNumber) => CheckSequenceNumber(sequenceNumber);

    private void CheckSequenceNumber(uint sequenceNumber)
    {
        if (sequenceNumber != _nextSequenceNumber)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The APNG sequence number {sequenceNumber} was expected to be {_nextSequenceNumber}."));

        _nextSequenceNumber++;
    }

    private void OnFirstImageData()
    {
        _seenIdat = true;
        if (ColorType == 3 && Palette.IsEmpty)
            throw Invalid("A palette image has no PLTE chunk before its image data.");

        IsAnimated = _seenActl;
        if (_walk == StructureWalk.Header)
        {
            _result = CreateInfo(ImageIdentifyMode.Header);
            return;
        }

        // Sequential readers: the header snapshot is their Info; the walk then yields before the first image
        _context.Sequential?.OnHeader(CreateInfo(ImageIdentifyMode.Header));
        _observer?.OnHeaderComplete(this);
    }

    private bool TryYield() => _walk == StructureWalk.Decode && _context.TryConsumeYield();

    private void StartImage(bool isIdat, PngFrameControl? control = null)
    {
        if (isIdat)
        {
            if (FirstFrameControl is { } first)
            {
                StartFrame(first);
                control = first;
            }
            else if (_walk == StructureWalk.FullScan)
            {
                // Static image or separate poster: one frame for MaxFrames, displayed or not
                _context.Tracker.ChargeScannedFrame();
            }
        }

        _imageOpen = true;
        _imageIsIdat = isIdat;
        _imageHasData = false;
        _observer?.OnImageStart(control);
    }

    private void StartFrame(PngFrameControl control)
    {
        _displayedFrames++;
        if (_displayedFrames > AnimationFrameCount)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The APNG file has more frames than the {AnimationFrameCount} declared by acTL."));

        if (_walk == StructureWalk.FullScan)
        {
            _context.Tracker.ChargeScannedFrame();
        }

        // Transparent black can be displayed when the first frame leaves part of the canvas uncovered, or when a frame
        // that is not the last one is disposed to the background (PREVIOUS on the first frame behaves like BACKGROUND)
        if (_previousFrame is null)
        {
            _transparencyFromAnimation |= !control.CoversCanvas(Width, Height);
        }
        else if (_previousFrame.Value.DisposeOp == PngFrameControl.DisposeBackground || (_displayedFrames == 2 && _previousFrame.Value.DisposeOp == PngFrameControl.DisposePrevious))
        {
            _transparencyFromAnimation = true;
        }

        _previousFrame = control;
    }

    private bool EndImage()
    {
        if (!_imageHasData)
            throw Invalid("An APNG frame has no fdAT chunk.");

        _imageOpen = false;
        return _observer?.OnImageEnd() ?? true;
    }

    private void OnEnd()
    {
        if (IsAnimated && _displayedFrames != AnimationFrameCount)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The APNG file has {_displayedFrames} frames but acTL declares {AnimationFrameCount}."));

        _state = State.Done;
        _result = CreateInfo(ImageIdentifyMode.FullScan);
        _observer?.OnEnd(this);
    }

    private ImageInfo CreateInfo(ImageIdentifyMode mode)
    {
        var colorModel = ColorType switch
        {
            0 => ImageColorModel.Grayscale,
            2 => ImageColorModel.Rgb,
            3 => ImageColorModel.Indexed,
            4 => ImageColorModel.GrayscaleAlpha,
            _ => ImageColorModel.Rgba,
        };

        bool? mayHaveTransparency;
        if (ColorType is 4 or 6 || Transparency is not null)
        {
            mayHaveTransparency = true;
        }
        else if (!IsAnimated)
        {
            mayHaveTransparency = false;
        }
        else if (mode == ImageIdentifyMode.Header)
        {
            mayHaveTransparency = AnimationFrameCount == 1 && FirstFrameControl is not null ? false : null;
        }
        else
        {
            mayHaveTransparency = _transparencyFromAnimation;
        }

        int frameCount;
        if (!IsAnimated)
        {
            frameCount = 1;
        }
        else
        {
            frameCount = mode == ImageIdentifyMode.Header ? AnimationFrameCount : _displayedFrames;
        }

        return new ImageInfo(
            ImageFormat.Png,
            Size,
            DefaultPixelFormat,
            colorModel,
            BitDepth,
            frameCount,
            IsAnimated,
            HasSeparatePoster,
            mayHaveTransparency,
            Animation,
            _metadata.Metadata,
            mode);
    }

    private void ProcessIccProfile(ReadOnlySpan<byte> data)
    {
        // Profile name (1-79 bytes), NUL, compression method 0, zlib datastream
        var separator = data.IndexOf((byte)0);
        if (separator is < 1 or > 79 || separator + 2 > data.Length || data[separator + 1] != 0)
            return;

        var profile = BoundedInflater.TryInflateZlib(data[(separator + 2)..], _context.Tracker);
        if (profile is not null)
        {
            _metadata.TryAdoptIccProfile(profile, IsGrayscale);
        }
    }

    private void ProcessText(ReadOnlySpan<byte> data)
    {
        var separator = data.IndexOf((byte)0);
        if (separator is < 1 or > 79)
            return;

        _metadata.Charge(data.Length);
        _metadata.AddText(Encoding.Latin1.GetString(data[..separator]), Encoding.Latin1.GetString(data[(separator + 1)..]));
    }

    private void ProcessCompressedText(ReadOnlySpan<byte> data)
    {
        var separator = data.IndexOf((byte)0);
        if (separator is < 1 or > 79 || separator + 2 > data.Length || data[separator + 1] != 0)
            return;

        var text = BoundedInflater.TryInflateZlib(data[(separator + 2)..], _context.Tracker);
        if (text is null)
            return;

        _metadata.Charge(separator);
        _metadata.AddText(Encoding.Latin1.GetString(data[..separator]), Encoding.Latin1.GetString(text));
    }

    private void ProcessInternationalText(ReadOnlySpan<byte> data)
    {
        // Keyword NUL, compression flag, compression method, language tag NUL, translated keyword NUL, UTF-8 text
        var keywordEnd = data.IndexOf((byte)0);
        if (keywordEnd is < 1 or > 79 || keywordEnd + 3 > data.Length)
            return;

        var compressed = data[keywordEnd + 1];
        if (compressed > 1 || (compressed == 1 && data[keywordEnd + 2] != 0))
            return;

        var rest = data[(keywordEnd + 3)..];
        var languageEnd = rest.IndexOf((byte)0);
        if (languageEnd < 0)
            return;

        var language = rest[..languageEnd];
        rest = rest[(languageEnd + 1)..];
        var translatedEnd = rest.IndexOf((byte)0);
        if (translatedEnd < 0)
            return;

        var translated = rest[..translatedEnd];
        var payload = rest[(translatedEnd + 1)..];
        byte[]? inflated = null;
        if (compressed == 1)
        {
            inflated = BoundedInflater.TryInflateZlib(payload, _context.Tracker);
            if (inflated is null)
                return;

            payload = inflated;
        }

        if (!Utf8.IsValid(payload) || !Utf8.IsValid(translated) || !System.Text.Ascii.IsValid(language))
            return;

        var keyword = Encoding.Latin1.GetString(data[..keywordEnd]);
        if (string.Equals(keyword, XmpKeyword, StringComparison.Ordinal))
        {
            // A decompressed packet was already charged by the inflater
            _metadata.TryAdoptXmp(payload, alreadyCharged: inflated is not null);
            return;
        }

        _metadata.Charge(keywordEnd + language.Length + translated.Length + (inflated is null ? payload.Length : 0));
        _metadata.AddText(keyword, Encoding.UTF8.GetString(payload), Encoding.ASCII.GetString(language), Encoding.UTF8.GetString(translated));
    }

    private static string ChunkName(uint type)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, type);
        return Encoding.ASCII.GetString(bytes);
    }

    private static InvalidImageContentException CrcMismatch(uint type) => Invalid($"The CRC of the PNG chunk '{ChunkName(type)}' does not match its data.");

    private static InvalidImageContentException Invalid(string message) => new(message, ImageFormat.Png);
}
