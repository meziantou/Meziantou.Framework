using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads one strip or tile of a TIFF page into rows of exactly the size the geometry requires: it undoes the fill order,
/// the compression and the horizontal differencing predictor, and converts the data units of a YCbCr page to RGB.
/// </summary>
/// <remarks>
/// <para>
/// The decompressed size of a block is always known before the block is read (the number of rows times the padded row
/// length), so every decompressor fills a fixed buffer and can never expand without bound: a block that produces fewer
/// bytes is reported as truncated, and the bytes it produces beyond the buffer are not read. The compressed length itself
/// comes from <c>StripByteCounts</c>/<c>TileByteCounts</c> and is validated against the length of the input first.
/// </para>
/// <para>
/// A block compressed with JPEG is a JPEG datastream of its own, decoded by the JPEG decoder of the library with the
/// tables of <c>JPEGTables</c>. The photometric interpretation of the page says what its components are: YCbCr components
/// are converted to RGB as in a JFIF file, the others are the stored samples. The frame of the datastream has the
/// precision the page declares; its samples of more than 8 bits are received scaled to 16 bits and kept, like the other
/// 16-bit samples of a block, in the byte order of the file.
/// </para>
/// </remarks>
internal sealed class TiffBlockReader : IDisposable
{
    private const byte JpegMarkerSoi = 0xD8;
    private const byte JpegMarkerEoi = 0xD9;
    private const byte JpegMarkerDht = 0xC4;
    private const byte JpegMarkerDqt = 0xDB;
    private const byte JpegMarkerDri = 0xDD;

    private const int MaxCompressionExpansion = 16;
    private const int MaxBlockOverhead = 4096;
    private const int MaxJpegOverhead = 1024 * 1024;

#if NET11_0_OR_GREATER
    // The size of the reads of DeflateStream from its source (the length of its rented buffer)
    private const int InflateStreamPiece = 8192;
#endif

    private static readonly byte[] ReversedBits = CreateReversedBits();

    private readonly RandomAccessSource _source;
    private readonly TiffPage _page;
    private readonly JpegBlockReceiver? _jpegReceiver;
    private readonly Jpeg2000BlockReceiver? _jpeg2000Receiver;
    private PooledBuffer? _block;
    private PooledBuffer? _units;
    private PooledBuffer? _changes;
    private PooledBuffer? _jpegRow;
    private TiffLzwDecoder? _lzw;
#if NET11_0_OR_GREATER
    private ZLibDecoder? _zlib;
#endif

