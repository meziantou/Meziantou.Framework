using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Incremental reader of a standalone Windows BMP file: <c>BITMAPFILEHEADER</c>, the DIB header and its masks, the palette,
/// the gap before the pixel data and the padded rows, in the three walks of the codec infrastructure.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>Header walk: stops once the DIB header and its masks are known (the palette is not read).</description></item>
/// <item><description>Full scan: also consumes the palette, the gap and every row, and charges the image to <see cref="ImageResourceLimits.MaxFrames"/>.</description></item>
/// <item><description>Decode: expands each stored row to <see cref="BmpDibLayout.SourcePixelFormat"/> and writes it to the decoder at its displayed position (bottom-up files are written from the last row up).</description></item>
/// </list>
/// <para>
/// Rows are accumulated byte by byte into one private row buffer, so the parser never asks the driver for more than a few
/// contiguous bytes and its memory never depends on the input length. Bytes after the last row are not read.
/// </para>
/// </remarks>
internal sealed class BmpStructureParser : ImageParser<ImageInfo>
{
    private readonly ImageCodecContext _context;
    private readonly StructureWalk _walk;
    private readonly BmpDecoder? _decoder;
    private readonly DecodedMetadataBuilder _metadata;

    private State _state;
    private BmpInfoHeader _pendingHeader;
    private long _position;
    private uint _pixelDataOffset;
    private int _headerLength;
    private long _rowLength;
    private int _rowFilled;
    private int _rowIndex;
    private PooledBuffer? _rowBuffer;
    private PooledBuffer? _expandedRow;
    private ImageInfo? _result;

    public BmpStructureParser(ImageCodecContext context, StructureWalk walk, BmpDecoder? decoder = null)
        : base(ImageFormat.Bmp)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (walk == StructureWalk.Decode)
        {
            ArgumentNullException.ThrowIfNull(decoder);
        }

