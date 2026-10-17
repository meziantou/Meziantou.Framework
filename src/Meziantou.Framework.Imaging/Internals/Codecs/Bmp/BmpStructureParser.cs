using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Incremental reader of a standalone BMP file: <c>BITMAPFILEHEADER</c>, the Windows or OS/2 DIB header and its masks, the
/// palette, the gap before the pixel data and the padded rows or the run-length stream, in the three walks of the codec
/// infrastructure.
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
/// <para>
/// A run-length stream (<c>BI_RLE8</c>, <c>BI_RLE4</c>, and <c>BCA_RLE24</c> of an OS/2 2.x header) is decompressed into the
/// same row buffer, in the stored layout of the matching uncompressed bitmap, so the rows are expanded by the same code.
/// The buffer starts every row cleared: the pixels a stream skips (a delta, an early end of line or an early end of bitmap)
/// are palette index 0, or black for 24-bit pixels. The stream is read strictly: a run that leaves its row, a delta that
/// leaves the canvas, data after the last row and a missing end-of-bitmap marker are malformed, never clamped. Bytes after
/// the end-of-bitmap marker are not read.
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
    private BmpHeaderKind _headerKind;
    private long _rowLength;
    private int _rowFilled;
    private int _rowIndex;
    private int _column;
    private int _absoluteBytes;
    private int _absolutePixels;
    private int _absoluteCursor;
    private bool _absolutePadding;
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
                    if (!BmpInfoHeader.TryGetKind(headerLength, out _headerKind))
                        throw BmpFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The BMP DIB header size is {headerLength}; 12 (OS/2 BITMAPCOREHEADER), 16 to 64 (OS/2 BITMAPCOREHEADER2), 40 (BITMAPINFOHEADER), 52, 56, 108 (BITMAPV4HEADER) or 124 (BITMAPV5HEADER) is expected."));

                    _headerLength = (int)headerLength;
                    _state = State.DibHeader;
                    break;
                }

                case State.DibHeader:
                {
                    var remaining = buffer[consumed..];
                    if (remaining.Length < _headerLength)
                        return ParseStatus.NeedMoreData(_headerLength);

                    var header = BmpInfoHeader.Parse(remaining[.._headerLength], _headerLength, _headerKind);
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
                    var status = Layout.Header.IsRunLength ? ReadRunLengthRows(buffer, ref consumed) : ReadRows(buffer, ref consumed);
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
        if (header.Kind == BmpHeaderKind.Os2V1 && header.IsIndexed)
        {
            // BITMAPCOREHEADER has no color count: a file with a short palette only says so by starting its pixel data early
            var available = ((long)_pixelDataOffset - BmpFormat.FileHeaderLength - header.HeaderLength) / header.PaletteEntryLength;
            if (available < header.PaletteEntries)
            {
                if (available < 1)
                    throw BmpFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The BMP pixel data offset is {_pixelDataOffset}, which leaves no room for a palette entry after the {header.HeaderLength}-byte header."));

                header = header.WithPaletteEntries((int)available);
            }
        }

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

        // A run-length stream only writes the pixels it encodes: the others must read as zero
        _rowBuffer = _context.Scope.Rent((int)_rowLength, AllocationKind.DecoderState, clear: Layout.Header.IsRunLength);
        _expandedRow = _context.Scope.Rent(Layout.Header.Width * PixelFormats.GetBytesPerPixel(Layout.SourcePixelFormat), AllocationKind.DecoderState, clear: false);
        return !_context.TryConsumeYield();
    }

    private ParseStatus ReadRows(ReadOnlySpan<byte> buffer, ref int consumed)
    {
        var header = Layout.Header;
        var rowLength = (int)_rowLength;

        // One lease of the frame for the rows of this call, taken when the first of them is complete
        var lease = default(PixelLease);
        try
        {
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
                EmitRow(ref lease);
            }

            return ParseStatus.Complete;
        }
        finally
        {
            lease.Dispose();
        }
    }

    /// <summary>Walks the commands of a run-length stream up to its end-of-bitmap marker.</summary>
    private ParseStatus ReadRunLengthRows(ReadOnlySpan<byte> buffer, ref int consumed)
    {
        var header = Layout.Header;
        var fourBit = header.Compression == BmpDibCompression.Rle4;
        var bytesPerPixel = header.Compression == BmpDibCompression.Rle24 ? 3 : 1;
        while (true)
        {
            var remaining = buffer[consumed..];
            if (_absoluteBytes > 0)
            {
                if (remaining.IsEmpty)
                    return ParseStatus.NeedMoreData(1);

                var take = Math.Min(remaining.Length, _absoluteBytes);
                if (_walk == StructureWalk.Decode)
                {
                    CopyAbsolutePixels(remaining[..take], fourBit);
                }

                _absoluteBytes -= take;
                Advance(ref consumed, take);
                continue;
            }

            if (_absolutePadding)
            {
                // An absolute run is padded to a 16-bit boundary
                if (remaining.IsEmpty)
                    return ParseStatus.NeedMoreData(1);

                _absolutePadding = false;
                Advance(ref consumed, 1);
                continue;
            }

            if (remaining.Length < 2)
                return ParseStatus.NeedMoreData(2);

            int count = remaining[0];
            if (count != 0)
            {
                // Encoded mode: a pixel count, then one pixel (two alternating 4-bit pixels for RLE4)
                if (remaining.Length < 1 + bytesPerPixel)
                    return ParseStatus.NeedMoreData(1 + bytesPerPixel);

                EnsureRunFits(count, "run");
                if (_walk == StructureWalk.Decode)
                {
                    FillRun(count, remaining.Slice(1, bytesPerPixel), fourBit);
                }

                _column += count;
                Advance(ref consumed, 1 + bytesPerPixel);
                continue;
            }

            int escape = remaining[1];
            switch (escape)
            {
                case BmpFormat.RunLengthEndOfLine:
                    EnsureRowAvailable();
                    Advance(ref consumed, 2);
                    EmitRunLengthRow();
                    break;

                case BmpFormat.RunLengthEndOfBitmap:
                    Advance(ref consumed, 2);

                    // Every row the stream did not reach is a row of skipped pixels
                    while (_rowIndex < header.Height)
                    {
                        EmitRunLengthRow();
                    }

                    return ParseStatus.Complete;

                case BmpFormat.RunLengthDelta:
                {
                    if (remaining.Length < 4)
                        return ParseStatus.NeedMoreData(4);

                    EnsureRowAvailable();
                    int right = remaining[2];
                    int up = remaining[3];
                    if (right > header.Width - _column || up >= header.Height - _rowIndex)
                        throw BmpFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The BMP run-length delta ({right}, {up}) at offset {_position} moves from column {_column} of stored row {_rowIndex} to outside the {header.Width}x{header.Height} canvas."));

                    Advance(ref consumed, 4);
                    var column = _column + right;
                    for (var i = 0; i < up; i++)
                    {
                        EmitRunLengthRow();
                    }

                    _column = column;
                    break;
                }

                default:
                {
                    // Absolute mode: the escape is a pixel count (3 to 255), followed by that many literal pixels
                    EnsureRunFits(escape, "absolute run");
                    Advance(ref consumed, 2);
                    _absoluteBytes = fourBit ? (escape + 1) / 2 : escape * bytesPerPixel;
                    _absolutePixels = escape;
                    _absoluteCursor = fourBit ? _column : _column * bytesPerPixel;
                    _absolutePadding = (_absoluteBytes & 1) != 0;
                    _column += escape;
                    break;
                }
            }
        }
    }

    private void EnsureRowAvailable()
    {
        if (_rowIndex >= Layout.Header.Height)
            throw BmpFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The BMP run-length data continues at offset {_position}, after the last of the {Layout.Header.Height} rows; only the end-of-bitmap marker is expected."));
    }

    private void EnsureRunFits(int count, string name)
    {
        EnsureRowAvailable();
        var left = Layout.Header.Width - _column;
        if (count > left)
            throw BmpFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The BMP {name} of {count} pixels at offset {_position} extends past the end of stored row {_rowIndex} ({left} pixels are left)."));
    }

    /// <summary>Writes an encoded run at the current column of the row buffer.</summary>
    private void FillRun(int count, ReadOnlySpan<byte> pixel, bool fourBit)
    {
        var row = _rowBuffer!.Span;
        if (fourBit)
        {
            // The run alternates the two 4-bit pixels of its byte, starting with the high-order one
            for (var i = 0; i < count; i++)
            {
                WriteNibble(row, _column + i, (i & 1) == 0 ? pixel[0] >> 4 : pixel[0] & 0x0F);
            }

            return;
        }

        if (pixel.Length == 1)
        {
            row.Slice(_column, count).Fill(pixel[0]);
            return;
        }

        var destination = row.Slice(_column * pixel.Length, count * pixel.Length);
        for (var offset = 0; offset < destination.Length; offset += pixel.Length)
        {
            pixel.CopyTo(destination[offset..]);
        }
    }

    /// <summary>Writes the next bytes of an absolute run, which may arrive split at any byte.</summary>
    private void CopyAbsolutePixels(ReadOnlySpan<byte> data, bool fourBit)
    {
        var row = _rowBuffer!.Span;
        if (!fourBit)
        {
            data.CopyTo(row[_absoluteCursor..]);
            _absoluteCursor += data.Length;
            return;
        }

        // Each byte stores two 4-bit pixels, high-order first; the low-order half of the last byte is unused for odd counts
        foreach (var value in data)
        {
            WriteNibble(row, _absoluteCursor++, value >> 4);
            _absolutePixels--;
            if (_absolutePixels > 0)
            {
                WriteNibble(row, _absoluteCursor++, value & 0x0F);
                _absolutePixels--;
            }
        }
    }

    private static void WriteNibble(Span<byte> row, int column, int value)
    {
        ref var target = ref row[column >> 1];
        target = (column & 1) == 0 ? (byte)((target & 0x0F) | (value << 4)) : (byte)((target & 0xF0) | value);
    }

    /// <summary>Emits the current row of a run-length stream and starts the next one with every pixel skipped.</summary>
    private void EmitRunLengthRow()
    {
        EmitRow();
        _column = 0;
        if (_walk == StructureWalk.Decode)
        {
            _rowBuffer!.Span[..(int)_rowLength].Clear();
        }
    }

    private void EmitRow()
    {
        var lease = default(PixelLease);
        try
        {
            EmitRow(ref lease);
        }
        finally
        {
            lease.Dispose();
        }
    }

    /// <summary>Emits the current row through a lease of the frame, taken on the first row a parser call emits.</summary>
    private void EmitRow(scoped ref PixelLease lease)
    {
        var header = Layout.Header;
        if (_walk == StructureWalk.Decode)
        {
            var expanded = _expandedRow!.Span[..(header.Width * PixelFormats.GetBytesPerPixel(Layout.SourcePixelFormat))];
            Layout.ExpandRow(_rowBuffer!.Span[..(int)_rowLength], expanded);
            if (!lease.IsActive)
            {
                lease = _decoder!.LeaseRows();
            }

            _decoder!.WriteRow(lease, header.IsTopDown ? _rowIndex : header.Height - 1 - _rowIndex, expanded);
        }

        _rowIndex++;
        _context.CancellationToken.ThrowIfCancellationRequested();
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
}