    public TiffBlockReader(RandomAccessSource source, TiffPage page)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(page);
        _source = source;
        _page = page;
        if (page.IsJpeg)
        {
            _jpegReceiver = new JpegBlockReceiver(page);
        }
        else if (page.IsJpeg2000)
        {
            _jpeg2000Receiver = new Jpeg2000BlockReceiver(page);
        }
    }

    /// <summary>Reads and decodes one block into the buffer of the reader.</summary>
    /// <param name="index">The index of the block in <see cref="TiffPage.BlockOffsets"/>.</param>
    /// <param name="rowCount">The number of rows of the block that belong to the page (the last blocks of a page hold fewer).</param>
    /// <returns><paramref name="rowCount"/> rows of <see cref="TiffPage.BlockRowLength"/> bytes, valid until the next call.</returns>
    /// <exception cref="InvalidImageContentException">The block is outside the input, shorter than its geometry requires, or its compressed data is malformed.</exception>
    public ReadOnlySpan<byte> ReadBlock(int index, int rowCount)
    {
        var page = _page;
        _block ??= _source.Scope.Rent(page.BlockRowLength * page.BlockHeight, AllocationKind.DecoderState, clear: false);
        var destination = _block.Span[..(rowCount * page.BlockRowLength)];
        if (page.IsJpeg)
        {
            DecodeJpeg(index, rowCount, _block.RawBuffer, handler: null);
            return destination;
        }

        if (page.IsJpeg2000)
        {
            DecodeJpeg2000(index, rowCount, _block.RawBuffer);
            return destination;
        }

        // A tile is always stored in full, even when it extends past the bottom edge of the page; a strip is not
        var storedRows = page.IsTiled ? page.BlockHeight : rowCount;
        var conversion = page.YCbCr;
        var stored = conversion is null
            ? _block.Span[..(storedRows * page.BlockRowLength)]
            : RentUnits(conversion.GetStoredLength(page.BlockWidth, storedRows));
        Decompress(index, storedRows, stored);
        if (page.Predictor == TiffFormat.PredictorHorizontalDifferencing)
        {
            UndoHorizontalDifferencing(stored, storedRows);
        }

        conversion?.Convert(stored, destination, page.BlockWidth, rowCount);
        return destination;
    }

    /// <summary>
    /// Decodes one strip compressed with JPEG and hands its rows over as they are decoded, so that a page stored as one
    /// strip is never held twice.
    /// </summary>
    /// <param name="index">The index of the strip.</param>
    /// <param name="rowCount">The number of rows of the strip.</param>
    /// <param name="handler">The destination of the rows.</param>
    public void ReadJpegStrip(int index, int rowCount, TiffBlockRowHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        DecodeJpeg(index, rowCount, buffer: null, handler);
    }

    public void Dispose()
    {
        _block?.Dispose();
        _block = null;
#if NET11_0_OR_GREATER
        _zlib?.Dispose();
        _zlib = null;
#endif
        _units?.Dispose();
        _units = null;
        _changes?.Dispose();
        _changes = null;
        _jpegRow?.Dispose();
        _jpegRow = null;
        _lzw?.Dispose();
        _lzw = null;
    }

    private static byte[] CreateReversedBits()
    {
        var table = new byte[256];
        for (var i = 0; i < table.Length; i++)
        {
            var value = 0;
            for (var bit = 0; bit < 8; bit++)
            {
                value |= ((i >> bit) & 1) << (7 - bit);
            }

            table[i] = (byte)value;
        }

        return table;
    }

    private Span<byte> RentUnits(long length)
    {
        if (length > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(_source.Limits);

        if (_units is null || _units.Length < length)
        {
            _units?.Dispose();
            _units = null;
            _units = _source.Scope.Rent((int)length, AllocationKind.DecoderState, clear: false);
        }

        return _units.Span[..(int)length];
    }

    private (long Offset, long Length) GetStoredRange(int index)
    {
        var offset = _page.BlockOffsets[index];
        var storedLength = _page.BlockByteCounts[index];
        if (offset > long.MaxValue || storedLength > long.MaxValue)
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {_page.Index} stores the block {index} at offset {offset} with {storedLength} bytes, outside the input."));

        return ((long)offset, (long)storedLength);
    }

    /// <summary>
    /// Reads the compressed bytes of a block, and no more of them than a block of its size can need. Every decompressor
    /// stops when the rows of the block are complete, so the bytes beyond that bound would never be looked at: not reading
    /// them keeps the work of a block proportional to its size, whatever <c>StripByteCounts</c> declares and however many
    /// blocks point at the same bytes.
    /// </summary>
    /// <param name="index">The index of the block.</param>
    /// <param name="decodedLength">The number of bytes the block decompresses to.</param>
    private PooledBuffer ReadCompressed(int index, long decodedLength)
    {
        var (offset, storedLength) = GetStoredRange(index);
        if (storedLength == 0)
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {_page.Index} declares an empty compressed block {index}."));

        // The largest expansions are far below 16 times: about 6 for fax rows of isolated pixels (6 bits per pixel, and an
        // end-of-line code per row), 1.5 for LZW and 2 for PackBits data that never repeats, about 6.5 for a JPEG block
        // whose every coefficient has the longest code. A JPEG datastream also carries tables and application segments
        var needed = (decodedLength * MaxCompressionExpansion) + (_page.IsJpeg || _page.IsJpeg2000 ? MaxJpegOverhead : MaxBlockOverhead);
        var compressed = _source.ReadToBuffer(offset, Math.Min(storedLength, needed));
        if (_page.IsLeastSignificantBitFirst)
        {
            ReverseBits(compressed.Span);
        }

        return compressed;
    }

    private static void ReverseBits(Span<byte> data)
    {
        var table = ReversedBits;
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = table[data[i]];
        }
    }

    /// <summary>Fills <paramref name="destination"/> with the stored samples of a block.</summary>
    private void Decompress(int index, int rowCount, Span<byte> destination)
    {
        var page = _page;
        if (page.Compression == TiffFormat.CompressionNone)
        {
            var (offset, storedLength) = GetStoredRange(index);
            if (storedLength < destination.Length)
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {page.Index} declares {storedLength} bytes for the uncompressed block {index}; its geometry needs {destination.Length}."));

            _source.Read(offset, destination);
            if (page.IsLeastSignificantBitFirst)
            {
                ReverseBits(destination);
            }

            return;
        }

        using var compressed = ReadCompressed(index, destination.Length);
        if (page.IsDeflate)
        {
            Inflate(compressed, destination, index);
            return;
        }

        if (page.IsCcitt)
        {
            DecodeFax(compressed.Span, destination, rowCount, index);
            return;
        }

        int written;
        if (page.Compression == TiffFormat.CompressionLzw)
        {
            if (TiffLzwDecoder.UsesOldBitOrder(compressed.Span))
                throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The LZW data of the TIFF block {index} of page {page.Index} uses the bit order of TIFF 5.0 drafts (least significant bit first), which is not supported."), "Compression: LZW with the old bit order");

            _lzw ??= new TiffLzwDecoder(_source.Scope);
            if (!_lzw.TryDecode(compressed.Span, destination, out written))
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The LZW data of the TIFF block {index} of page {page.Index} is malformed: a code refers to a string that was not defined."));
        }
        else
        {
            written = TiffPackBits.Decode(compressed.Span, destination);
        }

        if (written != destination.Length)
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The {TiffFormat.GetCompressionName(page.Compression)} data of the TIFF block {index} of page {page.Index} produced {written} bytes; {destination.Length} are required."));
    }

    private void Inflate(PooledBuffer compressed, Span<byte> destination, int index)
    {
#if NET11_0_OR_GREATER
        if (TryInflate(compressed.Span, destination))
            return;

        // The stream path defines the errors: it reads the block again and reports how it fails
#endif
        InflateStream(compressed, destination, index);
    }

    private void InflateStream(PooledBuffer compressed, Span<byte> destination, int index)
    {
        using var input = new MemoryStream(compressed.RawBuffer, 0, compressed.Length, writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        try
        {
            var written = 0;
            while (written < destination.Length)
            {
                var read = zlib.Read(destination[written..]);
                if (read <= 0)
                    throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The Deflate data of the TIFF block {index} of page {_page.Index} produced {written} bytes; {destination.Length} are required."));

                written += read;
            }
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidImageContentException(string.Create(CultureInfo.InvariantCulture, $"The Deflate data of the TIFF block {index} of page {_page.Index} is malformed."), ImageFormat.Tiff, exception);
        }
    }

#if NET11_0_OR_GREATER
    /// <summary>
    /// Decompresses a block with a decoder reused by every block of the page, fed with the pieces of
    /// <see cref="InflateStreamPiece"/> bytes in which <see cref="InflateStream"/> reads its source: the decoder sees the
    /// same input at every step, so it fills the block exactly when the stream path does.
    /// </summary>
    /// <returns><see langword="false"/> when the block is malformed or too short (the stream path then reports it).</returns>
    private bool TryInflate(ReadOnlySpan<byte> compressed, Span<byte> destination)
    {
        var decoder = _zlib ??= new ZLibDecoder();
        decoder.Reset();
        var written = 0;
        var position = 0;
        var pieceEnd = Math.Min(compressed.Length, InflateStreamPiece);
        while (written < destination.Length)
        {
            var status = decoder.Decompress(compressed[position..pieceEnd], destination[written..], out var consumed, out var produced);
            position += consumed;
            written += produced;
            switch (status)
            {
                case OperationStatus.Done:
                    return written == destination.Length;

                case OperationStatus.NeedMoreData:
                    if (pieceEnd == compressed.Length)
                        return false;

                    pieceEnd = Math.Min(compressed.Length, pieceEnd + InflateStreamPiece);
                    break;

                case OperationStatus.DestinationTooSmall:
                    break;

                default:
                    return false;
            }
        }

        return true;
    }
#endif

    private void DecodeFax(ReadOnlySpan<byte> compressed, Span<byte> destination, int rowCount, int index)
    {
        var page = _page;
        var listLength = TiffCcittDecoder.GetChangeListLength(page.BlockWidth);
        if (listLength * 2 * sizeof(int) > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(_source.Limits);

        _changes ??= _source.Scope.Rent((int)(listLength * 2 * sizeof(int)), AllocationKind.DecoderState, clear: false);
        var lists = unsafe(MemoryMarshal.Cast<byte, int>(_changes.Span));
        var error = TiffCcittDecoder.Decode(compressed, destination, page.BlockWidth, rowCount, page.Compression, page.IsTwoDimensionalFax, lists[..(int)listLength], lists.Slice((int)listLength, (int)listLength));
        if (error is null)
            return;

        if (error == TiffCcittDecoder.UncompressedModeError)
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The {TiffFormat.GetCompressionName(page.Compression)} data of the TIFF block {index} of page {page.Index} uses the uncompressed mode extension, which is not supported."), "Compression: CCITT uncompressed mode");

        throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The {TiffFormat.GetCompressionName(page.Compression)} data of the TIFF block {index} of page {page.Index} is malformed: {error}."));
    }

    /// <summary>Undoes the horizontal differencing predictor row by row, over the stored (padded) width of the block.</summary>
    private void UndoHorizontalDifferencing(Span<byte> block, int rowCount)
    {
        var rowLength = _page.BlockRowLength;
        var samplesPerPixel = _page.SamplesPerPixel;
        var is8Bit = _page.BitsPerSample == 8;
        var bigEndian = _page.IsBigEndian;
        for (var row = 0; row < rowCount; row++)
        {
            var line = block.Slice(row * rowLength, rowLength);
            if (is8Bit)
            {
                // Each sample adds the sample of the same channel on its left: the PNG Sub filter with one pixel of samples
                PngFilters.Unfilter(PngFilters.Sub, line, line, samplesPerPixel);
            }
            else
            {
                UndoDifferencing16(line, samplesPerPixel, bigEndian);
            }
        }
    }

    /// <summary>
    /// Undoes the horizontal differencing of one row of 16-bit samples, stored in the byte order of the file: each sample
    /// adds the sample of the same channel on its left, modulo 2^16.
    /// </summary>
    /// <remarks>
    /// With 128-bit hardware vectors, pixels of one to four samples are reconstructed one pixel per step in 16-bit lanes
    /// (the bytes of big-endian samples are swapped around the addition); the bytes after the last whole vector go through
    /// <see cref="UndoDifferencing16Scalar(Span{byte}, int, bool)"/>, the reference.
    /// </remarks>
    internal static void UndoDifferencing16(Span<byte> line, int samplesPerPixel, bool bigEndian)
    {
        var start = 0;
        var pixelBytes = samplesPerPixel * sizeof(ushort);
        if (Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian && samplesPerPixel is >= 1 and <= 4 && line.Length >= Vector128<byte>.Count)
        {
            var swap = Vector128.Create((byte)1, 0, 3, 2, 5, 4, 7, 6, 9, 8, 11, 10, 13, 12, 15, 14);
            var left = Vector128<ushort>.Zero;
            var last = line.Length - Vector128<byte>.Count;
            for (; start <= last; start += pixelBytes)
            {
                var samples = Vector128.Create(line[start..]);
                if (bigEndian)
                {
                    samples = Vector128.Shuffle(samples, swap);
                }

                left += samples.AsUInt16();
                var pixel = bigEndian ? Vector128.Shuffle(left.AsByte(), swap) : left.AsByte();
                var target = line[start..];
                switch (pixelBytes)
                {
                    case 2:
                        BinaryPrimitives.WriteUInt16LittleEndian(target, pixel.AsUInt16().ToScalar());
                        break;
                    case 4:
                        BinaryPrimitives.WriteUInt32LittleEndian(target, pixel.AsUInt32().ToScalar());
                        break;
                    case 6:
                        BinaryPrimitives.WriteUInt32LittleEndian(target, pixel.AsUInt32().ToScalar());
                        BinaryPrimitives.WriteUInt16LittleEndian(target[4..], pixel.AsUInt16().GetElement(2));
                        break;
                    default:
                        BinaryPrimitives.WriteUInt64LittleEndian(target, pixel.AsUInt64().ToScalar());
                        break;
                }
            }
        }

        UndoDifferencing16Scalar(line, samplesPerPixel, bigEndian, start / sizeof(ushort));
    }

    /// <summary>The sample-at-a-time reference of <see cref="UndoDifferencing16"/>.</summary>
    internal static void UndoDifferencing16Scalar(Span<byte> line, int samplesPerPixel, bool bigEndian)
        => UndoDifferencing16Scalar(line, samplesPerPixel, bigEndian, start: 0);

    /// <summary>Reconstructs the samples of a row from <paramref name="start"/> (a pixel boundary); the samples before it are already reconstructed.</summary>
    private static void UndoDifferencing16Scalar(Span<byte> line, int samplesPerPixel, bool bigEndian, int start)
    {
        var count = line.Length / 2;
        for (var i = Math.Max(start, samplesPerPixel); i < count; i++)
        {
            var offset = i * 2;
            var previousOffset = (i - samplesPerPixel) * 2;
            if (bigEndian)
            {
                BinaryPrimitives.WriteUInt16BigEndian(line[offset..], (ushort)(BinaryPrimitives.ReadUInt16BigEndian(line[offset..]) + BinaryPrimitives.ReadUInt16BigEndian(line[previousOffset..])));
            }
            else
            {
                BinaryPrimitives.WriteUInt16LittleEndian(line[offset..], (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(line[offset..]) + BinaryPrimitives.ReadUInt16LittleEndian(line[previousOffset..])));
            }
        }
    }

    /// <summary>Decodes the JPEG datastream of a block into a buffer, or row by row into a handler.</summary>
    private void DecodeJpeg(int index, int rowCount, byte[]? buffer, TiffBlockRowHandler? handler)
    {
        var page = _page;
        var receiver = _jpegReceiver!;
        using var compressed = ReadCompressed(index, (long)rowCount * page.BlockRowLength);
        if (handler is not null && page.DecodedBitsPerSample == 16)
        {
            // The rows of 16-bit samples are handed over in the byte order of the file, like the ones of a buffer
            _jpegRow ??= _source.Scope.Rent(page.BlockRowLength, AllocationKind.DecoderState, clear: false);
        }

        receiver.Begin(index, rowCount, buffer, handler, _jpegRow?.RawBuffer);
        try
        {
            using var decoder = new JpegDecoder(_source.Context, receiver, page.IsYCbCr ? ImageColorModel.YCbCr : ImageColorModel.Rgb);
            if (page.JpegTables is { } tables)
            {
                LoadJpegTables(decoder, tables);
            }

            using var parser = new JpegStructureParser(_source.Context, StructureWalk.Decode, decoder);
            if (!parser.Parse(compressed.Span, isEndOfInput: true, out _).IsComplete)
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG data of the TIFF block {index} of page {page.Index} is truncated."));
        }
        catch (InvalidImageContentException exception) when (exception.Format == ImageFormat.Jpeg)
        {
            throw new InvalidImageContentException(string.Create(CultureInfo.InvariantCulture, $"The JPEG data of the TIFF block {index} of page {page.Index} is malformed: {exception.Message}"), ImageFormat.Tiff, exception);
        }
        catch (UnsupportedImageFeatureException exception) when (exception.Format == ImageFormat.Jpeg)
        {
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The JPEG data of the TIFF block {index} of page {page.Index} is not supported: {exception.Message}"), string.Create(CultureInfo.InvariantCulture, $"Compression: {exception.Feature}"));
        }
        finally
        {
            receiver.End();
        }

        if (receiver.RowsReceived < rowCount)
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG data of the TIFF block {index} of page {page.Index} produced {receiver.RowsReceived} rows; {rowCount} are required."));
    }

    /// <summary>
    /// Decodes the JPEG 2000 codestream of a block into a buffer. The codestream may be alone or in a JP2 file, whose boxes
    /// are not interpreted: the page says what the components are.
    /// </summary>
    private void DecodeJpeg2000(int index, int rowCount, byte[] buffer)
    {
        var page = _page;
        var receiver = _jpeg2000Receiver!;
        using var compressed = ReadCompressed(index, (long)rowCount * page.BlockRowLength);
        receiver.Begin(index, rowCount, buffer);
        try
        {
            using var decoder = new Jpeg2000SampleDecoder(_source.Context, receiver);
            using var parser = new Jpeg2000StructureParser(_source.Context, StructureWalk.Decode, decoder, isEmbedded: true);
            if (!parser.Parse(compressed.Span, isEndOfInput: true, out _).IsComplete)
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 data of the TIFF block {index} of page {page.Index} is truncated."));
        }
        catch (InvalidImageContentException exception) when (exception.Format == ImageFormat.Jpeg2000)
        {
            throw new InvalidImageContentException(string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 data of the TIFF block {index} of page {page.Index} is malformed: {exception.Message}"), ImageFormat.Tiff, exception);
        }
        catch (UnsupportedImageFeatureException exception) when (exception.Format == ImageFormat.Jpeg2000)
        {
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 data of the TIFF block {index} of page {page.Index} is not supported: {exception.Message}"), string.Create(CultureInfo.InvariantCulture, $"Compression: {exception.Feature}"));
        }
        finally
        {
            receiver.End();
        }

        if (receiver.RowsReceived < rowCount)
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 data of the TIFF block {index} of page {page.Index} produced {receiver.RowsReceived} rows; {rowCount} are required."));
    }

    /// <summary>
    /// Gives the tables of <c>JPEGTables</c> to a decoder: an abbreviated datastream for table specifications (SOI, the
    /// quantization and Huffman tables, EOI) that the datastream of every block of the page relies on (TIFF Technical Note 2).
    /// </summary>
    private void LoadJpegTables(JpegDecoder decoder, ReadOnlySpan<byte> tables)
    {
        if (tables.Length < 4 || tables[0] != 0xFF || tables[1] != JpegMarkerSoi)
            throw InvalidTables();

        var position = 2;
        while (true)
        {
            if (position + 2 > tables.Length || tables[position] != 0xFF)
                throw InvalidTables();

            var marker = tables[position + 1];
            if (marker == 0xFF)
            {
                // Fill byte before a marker
                position++;
                continue;
            }

            if (marker == JpegMarkerEoi)
                return;

            if (position + 4 > tables.Length)
                throw InvalidTables();

            var length = (tables[position + 2] << 8) | tables[position + 3];
            if (length < 2 || position + 2 + length > tables.Length)
                throw InvalidTables();

            var payload = tables.Slice(position + 4, length - 2);
            if (marker == JpegMarkerDri && payload.Length != 2)
                throw InvalidTables();

            if (marker is JpegMarkerDqt or JpegMarkerDht or JpegMarkerDri)
            {
                decoder.OnSegment(marker, payload);
            }

            position += 2 + length;
        }

        InvalidImageContentException InvalidTables()
            => TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEGTables tag of the TIFF page {_page.Index} is not a JPEG datastream of table specifications."));
    }

    /// <summary>Receives the samples of the JPEG 2000 codestream of one block and checks its components against the page.</summary>
    private sealed class Jpeg2000BlockReceiver : Jpeg2000SampleReceiver
    {
        private readonly TiffPage _page;
        private byte[]? _buffer;
        private int _index;
        private int _rowCount;
        private long _samplesReceived;

        public Jpeg2000BlockReceiver(TiffPage page)
        {
            _page = page;
        }

        /// <summary>Gets the number of rows of the block that were received in full.</summary>
        public int RowsReceived => (int)(_samplesReceived / _page.BlockWidth);

        public void Begin(int index, int rowCount, byte[] buffer)
        {
            _index = index;
            _rowCount = rowCount;
            _buffer = buffer;
            _samplesReceived = 0;
        }

        public void End() => _buffer = null;

        public override void OnImage(int width, int height, ReadOnlySpan<Jpeg2000ComponentSize> components)
        {
            var page = _page;
            if (components.Length != page.SamplesPerPixel)
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 data of the TIFF block {_index} of page {page.Index} has {components.Length} component(s); the page declares {page.SamplesPerPixel} sample(s) per pixel."));

            foreach (var component in components)
            {
                if (component.Precision != page.BitsPerSample)
                    throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 data of the TIFF block {_index} of page {page.Index} has samples of {component.Precision} bits; the page declares {page.BitsPerSample}."));
            }

            // A block holds at least the rows of the page it covers, and never more than the rows of a block
            if (width != page.BlockWidth || height < _rowCount || height > page.BlockHeight)
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 data of the TIFF block {_index} of page {page.Index} is a {width}x{height} image; the block is {page.BlockWidth} pixels wide, holds {_rowCount} rows and has at most {page.BlockHeight}."));
        }

        public override void OnRow(int x, int y, ReadOnlySpan<byte> samples)
        {
            if (y >= _rowCount)
                return;

            var page = _page;
            var bytesPerPixel = page.BlockRowLength / page.BlockWidth;
            var destination = _buffer.AsSpan((y * page.BlockRowLength) + (x * bytesPerPixel), samples.Length);
            if (page.BitsPerSample == 16 && page.IsBigEndian == BitConverter.IsLittleEndian)
            {
                // The samples of a block are in the byte order of the file
                for (var i = 0; i < samples.Length; i += 2)
                {
                    destination[i] = samples[i + 1];
                    destination[i + 1] = samples[i];
                }
            }
            else
            {
                samples.CopyTo(destination);
            }

            _samplesReceived += samples.Length / bytesPerPixel;
        }
    }

    /// <summary>Receives the rows of the JPEG datastream of one block and checks its frame against the page.</summary>
    private sealed class JpegBlockReceiver : JpegSampleReceiver
    {
        private readonly TiffPage _page;
        private readonly int _componentCount;
        private byte[]? _buffer;
        private TiffBlockRowHandler? _handler;
        private byte[]? _row;
        private int _index;
        private int _rowCount;

        public JpegBlockReceiver(TiffPage page)
        {
            _page = page;
            _componentCount = page.SamplesPerPixel;
        }

        public int RowsReceived { get; private set; }

        /// <param name="index">The index of the block.</param>
        /// <param name="rowCount">The number of rows of the block that belong to the page.</param>
        /// <param name="buffer">The destination of the rows, or <see langword="null"/> when a handler receives them.</param>
        /// <param name="handler">The destination of the rows, or <see langword="null"/> when a buffer receives them.</param>
        /// <param name="row">The row a handler is given 16-bit samples in, or <see langword="null"/>.</param>
        public void Begin(int index, int rowCount, byte[]? buffer, TiffBlockRowHandler? handler, byte[]? row)
        {
            _index = index;
            _rowCount = rowCount;
            _buffer = buffer;
            _handler = handler;
            _row = row;
            RowsReceived = 0;
        }

        public void End()
        {
            _buffer = null;
            _handler = null;
            _row = null;
        }

        public override void OnFrame(int width, int height, int componentCount, int precision)
        {
            if (componentCount != _componentCount)
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG data of the TIFF block {_index} of page {_page.Index} has {componentCount} component(s); the page declares {_componentCount} sample(s) per pixel."));

            if (precision != _page.BitsPerSample)
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG data of the TIFF block {_index} of page {_page.Index} has samples of {precision} bits; the page declares {_page.BitsPerSample}."));

            // A block holds at least the rows of the page it covers, and never more than the rows of a block: some writers
            // give the last strip the height of the others, and a taller frame would be decoded to be thrown away
            if (width != _page.BlockWidth || height < _rowCount || height > _page.BlockHeight)
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG data of the TIFF block {_index} of page {_page.Index} is a {width}x{height} frame; the block is {_page.BlockWidth} pixels wide, holds {_rowCount} rows and has at most {_page.BlockHeight}."));
        }

        public override void OnRow(int y, ReadOnlySpan<byte> samples)
        {
            if (y >= _rowCount)
                return;

            if (_handler is not null)
            {
                _handler.OnRow(y, samples);
            }
            else
            {
                samples.CopyTo(_buffer.AsSpan(y * _page.BlockRowLength));
            }

            RowsReceived++;
        }

        public override void OnDeepRow(int y, ReadOnlySpan<ushort> samples)
        {
            if (y >= _rowCount)
                return;

            // The samples of a block are in the byte order of the file
            var destination = _handler is not null ? _row.AsSpan(0, samples.Length * sizeof(ushort)) : _buffer.AsSpan(y * _page.BlockRowLength, samples.Length * sizeof(ushort));
            if (_page.IsBigEndian)
            {
                SampleEndianness.WriteBigEndian(samples, destination);
            }
            else
            {
                SampleEndianness.WriteLittleEndian(samples, destination);
            }

            _handler?.OnRow(y, destination);
            RowsReceived++;
        }
    }
}
