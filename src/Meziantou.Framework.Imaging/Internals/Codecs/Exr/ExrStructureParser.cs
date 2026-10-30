using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Incremental reader of single-part OpenEXR files (OpenEXR File Layout): the magic number and the version field, the
/// header attributes, the offset table, then the chunks, in the three walks of the codec infrastructure.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>Header walk: reads the header and stops before the offset table.</description></item>
/// <item><description>Full scan: reads the offset table and walks every chunk (its coordinates and its size), without decompressing.</description></item>
/// <item><description>
/// Decode: the chunks of the full-resolution image are given to the <see cref="ExrDecoder"/> whole, in the order of the
/// file; the chunks of the other levels of a tiled image are walked and skipped.
/// </description></item>
/// </list>
/// <para>
/// The input is read forward only: the offsets of the table are sorted, and the chunks are read in that order. A chunk
/// must start at its offset, carry the coordinates of its entry of the table, and end before the next chunk starts; bytes
/// between two chunks are skipped, and bytes after the last one are not read. Every chunk is read, so a truncated file
/// is an error.
/// </para>
/// <para>
/// When workers decode the chunks (<see cref="ExrDecoder.BatchChunkCount"/>), the decoder collects them in a batch: the parser
/// has the batch decoded before it asks for more input and before it reports an error, so that the errors are those of
/// one worker, and its requests read ahead the chunks of the next batch (<see cref="NeedChunkBytes"/>).
/// </para>
/// <para>
/// A writer that stops before it patches the offset table leaves entries that point before the end of the table (zeros,
/// usually). Leniently, such a table is rebuilt the way OpenEXR does: the chunks follow the table without a gap, scan
/// lines in the order of the <c>lineOrder</c> attribute (increasing for random order) and tiles in any order, each
/// identified by its coordinates. The entries that are valid must give the offsets of their chunks, and a missing or
/// repeated chunk is still an error.
/// </para>
/// </remarks>
internal sealed class ExrStructureParser : ImageParser<ImageInfo>
{
    // A name, its null byte, a type, its null byte and the 4-byte size
    private const int MaxAttributeHeaderLength = (2 * (ExrFormat.LongNameMaxLength + 1)) + 4;

    private readonly ImageCodecContext _context;
    private readonly StructureWalk _walk;
    private readonly ExrDecoder? _decoder;
    private readonly DecodedMetadataBuilder _metadata;

    private State _state;
    private long _position;
    private long _skip;
    private long _tableEntries;
    private long _cursor;
    private long _chargedAttribute = -1;
    private long _tableEnd;
    private bool _isRebuildingTable;
    private PooledBuffer? _offsets;
    private PooledBuffer? _order;
    private ImageInfo? _result;

    public ExrStructureParser(ImageCodecContext context, StructureWalk walk, ExrDecoder? decoder = null)
        : base(ImageFormat.OpenExr)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (walk == StructureWalk.Decode)
        {
            ArgumentNullException.ThrowIfNull(decoder);
        }

