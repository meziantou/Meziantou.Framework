using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Incremental reader of a Truevision TGA file: the 18-byte header, the image identification field, the color map, the
/// raw or run-length encoded pixels and the TGA 2.0 trailer, in the three walks of the codec infrastructure.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>Header walk: reads the header only.</description></item>
/// <item><description>Full scan: also walks the identification field, the color map, every packet and the trailer, and charges the image to <see cref="ImageResourceLimits.MaxFrames"/>.</description></item>
/// <item><description>Decode: expands every packet into one stored row, converts it to <see cref="SourcePixelFormat"/> and writes it at its displayed position (bottom-up and right-to-left storage are resolved here), then validates the trailer before the image is complete.</description></item>
/// </list>
/// <para>
/// Pixels are accumulated into one private row, so the parser never asks the driver for more than a few contiguous bytes of
/// image data and its memory never depends on the size of the image.
/// </para>
/// <para>
/// The TGA 2.0 footer is the last 26 bytes of the file, and it holds the offset of the extension area: nothing after the
/// image data can be interpreted before the end of the input is known. The parser therefore asks the driver for every
/// byte that follows the last pixel (<see cref="ParseStatus.NeedMoreDataOrEnd"/>), and reads the trailer once the input
/// ended. A file without a footer is a TGA 1.0 file, whose trailing bytes mean nothing. The extension area can only
/// <em>reject</em> the image (premultiplied alpha): the representation was fixed from the image descriptor before the first
/// pixel, so the descriptor stays the only source of the alpha channel. The developer area is application data and is not
/// interpreted.
/// </para>
/// </remarks>
internal sealed class TgaStructureParser : ImageParser<ImageInfo>
{
    private readonly ImageCodecContext _context;
    private readonly StructureWalk _walk;
    private readonly TgaDecoder? _decoder;
    private readonly DecodedMetadataBuilder _metadata;
    private readonly byte[] _runPixel = new byte[4];

    private State _state;
    private long _position;
    private int _skipRemaining;
    private int _colorMapFilled;
    private long _pixelsLeft;
    private int _packetRemaining;
    private bool _packetIsRun;
    private int _storedFilled;
    private int _rowIndex;
    private PooledBuffer? _storedRow;
    private PooledBuffer? _sourceRow;
    private ImageInfo? _result;

    public TgaStructureParser(ImageCodecContext context, StructureWalk walk, TgaDecoder? decoder = null)
        : base(ImageFormat.Tga)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (walk == StructureWalk.Decode)
        {
            ArgumentNullException.ThrowIfNull(decoder);
        }

