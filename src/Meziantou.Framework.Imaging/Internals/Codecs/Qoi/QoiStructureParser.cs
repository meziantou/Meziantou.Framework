using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Incremental QOI reader (QOI specification 1.0): the 14-byte header, the chunk stream and the end marker, in the three
/// walks of the codec infrastructure. QOI has no container: the end of the chunk stream is only known once every pixel is
/// accounted for, so the full scan walks the chunks (lengths and run lengths only, no pixel value is computed) and the
/// decode walk reconstructs the pixels row by row for a <see cref="QoiDecoder"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>Header walk: reads the header only.</description></item>
/// <item><description>Full scan: walks every chunk to the end marker, charges the image to <see cref="ImageResourceLimits.MaxFrames"/>.</description></item>
/// <item><description>
/// Decode: one row buffer and the 64-entry running index; complete rows go to the decoder. Memory does not depend on the
/// input length (no full-file buffer), and the input is consumed as it arrives.
/// </description></item>
/// </list>
/// <para>
/// Defects are <see cref="InvalidImageContentException"/>: a zero width or height, a channel count other than 3 or 4, a
/// colorspace other than 0 or 1, a run extending past the last pixel, a missing or different end marker, and truncation.
/// Bytes after the end marker are not read. The header colorspace is reported as <see cref="ImageMetadata.TransferFunction"/>
/// (1 is <see cref="ColorTransferFunction.Linear"/>), never applied. The channel count is authoritative: a 3-channel stream is
/// decoded as opaque RGB (alpha values of <c>QOI_OP_RGBA</c> chunks still take part in the index hash, as the specification
/// requires, but are not part of the image).
/// </para>
/// </remarks>
internal sealed class QoiStructureParser : ImageParser<ImageInfo>
{
    /// <summary>The number of chunks scanned between two cancellation checks of a full scan.</summary>
    private const int ScanCancellationInterval = 64 * 1024;

    private readonly ImageCodecContext _context;
    private readonly StructureWalk _walk;
    private readonly QoiDecoder? _decoder;
    private readonly DecodedMetadataBuilder _metadata;
    private readonly uint[] _index = new uint[QoiFormat.IndexLength];

    private State _state;
    private long _position;
    private uint _pixel = QoiFormat.InitialPixel;
    private long _pixelsLeft;
    private int _run;
    private int _x;
    private int _y;
    private PooledBuffer? _row;
    private ImageInfo? _result;

    public QoiStructureParser(ImageCodecContext context, StructureWalk walk, QoiDecoder? decoder = null)
        : base(ImageFormat.Qoi)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (walk == StructureWalk.Decode)
        {
            ArgumentNullException.ThrowIfNull(decoder);
        }

