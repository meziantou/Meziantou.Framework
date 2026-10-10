using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Incremental reader of a Truevision TGA file: the 18-byte header, the image identification field, the color map and the
/// raw or run-length encoded pixels, in the three walks of the codec infrastructure.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>Header walk: reads the header only.</description></item>
/// <item><description>Full scan: also walks the identification field, the color map and every packet, and charges the image to <see cref="ImageResourceLimits.MaxFrames"/>.</description></item>
/// <item><description>Decode: expands every packet into one stored row, converts it to <see cref="SourcePixelFormat"/> and writes it at its displayed position (bottom-up and right-to-left storage are resolved here).</description></item>
/// </list>
/// <para>
/// Pixels are accumulated into one private row, so the parser never asks the driver for more than a few contiguous bytes and
/// its memory never depends on the input length. Parsing stops at the last pixel: the TGA 2.0 developer area, extension area
/// and footer are located by offsets stored at the <em>end</em> of the file, which a forward-only decoder that never reads
/// past the image data cannot follow. They are not read, so the image descriptor is the only
/// source of the alpha semantics.
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