        _context = context;
        _walk = walk;
        _decoder = walk == StructureWalk.Decode ? decoder : null;
        _metadata = new DecodedMetadataBuilder(context.Tracker, ImageFormat.Tga);
    }

    private enum State
    {
        Header,
        ImageId,
        ColorMap,
        Pixels,
        Trailer,
        Done,
    }

    /// <summary>Gets the resolved layout, available once the header is parsed.</summary>
    public TgaPixelLayout Layout { get; private set; } = null!;

    /// <summary>Gets the parsed header.</summary>
    public TgaHeader Header => Layout.Header;

    /// <summary>Gets the canvas size.</summary>
    public Size Size => new(Header.Width, Header.Height);

    /// <summary>Gets a value indicating whether the image descriptor declares a real alpha channel.</summary>
    public bool HasAlpha => Layout.HasAlpha;

    /// <summary>Gets the metadata: TGA stores none this version exposes.</summary>
    public ImageMetadata Metadata => _metadata.Metadata;

    /// <summary>Gets the layout of the rows handed to the decoder, which is also the default working representation.</summary>
    public PixelFormat SourcePixelFormat => Layout.SourcePixelFormat;

    public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
    {
        consumed = 0;
        while (true)
        {
            switch (_state)
            {
                case State.Header:
                {
                    if (buffer.Length < TgaFormat.HeaderLength)
                        return ParseStatus.NeedMoreData(TgaFormat.HeaderLength);

                    OnHeaderComplete(TgaHeader.Parse(buffer[..TgaFormat.HeaderLength]));
                    Advance(ref consumed, TgaFormat.HeaderLength);
                    if (_walk == StructureWalk.Header)
                    {
                        _result = CreateInfo(ImageIdentifyMode.Header);
                        _state = State.Done;
                        return ParseStatus.Complete;
                    }

                    _skipRemaining = Header.IdLength;
                    _state = State.ImageId;
                    if (_walk == StructureWalk.Decode)
                    {
                        _context.Sequential?.OnHeader(CreateInfo(ImageIdentifyMode.Header));
                        _decoder!.OnHeaderComplete(this);
                        _storedRow = _context.Scope.Rent(Layout.StoredRowLength, AllocationKind.DecoderState, clear: false);
                        _sourceRow = _context.Scope.Rent(Layout.SourceRowLength, AllocationKind.DecoderState, clear: false);
                        if (_context.TryConsumeYield())
                            return ParseStatus.Complete;
                    }

                    break;
                }

                case State.ImageId:
                {
                    if (_skipRemaining > 0)
                    {
                        var remaining = buffer[consumed..];
                        if (remaining.IsEmpty)
                            return ParseStatus.NeedMoreData(1);

                        var skip = Math.Min(remaining.Length, _skipRemaining);
                        Advance(ref consumed, skip);
                        _skipRemaining -= skip;
                        break;
                    }

                    _state = State.ColorMap;
                    break;
                }

                case State.ColorMap:
                {
                    var status = ReadColorMap(buffer, ref consumed);
                    if (!status.IsComplete)
                        return status;

                    _state = State.Pixels;
                    break;
                }

                case State.Pixels:
                {
                    var status = Header.IsRunLengthEncoded ? ReadRlePixels(buffer, ref consumed) : ReadRawPixels(buffer, ref consumed);
                    if (!status.IsComplete)
                        return status;

                    _state = State.Trailer;
                    break;
                }

                case State.Trailer:
                {
                    // Every byte after the image data is needed at once, and one more byte than the largest buffer cannot
                    // be held
                    var trailer = buffer[consumed..];
                    if (trailer.Length >= CheckedSizes.MaxBufferLength)
                        throw CheckedSizes.CreateOverflowException(_context.Limits);

                    if (!isEndOfInput)
                        return ParseStatus.NeedMoreDataOrEnd(trailer.Length + 1);

                    ValidateTrailer(trailer);
                    Advance(ref consumed, trailer.Length);
                    _state = State.Done;
                    _result = CreateInfo(ImageIdentifyMode.FullScan);
                    _decoder?.OnEnd(this);
                    return ParseStatus.Complete;
                }

                default:
                    return ParseStatus.Complete;
            }
        }
    }

    public override ImageInfo GetResult() => _result ?? throw new InvalidOperationException("The TGA data was not parsed.");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _storedRow?.Dispose();
            _storedRow = null;
            _sourceRow?.Dispose();
            _sourceRow = null;
        }

        base.Dispose(disposing);
    }

    private void Advance(ref int consumed, int count)
    {
        consumed += count;
        _position += count;
    }

    private void OnHeaderComplete(TgaHeader header)
    {
        Layout = TgaPixelLayout.Create(header);
        var limits = _context.Limits;
        limits.EnsureCanvasWithinLimits(header.Width, header.Height);
        if (CheckedSizes.GetRowLength(header.Width, header.BytesPerStoredPixel) > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(limits);

        _pixelsLeft = (long)header.Width * header.Height;
        if (_walk == StructureWalk.FullScan)
        {
            _context.Tracker.ChargeScannedFrame();
        }
    }

    private ParseStatus ReadColorMap(ReadOnlySpan<byte> buffer, ref int consumed)
    {
        if (!Header.HasColorMap)
            return ParseStatus.Complete;

        var entryBytes = Header.BytesPerColorMapEntry;
        while (_colorMapFilled < Header.ColorMapLength)
        {
            var remaining = buffer[consumed..];
            if (remaining.Length < entryBytes)
                return ParseStatus.NeedMoreData(entryBytes);

            Layout.SetColorMapEntry(Header.ColorMapFirstEntry + _colorMapFilled, remaining[..entryBytes]);
            _colorMapFilled++;
            Advance(ref consumed, entryBytes);
        }

        return ParseStatus.Complete;
    }

    private ParseStatus ReadRawPixels(ReadOnlySpan<byte> buffer, ref int consumed)
    {
        var storedRowLength = Layout.StoredRowLength;
        while (_rowIndex < Header.Height)
        {
            var remaining = buffer[consumed..];
            if (remaining.IsEmpty)
                return ParseStatus.NeedMoreData(1);

            var take = Math.Min(remaining.Length, storedRowLength - _storedFilled);
            if (_walk == StructureWalk.Decode)
            {
                remaining[..take].CopyTo(_storedRow!.Span[_storedFilled..]);
            }

            _storedFilled += take;
            Advance(ref consumed, take);
            if (_storedFilled < storedRowLength)
                continue;

            _storedFilled = 0;
            EmitRow();
        }

        return ParseStatus.Complete;
    }

    private ParseStatus ReadRlePixels(ReadOnlySpan<byte> buffer, ref int consumed)
    {
        var bytesPerPixel = Header.BytesPerStoredPixel;
        var storedRowLength = Layout.StoredRowLength;
        while (_rowIndex < Header.Height)
        {
            if (_packetRemaining == 0)
            {
                var remaining = buffer[consumed..];
                if (remaining.IsEmpty)
                    return ParseStatus.NeedMoreData(1);

                var packet = remaining[0];
                _packetIsRun = (packet & TgaFormat.RunLengthPacketFlag) != 0;
                var count = (packet & 0x7F) + 1;
                if (count > _pixelsLeft)
                    throw TgaFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TGA packet of {count} pixels at offset {_position} extends past the last pixel ({_pixelsLeft} pixels are left)."));

                if (_packetIsRun)
                {
                    if (remaining.Length < 1 + bytesPerPixel)
                        return ParseStatus.NeedMoreData(1 + bytesPerPixel);

                    remaining.Slice(1, bytesPerPixel).CopyTo(_runPixel);
                    Advance(ref consumed, 1 + bytesPerPixel);
                }
                else
                {
                    Advance(ref consumed, 1);
                }

                _pixelsLeft -= count;
                _packetRemaining = count;
            }

            if (_packetIsRun)
            {
                var count = Math.Min(_packetRemaining, Header.Width - (_storedFilled / bytesPerPixel));
                if (_walk == StructureWalk.Decode)
                {
                    var destination = _storedRow!.Span.Slice(_storedFilled, count * bytesPerPixel);
                    for (var offset = 0; offset < destination.Length; offset += bytesPerPixel)
                    {
                        _runPixel.AsSpan(0, bytesPerPixel).CopyTo(destination[offset..]);
                    }
                }

                _storedFilled += count * bytesPerPixel;
                _packetRemaining -= count;
            }
            else
            {
                var remaining = buffer[consumed..];
                if (remaining.Length < bytesPerPixel)
                    return ParseStatus.NeedMoreData(bytesPerPixel);

                var available = Math.Min(remaining.Length / bytesPerPixel, _packetRemaining);
                var count = Math.Min(available, Header.Width - (_storedFilled / bytesPerPixel));
                if (_walk == StructureWalk.Decode)
                {
                    remaining[..(count * bytesPerPixel)].CopyTo(_storedRow!.Span[_storedFilled..]);
                }

                _storedFilled += count * bytesPerPixel;
                _packetRemaining -= count;
                Advance(ref consumed, count * bytesPerPixel);
            }

            if (_storedFilled == storedRowLength)
            {
                _storedFilled = 0;
                EmitRow();
            }
        }

        return ParseStatus.Complete;
    }

    /// <summary>Reads the TGA 2.0 footer and the extension area it points at.</summary>
    /// <param name="trailer">Every byte between the end of the image data (the current position) and the end of the input.</param>
    private void ValidateTrailer(ReadOnlySpan<byte> trailer)
    {
        // Without the signature in its last bytes, the file is a TGA 1.0 file: what follows the image data is not a trailer
        if (trailer.Length < TgaFormat.FooterLength || !trailer.EndsWith(TgaFormat.FooterSignature))
            return;

        var imageDataEnd = _position;
        var footerOffset = imageDataEnd + trailer.Length - TgaFormat.FooterLength;
        long extensionOffset = BinaryPrimitives.ReadUInt32LittleEndian(trailer[^TgaFormat.FooterLength..]);
        if (extensionOffset == 0)
            return;

        if (extensionOffset < imageDataEnd || extensionOffset > footerOffset - TgaFormat.ExtensionAreaLength)
            throw TgaFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TGA footer locates the {TgaFormat.ExtensionAreaLength}-byte extension area at offset {extensionOffset}, outside the bytes between the end of the image data ({imageDataEnd}) and the footer ({footerOffset})."));

        var extension = trailer.Slice((int)(extensionOffset - imageDataEnd), TgaFormat.ExtensionAreaLength);
        var extensionSize = BinaryPrimitives.ReadUInt16LittleEndian(extension);
        if (extensionSize < TgaFormat.ExtensionAreaLength)
            throw TgaFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TGA extension area at offset {extensionOffset} declares {extensionSize} bytes, fewer than the {TgaFormat.ExtensionAreaLength} bytes of a TGA 2.0 extension area."));

        // The other attributes types say whether the alpha data is meaningful, which the image descriptor already decided
        // before the first pixel was read
        if (extension[TgaFormat.ExtensionAttributesTypeOffset] == TgaFormat.AttributesTypePremultipliedAlpha && HasAlpha)
            throw TgaFormat.Unsupported("The TGA extension area declares premultiplied alpha (attributes type 4); the working pixel formats store straight alpha and this version never divides it out.", "Attributes type: premultiplied alpha");
    }

    private void EmitRow()
    {
        if (_walk == StructureWalk.Decode)
        {
            var source = _sourceRow!.Span[..Layout.SourceRowLength];
            Layout.ExpandRow(_storedRow!.Span[..Layout.StoredRowLength], source);
            _decoder!.WriteRow(Header.IsTopToBottom ? _rowIndex : Header.Height - 1 - _rowIndex, source);
        }

        _rowIndex++;
        _context.CancellationToken.ThrowIfCancellationRequested();
    }

    private ImageInfo CreateInfo(ImageIdentifyMode mode)
        => new(
            ImageFormat.Tga,
            Size,
            SourcePixelFormat,
            Layout.ColorModel,
            bitsPerComponent: 8,
            frameCount: 1,
            isAnimated: false,
            hasPosterFrame: false,
            mayHaveTransparency: HasAlpha,
            animation: null,
            _metadata.Metadata,
            mode);
}