        _context = context;
        _walk = walk;
        _decoder = walk == StructureWalk.Decode ? decoder : null;
        _metadata = new DecodedMetadataBuilder(context.Tracker, ImageFormat.Qoi);
    }

    private enum State
    {
        Header,
        Chunks,
        EndMarker,
        Done,
    }

    /// <summary>Gets the image width.</summary>
    public int Width { get; private set; }

    /// <summary>Gets the image height.</summary>
    public int Height { get; private set; }

    public Size Size => new(Width, Height);

    /// <summary>Gets the header channel count: 3 (RGB) or 4 (RGBA).</summary>
    public int Channels { get; private set; }

    /// <summary>Gets the header colorspace: 0 (sRGB with linear alpha) or 1 (all channels linear).</summary>
    public byte ColorSpace { get; private set; }

    /// <summary>Gets the metadata: only the transfer function (QOI stores no other metadata).</summary>
    public ImageMetadata Metadata => _metadata.Metadata;

    /// <summary>Gets the default working representation: <see cref="PixelFormat.Rgba32"/> for 4 channels, otherwise <see cref="PixelFormat.Rgb24"/>.</summary>
    public PixelFormat DefaultPixelFormat => DefaultPixelFormats.ForQoi(Channels);

    public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
    {
        consumed = 0;
        while (true)
        {
            switch (_state)
            {
                case State.Header:
                {
                    if (buffer.Length < QoiFormat.HeaderLength)
                        return ParseStatus.NeedMoreData(QoiFormat.HeaderLength);

                    ProcessHeader(buffer[..QoiFormat.HeaderLength]);
                    Advance(ref consumed, QoiFormat.HeaderLength);
                    if (_walk == StructureWalk.Header)
                    {
                        _result = CreateInfo(ImageIdentifyMode.Header);
                        _state = State.Done;
                        return ParseStatus.Complete;
                    }

                    _state = State.Chunks;
                    if (_walk == StructureWalk.Decode)
                    {
                        _context.Sequential?.OnHeader(CreateInfo(ImageIdentifyMode.Header));
                        _decoder!.OnHeaderComplete(this);
                        _row = _context.Scope.Rent(Width * _decoder.RowChannels, AllocationKind.DecoderState, clear: false);
                        if (_context.TryConsumeYield())
                            return ParseStatus.Complete;
                    }

                    break;
                }

                case State.Chunks:
                {
                    var status = _walk == StructureWalk.Decode ? DecodeChunks(buffer, ref consumed) : ScanChunks(buffer, ref consumed);
                    if (!status.IsComplete)
                        return status;

                    _state = State.EndMarker;
                    break;
                }

                case State.EndMarker:
                {
                    var remaining = buffer[consumed..];
                    if (remaining.Length < QoiFormat.EndMarkerLength)
                        return ParseStatus.NeedMoreData(QoiFormat.EndMarkerLength);

                    if (!remaining[..QoiFormat.EndMarkerLength].SequenceEqual(QoiFormat.EndMarker))
                        throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The QOI end marker (7 zero bytes and 0x01) is missing after the last pixel, at offset {_position}."));

                    Advance(ref consumed, QoiFormat.EndMarkerLength);
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

    public override ImageInfo GetResult() => _result ?? throw new InvalidOperationException("The QOI data was not parsed.");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _row?.Dispose();
            _row = null;
        }

        base.Dispose(disposing);
    }

    private void Advance(ref int consumed, int count)
    {
        consumed += count;
        _position += count;
    }

    private void ProcessHeader(ReadOnlySpan<byte> header)
    {
        if (!QoiCodec.MatchesSignature(header))
            throw Invalid("The QOI magic 'qoif' is missing.");

        var width = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(header[8..]);
        var channels = header[12];
        var colorSpace = header[13];
        if (width == 0 || height == 0)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The QOI image is {width}x{height}: both dimensions must be positive."));

        if (channels is not (3 or 4))
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The QOI channel count is {channels}; 3 (RGB) or 4 (RGBA) is expected."));

        if (colorSpace is not (QoiFormat.ColorSpaceSrgb or QoiFormat.ColorSpaceLinear))
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The QOI colorspace is {colorSpace}; 0 (sRGB with linear alpha) or 1 (all channels linear) is expected."));

        var limits = _context.Limits;
        if (width > int.MaxValue)
            throw new ImageResourceLimitException(ImageResourceLimitKind.Width, limits.MaxWidth, width);

        if (height > int.MaxValue)
            throw new ImageResourceLimitException(ImageResourceLimitKind.Height, limits.MaxHeight, height);

        limits.EnsureCanvasWithinLimits((int)width, (int)height);
        if ((long)width * channels > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(limits);

        Width = (int)width;
        Height = (int)height;
        Channels = channels;
        ColorSpace = colorSpace;
        _pixelsLeft = (long)Width * Height;
        _metadata.Metadata.TransferFunction = colorSpace == QoiFormat.ColorSpaceLinear ? ColorTransferFunction.Linear : ColorTransferFunction.Srgb;
        if (_walk == StructureWalk.FullScan)
        {
            _context.Tracker.ChargeScannedFrame();
        }
    }

    /// <summary>Walks chunks without computing pixels: only chunk lengths and run lengths are needed to find the end marker.</summary>
    private ParseStatus ScanChunks(ReadOnlySpan<byte> buffer, ref int consumed)
    {
        var i = consumed;
        var pixelsLeft = _pixelsLeft;
        var budget = ScanCancellationInterval;
        try
        {
            while (pixelsLeft > 0)
            {
                if (i >= buffer.Length)
                    return ParseStatus.NeedMoreData(1);

                var tag = buffer[i];
                var length = QoiFormat.GetChunkLength(tag);
                if (buffer.Length - i < length)
                    return ParseStatus.NeedMoreData(length);

                var count = tag < QoiFormat.OpRgb && (tag & QoiFormat.TagMask) == QoiFormat.OpRun ? (tag & 0x3F) + 1 : 1;
                if (count > pixelsLeft)
                    throw RunPastEnd(count, pixelsLeft, _position + (i - consumed));

                pixelsLeft -= count;
                i += length;
                if (--budget == 0)
                {
                    _context.CancellationToken.ThrowIfCancellationRequested();
                    budget = ScanCancellationInterval;
                }
            }

            return ParseStatus.Complete;
        }
        finally
        {
            _pixelsLeft = pixelsLeft;
            Advance(ref consumed, i - consumed);
        }
    }

    /// <summary>Reconstructs pixels row by row; each complete row goes to the decoder.</summary>
    private ParseStatus DecodeChunks(ReadOnlySpan<byte> buffer, ref int consumed)
    {
        var width = Width;
        // The rows of an RGB image decoded to RGBA are opaque, whatever the alpha of the pixels in the index
        var channels = _decoder!.RowChannels;
        var opaque = Channels == 3 && channels == 4 ? 0xFF000000 : 0;
        var row = _row!.RawBuffer.AsSpan(0, width * channels);
        var row32 = channels == 4 ? unsafe(MemoryMarshal.Cast<byte, uint>(row)) : default;
        var index = _index.AsSpan();
        var pixel = _pixel;
        var x = _x;
        var i = consumed;

        // One lease of the frame for the rows of this call, taken when the first of them is complete
        var lease = default(PixelLease);
        try
        {
            while (true)
            {
                if (_run > 0)
                {
                    // A run carried over from the previous row
                    var count = Math.Min(_run, width - x);
                    Fill(row, row32, channels, x, count, pixel | opaque);
                    x += count;
                    _run -= count;
                }

                if (x == width)
                {
                    if (!lease.IsActive)
                    {
                        lease = _decoder!.LeaseRows();
                    }

                    _decoder!.WriteRow(lease, _y, row);
                    _y++;
                    x = 0;
                    if (_y == Height)
                        return ParseStatus.Complete;

                    _context.CancellationToken.ThrowIfCancellationRequested();
                    continue;
                }

                // Chunks of the current row: while a chunk of any length fits in the buffer, no length is checked
                if (i <= buffer.Length - QoiFormat.MaxChunkLength)
                {
                    DecodeCompleteChunks(buffer, ref i, consumed, row, row32, index, opaque, ref pixel, ref x);
                    if (x == width)
                        continue;
                }

                while (x < width)
                {
                    if (i >= buffer.Length)
                        return ParseStatus.NeedMoreData(1);

                    var tag = buffer[i];
                    if (tag == QoiFormat.OpRgb)
                    {
                        if (buffer.Length - i < 4)
                            return ParseStatus.NeedMoreData(4);

                        pixel = (pixel & 0xFF000000) | buffer[i + 1] | ((uint)buffer[i + 2] << 8) | ((uint)buffer[i + 3] << 16);
                        i += 4;
                    }
                    else if (tag == QoiFormat.OpRgba)
                    {
                        if (buffer.Length - i < 5)
                            return ParseStatus.NeedMoreData(5);

                        pixel = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(i + 1, 4));
                        i += 5;
                    }
                    else
                    {
                        switch (tag & QoiFormat.TagMask)
                        {
                            case QoiFormat.OpIndex:
                                pixel = index[tag];
                                i++;
                                break;

                            case QoiFormat.OpDiff:
                            {
                                // 2-bit differences with a bias of 2, wrapping around 256
                                var r = (pixel + (uint)((tag >> 4) & 3) - 2) & 0xFF;
                                var g = ((pixel >> 8) + (uint)((tag >> 2) & 3) - 2) & 0xFF;
                                var b = ((pixel >> 16) + (uint)(tag & 3) - 2) & 0xFF;
                                pixel = (pixel & 0xFF000000) | r | (g << 8) | (b << 16);
                                i++;
                                break;
                            }

                            case QoiFormat.OpLuma:
                            {
                                if (buffer.Length - i < 2)
                                    return ParseStatus.NeedMoreData(2);

                                // Green difference with a bias of 32; red and blue differences relative to it, bias 8
                                var second = buffer[i + 1];
                                var dg = (tag & 0x3F) - 32;
                                var r = (pixel + (uint)(dg + (second >> 4) - 8)) & 0xFF;
                                var g = ((pixel >> 8) + (uint)dg) & 0xFF;
                                var b = ((pixel >> 16) + (uint)(dg + (second & 0x0F) - 8)) & 0xFF;
                                pixel = (pixel & 0xFF000000) | r | (g << 8) | (b << 16);
                                i += 2;
                                break;
                            }

                            default:
                            {
                                // QOI_OP_RUN: 1-62 repetitions of the previous pixel; the part past this row is carried over
                                var run = (tag & 0x3F) + 1;
                                var left = ((long)(Height - _y) * width) - x;
                                if (run > left)
                                    throw RunPastEnd(run, left, _position + (i - consumed));

                                index[QoiFormat.Hash(pixel)] = pixel;
                                i++;
                                var count = Math.Min(run, width - x);
                                Fill(row, row32, channels, x, count, pixel | opaque);
                                x += count;
                                _run = run - count;
                                continue;
                            }
                        }
                    }

                    index[QoiFormat.Hash(pixel)] = pixel;
                    if (channels == 4)
                    {
                        row32[x] = pixel | opaque;
                    }
                    else
                    {
                        var offset = x * 3;
                        row[offset] = (byte)pixel;
                        row[offset + 1] = (byte)(pixel >> 8);
                        row[offset + 2] = (byte)(pixel >> 16);
                    }

                    x++;
                }
            }
        }
        finally
        {
            lease.Dispose();
            _pixel = pixel;
            _x = x;
            Advance(ref consumed, i - consumed);
        }
    }

    /// <summary>
    /// Decodes the chunks of the current row while at least <see cref="QoiFormat.MaxChunkLength"/> bytes remain from
    /// <paramref name="position"/>: no chunk can then be incomplete, so the input, the index and the row are read and written
    /// without checks. Stops at the end of the row or when fewer bytes remain; the caller decodes the rest with checks.
    /// </summary>
    private void DecodeCompleteChunks(ReadOnlySpan<byte> buffer, ref int position, int consumed, Span<byte> row, Span<uint> row32, Span<uint> index, uint opaque, ref uint pixel, ref int column)
    {
        var width = Width;
        var channels = row32.IsEmpty ? 3 : 4;
        var limit = buffer.Length - QoiFormat.MaxChunkLength;
        ref var source = ref unsafe(MemoryMarshal.GetReference(buffer));
        ref var entries = ref unsafe(MemoryMarshal.GetReference(index));
        ref var destination = ref unsafe(MemoryMarshal.GetReference(row));
        var i = position;
        var x = column;
        var current = pixel;
        while (x < width && i <= limit)
        {
            uint tag = unsafe(Unsafe.Add(ref source, i));
            if (tag < QoiFormat.OpLuma)
            {
                if (tag < QoiFormat.OpDiff)
                {
                    // QOI_OP_INDEX
                    current = unsafe(Unsafe.Add(ref entries, (int)tag));
                }
                else
                {
                    // QOI_OP_DIFF: 2-bit differences with a bias of 2, wrapping around 256
                    var dr = (int)((tag >> 4) & 3) - 2;
                    var dg = (int)((tag >> 2) & 3) - 2;
                    var db = (int)(tag & 3) - 2;
                    current = AddBytes(current, (uint)(dr & 0xFF) | ((uint)(dg & 0xFF) << 8) | ((uint)(db & 0xFF) << 16));
                }

                i++;
            }
            else if (tag < QoiFormat.OpRun)
            {
                // QOI_OP_LUMA: green difference with a bias of 32; red and blue differences relative to it, bias 8
                uint second = unsafe(Unsafe.Add(ref source, i + 1));
                var dg = (int)(tag & 0x3F) - 32;
                var dr = dg - 8 + (int)(second >> 4);
                var db = dg - 8 + (int)(second & 0x0F);
                current = AddBytes(current, (uint)(dr & 0xFF) | ((uint)(dg & 0xFF) << 8) | ((uint)(db & 0xFF) << 16));
                i += 2;
            }
            else if (tag < QoiFormat.OpRgb)
            {
                // QOI_OP_RUN: 1-62 repetitions of the previous pixel; the part past this row is carried over
                var run = (int)(tag & 0x3F) + 1;
                var left = ((long)(Height - _y) * width) - x;
                if (run > left)
                    throw RunPastEnd(run, left, _position + (i - consumed));

                unsafe { Unsafe.Add(ref entries, QoiFormat.Hash(current)) = current; }
                i++;
                var count = Math.Min(run, width - x);
                Fill(row, row32, channels, x, count, current | opaque);
                x += count;
                _run = run - count;
                continue;
            }
            else
            {
                // QOI_OP_RGB keeps the alpha of the previous pixel; QOI_OP_RGBA replaces it
                var value = unsafe(Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref source, i + 1)));
                if (!BitConverter.IsLittleEndian)
                {
                    value = BinaryPrimitives.ReverseEndianness(value);
                }

                if (tag == QoiFormat.OpRgb)
                {
                    current = (current & 0xFF000000) | (value & 0x00FFFFFF);
                    i += 4;
                }
                else
                {
                    current = value;
                    i += 5;
                }
            }

            unsafe { Unsafe.Add(ref entries, QoiFormat.Hash(current)) = current; }
            if (channels == 4)
            {
                unsafe { Unsafe.WriteUnaligned(ref Unsafe.Add(ref destination, x * 4), current | opaque); }
            }
            else
            {
                ref var target = ref unsafe(Unsafe.Add(ref destination, x * 3));
                target = (byte)current;
                unsafe
                {
                    Unsafe.Add(ref target, 1) = (byte)(current >> 8);
                    Unsafe.Add(ref target, 2) = (byte)(current >> 16);
                }
            }

            x++;
        }

        position = i;
        column = x;
        pixel = current;
    }

    // Adds the bytes of delta to the bytes of value, each modulo 256: the low 7 bits of the bytes add without carrying into
    // the next byte, and the top bits are added (xored) apart
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint AddBytes(uint value, uint delta) => ((value & 0x7F7F7F7F) + (delta & 0x7F7F7F7F)) ^ ((value ^ delta) & 0x80808080);

    private static void Fill(Span<byte> row, Span<uint> row32, int channels, int x, int count, uint pixel)
    {
        if (channels == 4)
        {
            row32.Slice(x, count).Fill(pixel);
            return;
        }

        var destination = row.Slice(x * 3, count * 3);
        var r = (byte)pixel;
        var g = (byte)(pixel >> 8);
        var b = (byte)(pixel >> 16);
        for (var offset = 0; offset < destination.Length; offset += 3)
        {
            destination[offset] = r;
            destination[offset + 1] = g;
            destination[offset + 2] = b;
        }
    }

    private ImageInfo CreateInfo(ImageIdentifyMode mode)
    {
        var hasAlpha = Channels == 4;
        return new ImageInfo(
            ImageFormat.Qoi,
            Size,
            DefaultPixelFormat,
            hasAlpha ? ImageColorModel.Rgba : ImageColorModel.Rgb,
            bitsPerComponent: 8,
            frameCount: 1,
            isAnimated: false,
            hasPosterFrame: false,
            mayHaveTransparency: hasAlpha,
            animation: null,
            _metadata.Metadata,
            mode);
    }

    private static InvalidImageContentException RunPastEnd(long run, long left, long position)
        => Invalid(string.Create(CultureInfo.InvariantCulture, $"The QOI run of {run} pixels at offset {position} extends past the last pixel ({left} pixels are left)."));

    private static InvalidImageContentException Invalid(string message) => new(message, ImageFormat.Qoi);
}