        _context = context;
        _walk = walk;
        _decoder = walk == StructureWalk.Decode ? decoder : null;
        _metadata = new DecodedMetadataBuilder(context.Tracker, ImageFormat.OpenExr);
    }

    private enum State
    {
        Preamble,
        Attributes,
        SkipAttribute,
        OffsetTable,
        Chunks,
        SkipChunk,
        Done,
    }

    /// <summary>Gets the header, once it is read.</summary>
    public ExrHeader Header { get; private set; } = null!;

    /// <summary>Gets the size of the image: its display window.</summary>
    public Size Size { get; private set; }

    public ImageMetadata Metadata => _metadata.Metadata;

    /// <summary>Gets the default working representation: <see cref="PixelFormat.RgbaHalf"/> when every channel is HALF and there is no alpha, otherwise <see cref="PixelFormat.RgbaFloat"/>.</summary>
    public PixelFormat DefaultPixelFormat => DefaultPixelFormats.ForOpenExr(Header.UsesHalfPixels);

    public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
    {
        try
        {
            return ParseCore(buffer, isEndOfInput, out consumed);
        }
        catch (Exception) when (_decoder is { HasPendingChunks: true })
        {
            // The chunks before the error are decoded first: an error of theirs comes first, as with one worker
            _decoder.DecodeBatch();
            throw;
        }
    }

    public override ImageInfo GetResult() => _result ?? throw new InvalidOperationException("The OpenEXR data was not parsed.");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _offsets?.Dispose();
            _offsets = null;
            _order?.Dispose();
            _order = null;
        }

        base.Dispose(disposing);
    }

    private ParseStatus ParseCore(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
    {
        consumed = 0;
        while (true)
        {
            var remaining = buffer[consumed..];
            switch (_state)
            {
                case State.Preamble:
                {
                    if (remaining.Length < ExrFormat.PreambleLength)
                        return ParseStatus.NeedMoreData(ExrFormat.PreambleLength);

                    ReadPreamble(remaining);
                    Advance(ref consumed, ExrFormat.PreambleLength);
                    _state = State.Attributes;
                    break;
                }

                case State.Attributes:
                {
                    if (remaining.IsEmpty)
                        return ParseStatus.NeedMoreData(1);

                    if (remaining[0] == 0)
                    {
                        // The null byte that ends the header
                        Advance(ref consumed, 1);
                        if (OnHeaderEnd())
                            return ParseStatus.Complete;

                        break;
                    }

                    var status = ReadAttribute(remaining, ref consumed);
                    if (!status.IsComplete)
                        return status;

                    break;
                }

                case State.SkipAttribute or State.SkipChunk:
                {
                    var count = (int)Math.Min(_skip, remaining.Length);
                    Advance(ref consumed, count);
                    _skip -= count;
                    if (_skip > 0)
                        return _state == State.SkipAttribute ? ParseStatus.NeedMoreData(1) : NeedChunkBytes(1, isEndOfInput);

                    _state = _state == State.SkipAttribute ? State.Attributes : State.Chunks;
                    break;
                }

                case State.OffsetTable:
                {
                    if (remaining.Length < 8)
                        return ParseStatus.NeedMoreData(8);

                    ReadOffsets(remaining, ref consumed);
                    if (_tableEntries < Header.ChunkCount)
                        return ParseStatus.NeedMoreData(8);

                    SortOffsets();
                    _state = State.Chunks;
                    break;
                }

                case State.Chunks:
                {
                    if (_cursor == Header.ChunkCount)
                    {
                        _state = State.Done;
                        _result = CreateInfo(ImageIdentifyMode.FullScan);
                        _decoder?.OnEnd(this);
                        return ParseStatus.Complete;
                    }

                    var status = ReadChunk(remaining, isEndOfInput, ref consumed);
                    if (!status.IsComplete)
                        return status;

                    break;
                }

                default:
                    return ParseStatus.Complete;
            }
        }
    }

    private void Advance(ref int consumed, int count)
    {
        consumed += count;
        _position += count;
    }

    private void ReadPreamble(ReadOnlySpan<byte> preamble)
    {
        if (!preamble.StartsWith(ExrFormat.Magic))
            throw ExrFormat.Invalid("the magic number 20000630 is missing.");

        var version = BinaryPrimitives.ReadInt32LittleEndian(preamble[4..]);
        if ((version & 0xFF) != ExrFormat.Version)
            throw ExrFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The OpenEXR file has the format version {version & 0xFF}; only version 2 is supported."), "Format version");

        if ((version & ExrFormat.MultipartFlag) != 0)
            throw ExrFormat.Unsupported("The OpenEXR file has several parts, which is not supported.", "Multi-part file");

        if ((version & ExrFormat.NonImageFlag) != 0)
            throw ExrFormat.Unsupported("The OpenEXR image holds deep data, which is not supported.", "Deep data");

        if ((version & ~0xFF & ~ExrFormat.KnownFlags) != 0)
            throw ExrFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The OpenEXR version field 0x{version:X8} has flags that are not defined."), "Version flags");

        Header = new ExrHeader(version);
    }

    private ParseStatus ReadAttribute(ReadOnlySpan<byte> remaining, ref int consumed)
    {
        var maxName = Header.MaxNameLength;
        var nameLength = remaining[..Math.Min(remaining.Length, maxName + 1)].IndexOf((byte)0);
        if (nameLength < 0)
        {
            if (remaining.Length > maxName)
                throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"an attribute name is longer than {maxName} bytes."));

            return ParseStatus.NeedMoreData(Math.Min(remaining.Length + 1, MaxAttributeHeaderLength));
        }

        var typeStart = nameLength + 1;
        var typeArea = remaining[typeStart..];
        var typeLength = typeArea[..Math.Min(typeArea.Length, maxName + 1)].IndexOf((byte)0);
        if (typeLength < 0)
        {
            if (typeArea.Length > maxName)
                throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"an attribute type name is longer than {maxName} bytes."));

            return ParseStatus.NeedMoreData(Math.Min(remaining.Length + 1, MaxAttributeHeaderLength));
        }

        if (typeLength == 0)
            throw ExrFormat.Invalid("an attribute has an empty type name.");

        var headerLength = typeStart + typeLength + 1 + 4;
        if (remaining.Length < headerLength)
            return ParseStatus.NeedMoreData(headerLength);

        var name = Encoding.UTF8.GetString(remaining[..nameLength]);
        var type = Encoding.UTF8.GetString(typeArea[..typeLength]);
        var size = BinaryPrimitives.ReadInt32LittleEndian(remaining[(headerLength - 4)..]);
        if (size < 0)
            throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the attribute '{name}' has the size {size}."));

        // Every value counts as metadata, before it is buffered or skipped
        if (_chargedAttribute != _position)
        {
            _metadata.Charge(size);
            _chargedAttribute = _position;
        }

        if (!ExrHeader.IsRead(name))
        {
            // The values the library does not read are skipped as they arrive
            Header.ReadAttribute(name, type, default);
            Advance(ref consumed, headerLength);
            _skip = size;
            _state = State.SkipAttribute;
            return ParseStatus.Complete;
        }

        if (size > int.MaxValue - headerLength)
            throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the attribute '{name}' has the size {size}."));

        if (remaining.Length < headerLength + size)
            return ParseStatus.NeedMoreData(headerLength + size);

        Header.ReadAttribute(name, type, remaining.Slice(headerLength, size));
        Advance(ref consumed, headerLength + size);
        return ParseStatus.Complete;
    }

    /// <returns><see langword="true"/> when the walk stops here (header walk, or a yield to a sequential reader).</returns>
    private bool OnHeaderEnd()
    {
        var header = Header;
        header.Complete(_context.Limits);
        Size = new Size((int)header.DisplayWindow.Width, (int)header.DisplayWindow.Height);
        ExrColorDescription.Apply(header, _metadata.Metadata);
        if (_walk == StructureWalk.Header)
        {
            _result = CreateInfo(ImageIdentifyMode.Header);
            _state = State.Done;
            return true;
        }

        _state = State.OffsetTable;
        PrepareOffsets();
        if (_walk == StructureWalk.FullScan)
        {
            _context.Tracker.ChargeScannedFrame();
            return false;
        }

        _context.Sequential?.OnHeader(CreateInfo(ImageIdentifyMode.Header));
        _decoder!.OnHeaderComplete(this);
        return _context.TryConsumeYield();
    }

    private void PrepareOffsets()
    {
        var count = Header.ChunkCount;
        if (count > CheckedSizes.MaxBufferLength / sizeof(long))
            throw CheckedSizes.CreateOverflowException(_context.Limits);

        _offsets = _context.Scope.Rent((int)count * sizeof(long), AllocationKind.DecoderState, clear: false);
        _order = _context.Scope.Rent((int)count * sizeof(int), AllocationKind.DecoderState, clear: false);
    }

    private Span<long> Offsets => unsafe(MemoryMarshal.Cast<byte, long>(_offsets!.Span));

    private Span<int> Order => unsafe(MemoryMarshal.Cast<byte, int>(_order!.Span));

    private void ReadOffsets(ReadOnlySpan<byte> remaining, ref int consumed)
    {
        var offsets = Offsets;
        var order = Order;
        var entries = (int)Math.Min(remaining.Length / 8, Header.ChunkCount - _tableEntries);
        for (var i = 0; i < entries; i++)
        {
            var offset = BinaryPrimitives.ReadUInt64LittleEndian(remaining[(8 * i)..]);
            var index = (int)_tableEntries;
            if (offset > long.MaxValue && !_context.IsLenient)
                throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the offset of the chunk {index} is {offset}."));

            // A negative offset is before the end of the table: an entry the lenient decoding rebuilds
            offsets[index] = (long)offset;
            order[index] = index;
            _tableEntries++;
        }

        Advance(ref consumed, entries * 8);
    }

    private void SortOffsets()
    {
        _tableEnd = _position;
        if (_context.IsLenient && Offsets.ContainsAnyInRange(long.MinValue, _position - 1))
        {
            // The chunks are found by walking them: the table keeps the entries to check, and the order marks the chunks seen
            _isRebuildingTable = true;
            Order.Clear();
            return;
        }

        var offsets = Offsets;
        offsets.Sort(Order);
        for (var i = 0; i < offsets.Length; i++)
        {
            if (offsets[i] < _position)
                throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the chunk {Order[i]} is at the offset {offsets[i]}, before the end of the offset table ({_position})."));

            if (i > 0 && offsets[i] == offsets[i - 1])
                throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the chunks {Order[i - 1]} and {Order[i]} are at the same offset."));
        }
    }

    private ParseStatus ReadChunk(ReadOnlySpan<byte> remaining, bool isEndOfInput, ref int consumed)
    {
        if (_isRebuildingTable)
            return ReadRebuiltChunk(remaining, isEndOfInput, ref consumed);

        var target = Offsets[(int)_cursor];
        var index = Order[(int)_cursor];
        if (_position < target)
        {
            // Bytes between two chunks
            var gap = (int)Math.Min(target - _position, remaining.Length);
            Advance(ref consumed, gap);
            return _position < target ? NeedChunkBytes(1, isEndOfInput) : ParseStatus.Complete;
        }

        if (_position > target)
            throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the chunk {index} starts at the offset {target}, inside the chunk before it."));

        var header = Header;
        var headerLength = header.IsTiled ? ExrFormat.TileChunkHeaderLength : ExrFormat.ScanLineChunkHeaderLength;
        if (remaining.Length < headerLength)
            return NeedChunkBytes(headerLength, isEndOfInput);

        var expected = header.IsTiled ? GetTileIndex(remaining, index) : GetScanLineIndex(remaining, index);
        if (expected != index)
            throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the chunk at the offset {target} is the chunk {expected}, but the offset table gives it to the chunk {index}."));

        return ReadChunkData(remaining, isEndOfInput, ref consumed, headerLength, index);
    }

    /// <summary>
    /// Reads the next chunk of a file whose offset table is rebuilt: it starts where the one before it ends, and its
    /// coordinates say which chunk it is.
    /// </summary>
    private ParseStatus ReadRebuiltChunk(ReadOnlySpan<byte> remaining, bool isEndOfInput, ref int consumed)
    {
        var header = Header;
        var headerLength = header.IsTiled ? ExrFormat.TileChunkHeaderLength : ExrFormat.ScanLineChunkHeaderLength;
        if (remaining.Length < headerLength)
            return NeedChunkBytes(headerLength, isEndOfInput);

        var position = (int)_cursor;
        var index = (int)(header.IsTiled ? GetTileIndex(remaining, position) : GetScanLineIndex(remaining, position));
        if (!header.IsTiled)
        {
            // OpenEXR rebuilds a table of scan lines from chunks stored in their line order, increasing for a random order
            var expected = header.LineOrder == ExrLineOrder.DecreasingY ? header.ChunkCount - 1 - _cursor : _cursor;
            if (index != expected)
                throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the offset table is incomplete and the chunk at the offset {_position} is the chunk {index}; the chunks after the table must be in their line order, which puts the chunk {expected} there."));
        }
        else if (Order[index] != 0)
        {
            throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the offset table is incomplete and the chunk {index} is stored twice."));
        }

        var declared = Offsets[index];
        if (declared >= _tableEnd && declared != _position)
            throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the offset table is incomplete and gives the offset {declared} to the chunk {index}, which is at the offset {_position}."));

        // The chunk is seen once it is read: a chunk that needs more input is read again from its coordinates
        var status = ReadChunkData(remaining, isEndOfInput, ref consumed, headerLength, index);
        if (status.IsComplete)
        {
            Order[index] = 1;
        }

        return status;
    }

    /// <summary>Reads the size and the data of the chunk whose coordinates start <paramref name="remaining"/>.</summary>
    private ParseStatus ReadChunkData(ReadOnlySpan<byte> remaining, bool isEndOfInput, ref int consumed, int headerLength, int index)
    {
        var header = Header;
        var size = BinaryPrimitives.ReadInt32LittleEndian(remaining[(headerLength - 4)..]);
        if (size < 0 || size > int.MaxValue - headerLength)
            throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the chunk {index} has the size {size}."));

        if (_walk == StructureWalk.Decode && index < header.FirstLevelChunkCount)
        {
            if (remaining.Length < headerLength + size)
                return NeedChunkBytes(headerLength + size, isEndOfInput);

            _decoder!.OnChunk(index, remaining.Slice(headerLength, size));
            Advance(ref consumed, headerLength + size);
            _cursor++;
            return ParseStatus.Complete;
        }

        Advance(ref consumed, headerLength);
        _skip = size;
        _state = State.SkipChunk;
        _cursor++;
        _context.CancellationToken.ThrowIfCancellationRequested();
        return ParseStatus.Complete;
    }

    /// <summary>
    /// Requests input while the chunks are read: <paramref name="required"/> bytes, the request of one worker. When workers
    /// decode the chunks, the batch is decoded first (the input may end or fail: the errors of the chunks before come first),
    /// and the request reads ahead the chunks of the next batch, so that a stream gives them at once: up to the start of the
    /// chunk after them (for the last chunk, its largest uncompressed size is a guess), within the bytes of a batch and the
    /// encoded-byte limit. An input that ends before is read again, and the parser asks for what it needs.
    /// </summary>
    private ParseStatus NeedChunkBytes(int required, bool isEndOfInput)
    {
        if (_decoder is not { BatchChunkCount: > 1 } decoder)
            return ParseStatus.NeedMoreData(required);

        decoder.DecodeBatch();
        if (isEndOfInput)
            return ParseStatus.NeedMoreData(required);

        var header = Header;
        var next = _cursor + decoder.BatchChunkCount;
        var headerLength = header.IsTiled ? ExrFormat.TileChunkHeaderLength : ExrFormat.ScanLineChunkHeaderLength;
        var window = decoder.BatchByteCount + ((long)decoder.BatchChunkCount * headerLength);
        if (!_isRebuildingTable)
        {
            // A rebuilt table has no offsets to read ahead to: the window of a batch is the guess
            var end = next < header.ChunkCount ? Offsets[(int)next] : Offsets[^1] + headerLength + header.MaxChunkDataLength;
            window = Math.Min(end - _position, window);
        }

        window = Math.Min(Math.Min(window, _context.Limits.MaxEncodedBytes - _position), CheckedSizes.MaxBufferLength);
        return ParseStatus.NeedMoreData((int)Math.Max(required, window));
    }

    private long GetScanLineIndex(ReadOnlySpan<byte> chunk, int index)
    {
        var header = Header;
        var y = BinaryPrimitives.ReadInt32LittleEndian(chunk);
        var offset = (long)y - header.DataWindow.MinY;
        if (offset < 0 || offset % header.LinesPerChunk != 0 || offset / header.LinesPerChunk >= header.ChunkCount)
            throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the chunk {index} starts at the line {y}, which does not start a chunk of the data window."));

        return offset / header.LinesPerChunk;
    }

    private long GetTileIndex(ReadOnlySpan<byte> chunk, int index)
    {
        var header = Header;
        var tx = BinaryPrimitives.ReadInt32LittleEndian(chunk);
        var ty = BinaryPrimitives.ReadInt32LittleEndian(chunk[4..]);
        var lx = BinaryPrimitives.ReadInt32LittleEndian(chunk[8..]);
        var ly = BinaryPrimitives.ReadInt32LittleEndian(chunk[12..]);
        var validLevel = header.LevelMode switch
        {
            ExrLevelMode.OneLevel => lx == 0 && ly == 0,
            ExrLevelMode.MipmapLevels => lx == ly && lx >= 0 && lx < header.XLevelCount,
            _ => lx >= 0 && lx < header.XLevelCount && ly >= 0 && ly < header.YLevelCount,
        };

        if (!validLevel || tx < 0 || ty < 0 || tx >= header.GetTileCount(lx, horizontal: true) || ty >= header.GetTileCount(ly, horizontal: false))
            throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the chunk {index} is the tile ({tx}, {ty}) of the level ({lx}, {ly}), which the image does not have."));

        return header.GetFirstTileIndex(lx, ly) + ((long)ty * header.GetTileCount(lx, horizontal: true)) + tx;
    }

    private ImageInfo CreateInfo(ImageIdentifyMode mode)
    {
        var header = Header;
        var colorModel = header.IsLuminance
            ? header.HasAlpha ? ImageColorModel.GrayscaleAlpha : ImageColorModel.Grayscale
            : header.HasAlpha ? ImageColorModel.Rgba : ImageColorModel.Rgb;
        return new ImageInfo(
            ImageFormat.OpenExr,
            Size,
            DefaultPixelFormat,
            colorModel,
            bitsPerComponent: header.IsHalfOnly ? 16 : 32,
            frameCount: 1,
            isAnimated: false,
            hasPosterFrame: false,
            mayHaveTransparency: header.HasAlpha || header.DataWindow != header.DisplayWindow,
            animation: null,
            _metadata.Metadata,
            mode);
    }
}