        _context = context;
        _walk = walk;
        _decoder = walk == StructureWalk.Decode ? decoder : null;
        _metadata = new DecodedMetadataBuilder(context.Tracker, ImageFormat.Bmp);
    }

    private enum State
    {
        FileHeader,
        HeaderSize,
        DibHeader,
        TrailingMasks,
        Palette,
        Gap,
        Rows,
        Done,
    }

    /// <summary>Gets the resolved layout, available once the header is parsed.</summary>
    public BmpDibLayout Layout { get; private set; } = null!;

    /// <summary>Gets the canvas size.</summary>
    public Size Size => new(Layout.Header.Width, Layout.Header.Height);

    /// <summary>Gets the metadata: BMP stores only the physical resolution.</summary>
    public ImageMetadata Metadata => _metadata.Metadata;

    /// <summary>Gets the default working representation.</summary>
    public PixelFormat DefaultPixelFormat => Layout.SourcePixelFormat;

    public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
    {
        consumed = 0;
        while (true)
        {
            switch (_state)
            {
                case State.FileHeader:
                {
                    if (buffer.Length < BmpFormat.FileHeaderLength)
                        return ParseStatus.NeedMoreData(BmpFormat.FileHeaderLength);

                    if (!buffer.StartsWith(BmpFormat.Magic))
                        throw BmpFormat.Invalid("The BMP file signature 'BM' is missing.");

                    _pixelDataOffset = BinaryPrimitives.ReadUInt32LittleEndian(buffer[10..]);
                    Advance(ref consumed, BmpFormat.FileHeaderLength);
                    _state = State.HeaderSize;
                    break;
                }

                case State.HeaderSize:
                {
                    var remaining = buffer[consumed..];
                    if (remaining.Length < BmpFormat.HeaderSizeFieldLength)
                        return ParseStatus.NeedMoreData(BmpFormat.HeaderSizeFieldLength);

                    var headerLength = BinaryPrimitives.ReadUInt32LittleEndian(remaining);
                    if (!BmpInfoHeader.IsSupportedHeaderLength(headerLength))
                        throw CreateUnsupportedHeaderException(headerLength);

                    _headerLength = (int)headerLength;
                    _state = State.DibHeader;
                    break;
                }

                case State.DibHeader:
                {
                    var remaining = buffer[consumed..];
                    if (remaining.Length < _headerLength)
                        return ParseStatus.NeedMoreData(_headerLength);

                    var header = BmpInfoHeader.Parse(remaining[.._headerLength], _headerLength);
                    Advance(ref consumed, _headerLength);
                    if (header.TrailingMaskLength == 0)
                    {
                        OnHeaderComplete(header);
                        if (_walk == StructureWalk.Header)
                            return CompleteHeaderWalk();

                        _state = State.Palette;
                        if (!StartPixelWalk())
                            return ParseStatus.Complete;

                        break;
                    }

                    _pendingHeader = header;
                    _state = State.TrailingMasks;
                    break;
                }

                case State.TrailingMasks:
                {
                    var length = _pendingHeader.TrailingMaskLength;
                    var remaining = buffer[consumed..];
                    if (remaining.Length < length)
                        return ParseStatus.NeedMoreData(length);

                    OnHeaderComplete(_pendingHeader.WithTrailingMasks(remaining[..length]));
                    Advance(ref consumed, length);
                    if (_walk == StructureWalk.Header)
                        return CompleteHeaderWalk();

                    _state = State.Palette;
                    if (!StartPixelWalk())
                        return ParseStatus.Complete;

                    break;
                }

                case State.Palette:
                {
                    var length = Layout.Header.PaletteLength;
                    if (length > 0)
                    {
                        var remaining = buffer[consumed..];
                        if (remaining.Length < length)
                            return ParseStatus.NeedMoreData(length);

                        if (Layout.Header.IsIndexed)
                        {
                            Layout.SetPalette(remaining[..length]);
                        }

                        Advance(ref consumed, length);
                    }

                    _state = State.Gap;
                    break;
                }

                case State.Gap:
                {
                    if (_position < _pixelDataOffset)
                    {
                        var remaining = buffer[consumed..];
                        if (remaining.IsEmpty)
                            return ParseStatus.NeedMoreData(1);

                        var skip = (int)Math.Min(remaining.Length, _pixelDataOffset - _position);
                        Advance(ref consumed, skip);
                        break;
                    }

                    _state = State.Rows;
                    break;
                }

                case State.Rows:
                {
                    var status = ReadRows(buffer, ref consumed);
                    if (!status.IsComplete)
                        return status;

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

    public override ImageInfo GetResult() => _result ?? throw new InvalidOperationException("The BMP data was not parsed.");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _rowBuffer?.Dispose();
            _rowBuffer = null;
            _expandedRow?.Dispose();
            _expandedRow = null;
        }

        base.Dispose(disposing);
    }

    private void Advance(ref int consumed, int count)
    {
        consumed += count;
        _position += count;
    }

    private ParseStatus CompleteHeaderWalk()
    {
        _result = CreateInfo(ImageIdentifyMode.Header);
        _state = State.Done;
        return ParseStatus.Complete;
    }

    private void OnHeaderComplete(BmpInfoHeader header)
    {
        var limits = _context.Limits;
        limits.EnsureCanvasWithinLimits(header.Width, header.Height);
        Layout = BmpDibLayout.Create(header);
        _rowLength = header.RowLength;
        if (_rowLength > CheckedSizes.MaxBufferLength || CheckedSizes.GetRowLength(header.Width, PixelFormats.GetBytesPerPixel(Layout.SourcePixelFormat)) > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(limits);

        _metadata.TrySetResolution(ResolutionConversion.FromBmpPixelsPerMeter(header.HorizontalPixelsPerMeter, header.VerticalPixelsPerMeter));

        var minimumOffset = (long)BmpFormat.FileHeaderLength + header.HeaderLength + header.TrailingMaskLength + header.PaletteLength;
        if (_pixelDataOffset < minimumOffset)
            throw BmpFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The BMP pixel data offset is {_pixelDataOffset}, before the end of the header and palette ({minimumOffset})."));

        if (_walk == StructureWalk.FullScan)
        {
            _context.Tracker.ChargeScannedFrame();
        }
    }

    /// <summary>Prepares the pixel walk; returns false when the sequential reader asked to yield after the header.</summary>
    private bool StartPixelWalk()
    {
        if (_walk != StructureWalk.Decode)
            return true;

        _context.Sequential?.OnHeader(CreateInfo(ImageIdentifyMode.Header));
        _decoder!.OnHeaderComplete(this);
        _rowBuffer = _context.Scope.Rent((int)_rowLength, AllocationKind.DecoderState, clear: false);
        _expandedRow = _context.Scope.Rent(Layout.Header.Width * PixelFormats.GetBytesPerPixel(Layout.SourcePixelFormat), AllocationKind.DecoderState, clear: false);
        return !_context.TryConsumeYield();
    }

    private ParseStatus ReadRows(ReadOnlySpan<byte> buffer, ref int consumed)
    {
        var header = Layout.Header;
        var rowLength = (int)_rowLength;
        while (_rowIndex < header.Height)
        {
            var remaining = buffer[consumed..];
            if (remaining.IsEmpty)
                return ParseStatus.NeedMoreData(1);

            var take = Math.Min(remaining.Length, rowLength - _rowFilled);
            if (_walk == StructureWalk.Decode)
            {
                remaining[..take].CopyTo(_rowBuffer!.Span[_rowFilled..]);
            }

            _rowFilled += take;
            Advance(ref consumed, take);
            if (_rowFilled < rowLength)
                continue;

            _rowFilled = 0;
            if (_walk == StructureWalk.Decode)
            {
                var expanded = _expandedRow!.Span[..(header.Width * PixelFormats.GetBytesPerPixel(Layout.SourcePixelFormat))];
                Layout.ExpandRow(_rowBuffer!.Span[..rowLength], expanded);
                _decoder!.WriteRow(header.IsTopDown ? _rowIndex : header.Height - 1 - _rowIndex, expanded);
            }

            _rowIndex++;
            _context.CancellationToken.ThrowIfCancellationRequested();
        }

        return ParseStatus.Complete;
    }

    private ImageInfo CreateInfo(ImageIdentifyMode mode)
        => new(
            ImageFormat.Bmp,
            Size,
            DefaultPixelFormat,
            Layout.ColorModel,
            Layout.BitsPerComponent,
            frameCount: 1,
            isAnimated: false,
            hasPosterFrame: false,
            mayHaveTransparency: Layout.HasAlpha,
            animation: null,
            _metadata.Metadata,
            mode);

    private static Exception CreateUnsupportedHeaderException(uint headerLength) => headerLength switch
    {
        BmpFormat.CoreHeaderLength => BmpFormat.Unsupported("The BMP uses the 12-byte OS/2 BITMAPCOREHEADER, which this version does not decode.", "Header: BITMAPCOREHEADER"),
        BmpFormat.Os2V2HeaderLength => BmpFormat.Unsupported("The BMP uses the 64-byte OS/2 BITMAPCOREHEADER2, which this version does not decode.", "Header: BITMAPCOREHEADER2"),
        >= 16 and <= 64 => BmpFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The BMP uses an OS/2 DIB header of {headerLength} bytes, which this version does not decode."), "Header: OS/2 DIB header"),
        _ => BmpFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The BMP DIB header size is {headerLength}; 40 (BITMAPINFOHEADER), 52, 56, 108 (BITMAPV4HEADER) or 124 (BITMAPV5HEADER) is expected.")),
    };
}
