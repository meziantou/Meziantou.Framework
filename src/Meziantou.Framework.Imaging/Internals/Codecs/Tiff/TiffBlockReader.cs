using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads one strip or tile of a TIFF page into rows of exactly the size the geometry requires: it undoes the fill order,
/// the compression and the predictors, converts the data units of a YCbCr page to RGB, and signed samples to unsigned
/// ones.
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
    private readonly Workspace _workspace;
    private PooledBuffer? _jpegRow;

    public TiffBlockReader(RandomAccessSource source, TiffPage page)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(page);
        _source = source;
        _page = page;
        _workspace = new Workspace(source.Scope);
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
        if (TryGetStoredRows(index, rowCount, out var rows))
            return rows;

        if (page.IsJpeg || page.IsJpeg2000)
        {
            var buffer = _workspace.GetBlock(page);
            if (page.IsJpeg)
            {
                DecodeJpeg(index, rowCount, buffer.RawBuffer, handler: null);
            }
            else
            {
                DecodeJpeg2000(index, rowCount, buffer.RawBuffer);
            }

            return buffer.Span[..(rowCount * page.BlockRowLength)];
        }

        var block = Fetch(index, rowCount, _workspace);
        try
        {
            Decompress(block, _workspace);
        }
        finally
        {
            // The compressed bytes are released before the predictors rent their row, as they always were
            block.Dispose();
        }

        return Finish(block, _workspace);
    }

    /// <summary>
    /// Gets a value indicating whether the blocks of a page are worth decoding by several workers: the LZW, Deflate and
    /// CCITT blocks. Uncompressed and PackBits blocks cost about what copying them costs (1 and 2.4 ms for a 3840x2160 RGB
    /// page, against 70 to 100 ms for LZW and Deflate), which handing them to workers does not save. The JPEG and JPEG 2000
    /// datastreams are decoded on the calling thread by the decoders of those formats.
    /// </summary>
    public static bool CanDecodeInParallel(TiffPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return page.IsDeflate || page.IsCcitt || page.Compression == TiffFormat.CompressionLzw;
    }

    /// <summary>
    /// Gets the number of bytes charged for the working memory a worker needs to decode any block of a page: what
    /// <see cref="Workspace.Prepare"/> rents, and the copy of the largest compressed block.
    /// </summary>
    public static long GetWorkerBytes(TiffPage page, AllocationScope scope)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(scope);

        // A buffer that cannot be rented makes the decoding of the first block fail: it is left to one worker
        var unitsLength = page.YCbCr?.GetStoredLength(page.BlockWidth, page.BlockHeight) ?? 0;
        var changesLength = page.IsCcitt ? TiffCcittDecoder.GetChangeListLength(page.BlockWidth) * 2 * sizeof(int) : 0;
        if (unitsLength > CheckedSizes.MaxBufferLength || changesLength > CheckedSizes.MaxBufferLength)
            return long.MaxValue;

        var bytes = scope.GetChargedSize((long)page.BlockRowLength * page.BlockHeight);
        if (unitsLength > 0)
        {
            bytes += scope.GetChargedSize(unitsLength);
        }

        if (changesLength > 0)
        {
            bytes += scope.GetChargedSize(changesLength);
        }

        if (page.Predictor == TiffFormat.PredictorFloatingPoint)
        {
            bytes += scope.GetChargedSize(page.BlockRowLength);
        }

        if (page.Compression == TiffFormat.CompressionLzw)
        {
            bytes += scope.GetChargedSize(TiffLzwDecoder.StateLength);
        }

        if (page.Compression != TiffFormat.CompressionNone)
        {
            // The compressed bytes are copied when the input is a stream, when their bits are reversed, and for the Deflate
            // decompressor of .NET 10: counted whenever the block is compressed
            var decodedLength = page.YCbCr is { } units ? units.GetStoredLength(page.BlockWidth, page.BlockHeight) : (long)page.BlockRowLength * page.BlockHeight;
            var largest = 0UL;
            foreach (var count in page.BlockByteCounts)
            {
                largest = Math.Max(largest, count);
            }

            var copyLength = (long)Math.Min(largest, (ulong)GetCompressedReadLimit(page, decodedLength));
            if (copyLength > CheckedSizes.MaxBufferLength)
                return long.MaxValue;

            bytes += scope.GetChargedSize(copyLength);
        }

        return bytes;
    }

    /// <summary>
    /// Reads what the decoding of a block needs from the input, on the calling thread: the rows of an uncompressed block,
    /// or the compressed bytes (charged to the encoded-byte limit, and copied when they are not in memory). The rest of
    /// the work (<see cref="Decompress(in FetchedBlock, Workspace)"/> and <see cref="Finish"/>) reads nothing else from
    /// the input and charges nothing.
    /// </summary>
    /// <param name="index">The index of the block.</param>
    /// <param name="rowCount">The number of rows of the block that belong to the page.</param>
    /// <param name="workspace">The working memory the block is decoded with.</param>
    /// <returns>The block, which the caller disposes.</returns>
    public FetchedBlock Fetch(int index, int rowCount, Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var page = _page;
        _ = workspace.GetBlock(page);

        // A tile is always stored in full, even when it extends past the bottom edge of the page; a strip is not
        var storedRows = page.IsTiled ? page.BlockHeight : rowCount;
        var stored = GetStored(storedRows, workspace);
        if (page.Compression == TiffFormat.CompressionNone)
        {
            var (offset, storedLength) = GetStoredRange(index);
            if (storedLength < stored.Length)
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {page.Index} declares {storedLength} bytes for the uncompressed block {index}; its geometry needs {stored.Length}."));

            _source.Read(offset, stored);
            if (page.IsLeastSignificantBitFirst)
            {
                ReverseBits(stored);
            }

            return new FetchedBlock(index, rowCount, storedRows, 0, 0, Copy: null);
        }

        var (compressedOffset, compressedLength) = ReadCompressed(index, stored.Length, out var copy);
        return new FetchedBlock(index, rowCount, storedRows, compressedOffset, compressedLength, copy);
    }

    /// <summary>
    /// Decompresses a fetched block into the working memory: on any thread, with a workspace no other thread uses. A
    /// workspace prepared by <see cref="Workspace.Prepare"/> rents nothing.
    /// </summary>
    public void Decompress(scoped in FetchedBlock block, Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (_page.Compression == TiffFormat.CompressionNone)
            return;

        var compressed = block.Copy is { } copy ? copy.Span : GetCompressedInPlace(block);
        DecompressBlock(block.Index, block.StoredRows, compressed, block.Copy, GetStored(block.StoredRows, workspace), workspace);
    }

    /// <summary>Undoes the predictors and the sample transformations of a decompressed block, like <see cref="Decompress(in FetchedBlock, Workspace)"/>.</summary>
    /// <returns>The rows of the block, valid until the workspace decodes another block.</returns>
    public ReadOnlySpan<byte> Finish(scoped in FetchedBlock block, Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var page = _page;
        var storedRows = block.StoredRows;
        var stored = GetStored(storedRows, workspace);
        if (page.Predictor == TiffFormat.PredictorHorizontalDifferencing)
        {
            UndoHorizontalDifferencing(stored, storedRows);
        }
        else if (page.Predictor == TiffFormat.PredictorFloatingPoint)
        {
            UndoFloatingPointDifferencing(stored, storedRows, workspace);
        }

        if (page.IsSigned)
        {
            ToUnsigned(stored, page.BitsPerSample, page.IsBigEndian);
        }

        var destination = workspace.GetBlock(page).Span[..(block.RowCount * page.BlockRowLength)];
        page.YCbCr?.Convert(stored, destination, page.BlockWidth, block.RowCount);
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

    /// <summary>Gets the working memory of the reader, which <see cref="ReadBlock"/> decodes with.</summary>
    public Workspace OwnWorkspace => _workspace;

    public void Dispose()
    {
        _workspace.Dispose();
        _jpegRow?.Dispose();
        _jpegRow = null;
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

    /// <summary>Gets the largest number of compressed bytes read for a block of <paramref name="decodedLength"/> bytes.</summary>
    private static long GetCompressedReadLimit(TiffPage page, long decodedLength)
    {
        // The largest expansions are far below 16 times: about 6 for fax rows of isolated pixels (6 bits per pixel, and an
        // end-of-line code per row), 1.5 for LZW and 2 for PackBits data that never repeats, about 6.5 for a JPEG block
        // whose every coefficient has the longest code. A JPEG datastream also carries tables and application segments
        return (decodedLength * MaxCompressionExpansion) + (page.IsJpeg || page.IsJpeg2000 ? MaxJpegOverhead : MaxBlockOverhead);
    }

    /// <summary>Gets the buffer the stored samples of a block are decompressed into: the rows of the block, or its YCbCr data units.</summary>
    private Span<byte> GetStored(int storedRows, Workspace workspace)
    {
        var page = _page;
        return page.YCbCr is { } conversion
            ? workspace.GetUnits(conversion.GetStoredLength(page.BlockWidth, storedRows), _source.Limits)
            : workspace.GetBlock(page).Span[..(storedRows * page.BlockRowLength)];
    }

    /// <summary>Gets the compressed bytes of a fetched block that are read where they are, in an input held in memory (already charged by <see cref="Fetch"/>).</summary>
    private ReadOnlySpan<byte> GetCompressedInPlace(in FetchedBlock block)
    {
        if (!_source.TryGetSpan(block.Offset, block.Length, out var compressed, charge: false))
            throw new InvalidOperationException("The compressed bytes of the block are not in memory.");

        return compressed;
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
    /// <param name="copy">
    /// The buffer that holds the bytes, which the caller disposes, or <see langword="null"/> when they are the bytes of an
    /// input held in memory: they are only copied when they must be changed (bit order) or the input is a stream.
    /// </param>
    /// <returns>The range of the compressed bytes in the input (charged to the encoded-byte limit).</returns>
    private (long Offset, int Length) ReadCompressed(int index, long decodedLength, out PooledBuffer? copy)
    {
        var (offset, storedLength) = GetStoredRange(index);
        if (storedLength == 0)
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {_page.Index} declares an empty compressed block {index}."));

        var length = Math.Min(storedLength, GetCompressedReadLimit(_page, decodedLength));
        copy = null;
        if (!_page.IsLeastSignificantBitFirst && CanDecompressInPlace && _source.TryGetSpan(offset, length, out _))
            return (offset, (int)length);

        copy = _source.ReadToBuffer(offset, length);
        if (_page.IsLeastSignificantBitFirst)
        {
            ReverseBits(copy.Span);
        }

        return (offset, (int)length);
    }

    /// <summary>Gets the compressed bytes of a block for the JPEG and JPEG 2000 decoders, which run on the calling thread.</summary>
    private ReadOnlySpan<byte> ReadCompressedBytes(int index, long decodedLength, out PooledBuffer? copy)
    {
        var (offset, length) = ReadCompressed(index, decodedLength, out copy);
        if (copy is not null)
            return copy.Span;

        _source.TryGetSpan(offset, length, out var compressed, charge: false);
        return compressed;
    }

#if NET11_0_OR_GREATER
    /// <summary>Gets a value indicating whether the decompressor of the page reads the compressed bytes where they are.</summary>
    private static bool CanDecompressInPlace => true;
#else
    /// <summary>
    /// Gets a value indicating whether the decompressor of the page reads the compressed bytes where they are. The Deflate
    /// decompressor of .NET 10 reads a stream, which is created over a buffer.
    /// </summary>
    private bool CanDecompressInPlace => !_page.IsDeflate;
#endif

    /// <summary>
    /// Gets the rows of an uncompressed block that needs no change straight from an input held in memory, with the checks
    /// of <see cref="Fetch"/>.
    /// </summary>
    /// <returns><see langword="false"/> when the block is read into the buffer of the reader.</returns>
    private bool TryGetStoredRows(int index, int rowCount, out ReadOnlySpan<byte> rows)
    {
        rows = default;
        var page = _page;
        if (page.Compression != TiffFormat.CompressionNone || page.IsJpeg || page.IsJpeg2000 || page.YCbCr is not null || page.Predictor != TiffFormat.PredictorNone || page.IsSigned || page.IsLeastSignificantBitFirst)
            return false;

        // A tile is always stored in full, even when it extends past the bottom edge of the page; a strip is not
        var (offset, storedLength) = GetStoredRange(index);
        var length = (long)(page.IsTiled ? page.BlockHeight : rowCount) * page.BlockRowLength;
        if (storedLength < length || !_source.TryGetSpan(offset, length, out var stored))
            return false; // Decompress reads it, or reports that it is too short

        rows = stored[..(rowCount * page.BlockRowLength)];
        return true;
    }

    private static void ReverseBits(Span<byte> data)
    {
        var table = ReversedBits;
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = table[data[i]];
        }
    }

    /// <summary>Fills <paramref name="destination"/> with the stored samples of a compressed block.</summary>
    private void DecompressBlock(int index, int rowCount, ReadOnlySpan<byte> compressed, PooledBuffer? copy, Span<byte> destination, Workspace workspace)
    {
        var page = _page;
        if (page.IsDeflate)
        {
            Inflate(compressed, copy, destination, index, workspace);
            return;
        }

        if (page.IsCcitt)
        {
            DecodeFax(compressed, destination, rowCount, index, workspace);
            return;
        }

        int written;
        if (page.Compression == TiffFormat.CompressionLzw)
        {
            if (TiffLzwDecoder.UsesOldBitOrder(compressed))
                throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The LZW data of the TIFF block {index} of page {page.Index} uses the bit order of TIFF 5.0 drafts (least significant bit first), which is not supported."), "Compression: LZW with the old bit order");

            if (!workspace.GetLzw().TryDecode(compressed, destination, out written))
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The LZW data of the TIFF block {index} of page {page.Index} is malformed: a code refers to a string that was not defined."));
        }
        else
        {
            written = TiffPackBits.Decode(compressed, destination);
        }

        if (written != destination.Length)
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The {TiffFormat.GetCompressionName(page.Compression)} data of the TIFF block {index} of page {page.Index} produced {written} bytes; {destination.Length} are required."));
    }

    /// <param name="compressed">The compressed bytes.</param>
    /// <param name="copy">The buffer that holds them, or <see langword="null"/> when they are the bytes of the input (.NET 11 only).</param>
    /// <param name="destination">The rows of the block.</param>
    /// <param name="index">The index of the block.</param>
    /// <param name="workspace">The working memory of the decompression.</param>
    private void Inflate(ReadOnlySpan<byte> compressed, PooledBuffer? copy, Span<byte> destination, int index, Workspace workspace)
    {
#if NET11_0_OR_GREATER
        if (TryInflate(compressed, destination, workspace))
            return;

        // The stream path defines the errors: it reads the block again and reports how it fails
#else
        _ = workspace;
#endif
        if (copy is not null)
        {
            using var buffer = new MemoryStream(copy.RawBuffer, 0, copy.Length, writable: false);
            InflateStream(buffer, destination, index);
            return;
        }

        // The stream reads the bytes where they are, pinned until it is done: nothing is rented, whatever thread decodes
        unsafe
        {
            fixed (byte* pointer = compressed)
            {
                using var input = new UnmanagedMemoryStream(pointer, compressed.Length);
                InflateStream(input, destination, index);
            }
        }
    }

    private void InflateStream(Stream input, Span<byte> destination, int index)
    {
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
        catch (IOException exception)
        {
            // The source is in memory: an IOException is the BCL's (internal) ZLibException for zlib error codes that are
            // not data errors, for example a preset dictionary (FDICT), which TIFF does not define (fuzz regression)
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
    private static bool TryInflate(ReadOnlySpan<byte> compressed, Span<byte> destination, Workspace workspace)
    {
        var decoder = workspace.GetZLib();
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

    private void DecodeFax(ReadOnlySpan<byte> compressed, Span<byte> destination, int rowCount, int index, Workspace workspace)
    {
        var page = _page;
        var listLength = TiffCcittDecoder.GetChangeListLength(page.BlockWidth);
        var lists = unsafe(MemoryMarshal.Cast<byte, int>(workspace.GetChangeLists(listLength, _source.Limits).Span));
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
        var samplesPerPixel = _page.BlockSamplesPerPixel;
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

    /// <summary>
    /// Undoes the floating-point predictor of TIFF Technical Note 3 row by row, over the stored (padded) width of the block.
    /// </summary>
    private void UndoFloatingPointDifferencing(Span<byte> block, int rowCount, Workspace workspace)
    {
        var page = _page;
        var rowLength = page.BlockRowLength;
        var scratch = workspace.GetPredictorRow(rowLength).Span[..rowLength];
        for (var row = 0; row < rowCount; row++)
        {
            UndoFloatingPointDifferencing(block.Slice(row * rowLength, rowLength), scratch, page.BlockSamplesPerPixel, page.BitsPerSample / 8, page.IsBigEndian);
        }
    }

    /// <summary>
    /// Undoes the floating-point predictor of one row (TIFF Technical Note 3). The encoder splits the samples of the row
    /// into planes of bytes, the most significant byte of every sample first and the least significant one last whatever
    /// the byte order of the file, then replaces each byte with its difference from the byte <paramref name="samplesPerPixel"/>
    /// positions before it, modulo 256. The bytes are summed back, then the samples are rebuilt in the byte order of the
    /// file, like the other samples of a block.
    /// </summary>
    /// <param name="line">The bytes of the row, replaced by its samples.</param>
    /// <param name="scratch">A buffer of the length of the row.</param>
    /// <param name="samplesPerPixel">The number of samples of a pixel.</param>
    /// <param name="bytesPerSample">The number of bytes of a sample (2, 3 or 4).</param>
    /// <param name="bigEndian">Whether the samples of the file are big-endian.</param>
    internal static void UndoFloatingPointDifferencing(Span<byte> line, Span<byte> scratch, int samplesPerPixel, int bytesPerSample, bool bigEndian)
    {
        // The sum of the byte on the left: the PNG Sub filter with pixels of one byte per sample
        PngFilters.Unfilter(PngFilters.Sub, line, line, samplesPerPixel);
        line.CopyTo(scratch);
        var count = line.Length / bytesPerSample;
        for (var plane = 0; plane < bytesPerSample; plane++)
        {
            var bytes = scratch.Slice(plane * count, count);
            var position = bigEndian ? plane : bytesPerSample - 1 - plane;
            for (var i = 0; i < bytes.Length; i++)
            {
                line[(i * bytesPerSample) + position] = bytes[i];
            }
        }
    }

    /// <summary>
    /// Turns two's complement samples of 8 or 16 bits into the unsigned samples that are larger by half of their range, by
    /// flipping their sign bit: the most significant byte of each sample, in the byte order of the file.
    /// </summary>
    internal static void ToUnsigned(Span<byte> samples, int bitsPerSample, bool bigEndian)
    {
        var step = bitsPerSample / 8;
        for (var i = step == 1 || bigEndian ? 0 : 1; i < samples.Length; i += step)
        {
            samples[i] ^= 0x80;
        }
    }

    /// <summary>Decodes the JPEG datastream of a block into a buffer, or row by row into a handler.</summary>
    private void DecodeJpeg(int index, int rowCount, byte[]? buffer, TiffBlockRowHandler? handler)
    {
        var page = _page;
        var receiver = _jpegReceiver!;
        var compressed = ReadCompressedBytes(index, (long)rowCount * page.BlockRowLength, out var copy);
        using var owner = copy;
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
            if (!parser.Parse(compressed, isEndOfInput: true, out _).IsComplete)
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
        var compressed = ReadCompressedBytes(index, (long)rowCount * page.BlockRowLength, out var copy);
        using var owner = copy;
        receiver.Begin(index, rowCount, buffer);
        try
        {
            using var decoder = new Jpeg2000SampleDecoder(_source.Context, receiver);
            using var parser = new Jpeg2000StructureParser(_source.Context, StructureWalk.Decode, decoder, isEmbedded: true);
            if (!parser.Parse(compressed, isEndOfInput: true, out _).IsComplete)
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

    /// <summary>
    /// A block whose input <see cref="Fetch"/> read: its rows are in the workspace when it is not compressed, else its
    /// compressed bytes are at <see cref="Offset"/> in an input held in memory, or in <see cref="Copy"/>.
    /// </summary>
    /// <param name="Index">The index of the block.</param>
    /// <param name="RowCount">The number of rows of the block that belong to the page.</param>
    /// <param name="StoredRows">The number of rows the block stores (a tile is stored in full).</param>
    /// <param name="Offset">The offset of the compressed bytes in the input.</param>
    /// <param name="Length">The number of compressed bytes read.</param>
    /// <param name="Copy">The compressed bytes, when they are copied; disposed with the block.</param>
    public readonly record struct FetchedBlock(int Index, int RowCount, int StoredRows, long Offset, int Length, PooledBuffer? Copy) : IDisposable
    {
        public void Dispose() => Copy?.Dispose();
    }

    /// <summary>
    /// The working memory of the decoding of one block at a time: the rows of the block, its YCbCr data units, the change
    /// lists of fax rows, the row of the floating-point predictor, and the LZW and Deflate decoders. The reader rents it as
    /// its blocks need it; each worker of a page has one, which <see cref="Prepare"/> rents whole on the calling thread.
    /// </summary>
    public sealed class Workspace : IDisposable
    {
        private readonly AllocationScope _scope;
        private PooledBuffer? _block;
        private PooledBuffer? _units;
        private PooledBuffer? _changes;
        private PooledBuffer? _predictorRow;
        private TiffLzwDecoder? _lzw;
#if NET11_0_OR_GREATER
        private ZLibDecoder? _zlib;
#endif

        public Workspace(AllocationScope scope)
        {
            ArgumentNullException.ThrowIfNull(scope);
            _scope = scope;
        }

        /// <summary>
        /// Rents what the decoding of any block of a page needs (<see cref="GetWorkerBytes"/> bytes at most, the copy of the
        /// compressed bytes excluded), so that decoding a block rents nothing.
        /// </summary>
        public void Prepare(TiffPage page, ImageResourceLimits limits)
        {
            ArgumentNullException.ThrowIfNull(page);
            ArgumentNullException.ThrowIfNull(limits);
            _ = GetBlock(page);
            if (page.YCbCr is { } conversion)
            {
                _ = GetUnits(conversion.GetStoredLength(page.BlockWidth, page.BlockHeight), limits);
            }

            if (page.IsCcitt)
            {
                _ = GetChangeLists(TiffCcittDecoder.GetChangeListLength(page.BlockWidth), limits);
            }

            if (page.Predictor == TiffFormat.PredictorFloatingPoint)
            {
                _ = GetPredictorRow(page.BlockRowLength);
            }

            if (page.Compression == TiffFormat.CompressionLzw)
            {
                _ = GetLzw();
            }

#if NET11_0_OR_GREATER
            if (page.IsDeflate)
            {
                _ = GetZLib();
            }
#endif
        }

        /// <summary>Gets the rows of a block.</summary>
        public PooledBuffer GetBlock(TiffPage page) => _block ??= _scope.Rent(page.BlockRowLength * page.BlockHeight, AllocationKind.DecoderState, clear: false);

        /// <summary>Gets the YCbCr data units of a block.</summary>
        public Span<byte> GetUnits(long length, ImageResourceLimits limits)
        {
            if (length > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(limits);

            if (_units is null || _units.Length < length)
            {
                _units?.Dispose();
                _units = null;
                _units = _scope.Rent((int)length, AllocationKind.DecoderState, clear: false);
            }

            return _units.Span[..(int)length];
        }

        /// <summary>Gets the two change lists of fax rows, of <paramref name="listLength"/> entries each.</summary>
        public PooledBuffer GetChangeLists(long listLength, ImageResourceLimits limits)
        {
            if (listLength * 2 * sizeof(int) > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(limits);

            return _changes ??= _scope.Rent((int)(listLength * 2 * sizeof(int)), AllocationKind.DecoderState, clear: false);
        }

        /// <summary>Gets the scratch row of the floating-point predictor.</summary>
        public PooledBuffer GetPredictorRow(int rowLength) => _predictorRow ??= _scope.Rent(rowLength, AllocationKind.DecoderState, clear: false);

        /// <summary>Gets the LZW decoder.</summary>
        public TiffLzwDecoder GetLzw() => _lzw ??= new TiffLzwDecoder(_scope);

#if NET11_0_OR_GREATER
        /// <summary>Gets the Deflate decoder, reused by every block.</summary>
        public ZLibDecoder GetZLib() => _zlib ??= new ZLibDecoder();
#endif

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
            _predictorRow?.Dispose();
            _predictorRow = null;
            _lzw?.Dispose();
            _lzw = null;
        }
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
            if (components.Length != page.BlockSamplesPerPixel)
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 data of the TIFF block {_index} of page {page.Index} has {components.Length} component(s); a block of the page holds {page.BlockSamplesPerPixel} sample(s) per pixel."));

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
            _componentCount = page.BlockSamplesPerPixel;
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
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG data of the TIFF block {_index} of page {_page.Index} has {componentCount} component(s); a block of the page holds {_componentCount} sample(s) per pixel."));

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
