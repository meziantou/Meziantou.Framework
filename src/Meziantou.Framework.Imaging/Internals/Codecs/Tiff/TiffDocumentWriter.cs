using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Writes a TIFF or BigTIFF document page by page into an <see cref="ImageOutputBuffer"/>,
/// shared by the single-image encoder session and by <see cref="ImageCollectionWriterCore"/>.
/// </summary>
/// <remarks>
/// <para>
/// Layout per page: the strips (or tiles) first, in top-down order, then the directory that describes them. A directory
/// stores the offset and the stored length of every block, so it can only be written once the blocks are compressed; the offset
/// that points <em>to</em> the directory (the header's first-IFD field, or the previous directory's next-IFD field) is
/// therefore patched afterward, which is why the destination must be seekable.
/// </para>
/// <para>
/// One call to <see cref="EncodeStrip"/> writes one strip, so the memory of an encode is one strip (about
/// <see cref="TiffEncoder.DefaultStripBytes"/> of samples) whatever the size of the page or the number of pages, and the
/// writer can flush between strips. A page compressed with JPEG is one strip, written by the JPEG encoder of the library
/// in as many calls as it needs: its memory is the one of a JPEG file. A tiled page is written one tile per call; the
/// rows of a row of tiles are serialized once into a band, which the tiles of that row are cut from.
/// </para>
/// <para>
/// With several workers (<see cref="ImageConfiguration.MaxDegreeOfParallelism"/>), LZW and Deflate strips and tiles are compressed
/// by batches of one strip per worker, each into an output of its own, and appended in order, one per call: the file and
/// its flushes are those of one worker, and the memory is one strip and its compressed bytes per worker.
/// </para>
/// <para>
/// Nothing is lost silently. The bilevel and palette layouts store a page exactly or reject it, and so does a layout
/// without alpha given a pixel that is not opaque: <see cref="PreparePage"/> reads the whole frame before anything of
/// the page is written.
/// </para>
/// <para>
/// Floating-point pixels are written as 32-bit floating-point samples (<c>SampleFormat</c> 3), which hold every value of
/// <see cref="PixelFormat.RgbaFloat"/> and <see cref="PixelFormat.RgbaHalf"/>. There is no floating-point gray pixel
/// format, so <see cref="PreparePage"/> also reads the frame to write the fewest samples that store it exactly: one gray
/// sample when the three colors of every pixel are the same numbers (and no ICC profile describes RGB colors), and no
/// alpha sample when every pixel is opaque. Deflate and LZW strips use the floating-point predictor of TIFF Technical
/// Note 3, which makes them much smaller.
/// </para>
/// <para>
/// The predictors work on the rows of a block (<see cref="ApplyPredictor"/>): the rows of a strip, or the padded rows of
/// a tile, whose samples are serialized in the byte order of the file first.
/// </para>
/// </remarks>
internal sealed class TiffDocumentWriter : IDisposable
{
    private const ulong WhiteColor = 0xFFFF_FFFF_FFFF;

    private readonly TiffEncoder _encoder;
    private readonly AllocationScope _scope;
    private readonly ImageConfiguration _configuration;
    private readonly int _compression;
    private readonly List<ulong> _blockOffsets = [];
    private readonly List<ulong> _blockByteCounts = [];
    private PooledBuffer? _strip;
    private PooledBuffer? _band;
    private PooledBuffer? _predictorRow;
    private PooledBuffer? _converted;
    private PooledBuffer? _changes;
    private TiffLzwEncoder? _lzw;
    private ImageEncoderSession? _jpeg;
    private bool _jpegFrameWritten;
    private long _jpegStart;
    private PreparedPage? _prepared;
    private long _pendingPointerOffset = -1;
    private long _nextPointerOffset = -1;
    private PageState _page;
    private int _blocksWritten;
    private int _bandRow = -1;
    private StripWorkers? _workers;
    private bool _workersChosen;

    /// <summary>Creates the writer of a document.</summary>
    /// <param name="encoder">The encoder settings.</param>
    /// <param name="scope">The scope charged for the buffers of the writer.</param>
    /// <param name="configuration">The configuration of the operation.</param>
    /// <exception cref="ArgumentException">The compression of the encoder cannot store its pixel layout.</exception>
    public TiffDocumentWriter(TiffEncoder encoder, AllocationScope scope, ImageConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(configuration);
        var isFax = encoder.Compression is TiffCompression.CcittModifiedHuffman or TiffCompression.CcittGroup3 or TiffCompression.CcittGroup4;
        if (isFax && encoder.PixelLayout != TiffPixelLayout.Bilevel)
            throw new ArgumentException($"TiffCompression.{encoder.Compression} codes bilevel data: it requires TiffEncoder.PixelLayout = TiffPixelLayout.Bilevel, but the layout is {encoder.PixelLayout}.", nameof(encoder));

        if (encoder.Compression == TiffCompression.Jpeg && encoder.PixelLayout != TiffPixelLayout.Auto)
            throw new ArgumentException($"TiffCompression.Jpeg stores grayscale or YCbCr samples: it requires TiffEncoder.PixelLayout = TiffPixelLayout.Auto, but the layout is {encoder.PixelLayout}.", nameof(encoder));

        if (encoder.Predictor == TiffPredictor.HorizontalDifferencing)
        {
            if (encoder.Compression is not (TiffCompression.Deflate or TiffCompression.Lzw))
                throw new ArgumentException($"TiffPredictor.HorizontalDifferencing is applied by readers to Deflate and LZW data: it requires TiffCompression.Deflate or TiffCompression.Lzw, but the compression is {encoder.Compression}.", nameof(encoder));

            if (encoder.PixelLayout != TiffPixelLayout.Auto)
                throw new ArgumentException($"TiffPredictor.HorizontalDifferencing predicts 8- and 16-bit samples: it requires TiffEncoder.PixelLayout = TiffPixelLayout.Auto, but the layout is {encoder.PixelLayout}.", nameof(encoder));
        }

        if ((encoder.TileWidth == 0) != (encoder.TileHeight == 0))
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"A tiled page needs both tile sizes, but TiffEncoder.TileWidth is {encoder.TileWidth} and TiffEncoder.TileHeight is {encoder.TileHeight}. Set both, or neither to write strips."), nameof(encoder));

        if (encoder.TileWidth > 0 && encoder.Compression == TiffCompression.Jpeg)
            throw new ArgumentException("TiffCompression.Jpeg writes each page as one strip: it cannot be combined with TiffEncoder.TileWidth and TiffEncoder.TileHeight.", nameof(encoder));

        _encoder = encoder;
        _scope = scope;
        _configuration = configuration;
        _compression = encoder.Compression switch
        {
            TiffCompression.None => TiffFormat.CompressionNone,
            TiffCompression.Lzw => TiffFormat.CompressionLzw,
            TiffCompression.PackBits => TiffFormat.CompressionPackBits,
            TiffCompression.CcittModifiedHuffman => TiffFormat.CompressionCcittRle,
            TiffCompression.CcittGroup3 => TiffFormat.CompressionCcittFax3,
            TiffCompression.CcittGroup4 => TiffFormat.CompressionCcittFax4,
            TiffCompression.Jpeg => TiffFormat.CompressionJpeg,
            _ => TiffFormat.CompressionAdobeDeflate,
        };
    }

    /// <summary>Gets a value indicating whether the current page still has strips or tiles to write.</summary>
    public bool HasPendingBlocks => _blocksWritten < _page.BlockCount;

    /// <summary>Gets the number of pages whose directory was written.</summary>
    public int PageCount { get; private set; }

    private bool IsBigTiff => _encoder.BigTiff;

    private bool IsBigEndian => _encoder.BigEndian;

    /// <summary>Gets a value indicating whether the strips of the current page use the floating-point predictor: floating-point samples compressed with Deflate or LZW.</summary>
    private bool UsesFloatingPointPredictor => _page.Layout.IsFloatingPoint && _compression is TiffFormat.CompressionAdobeDeflate or TiffFormat.CompressionLzw;

    /// <summary>Gets a value indicating whether the strips of the current page use horizontal differencing: integer samples with the predictor of the encoder.</summary>
    private bool UsesHorizontalDifferencing => _encoder.Predictor == TiffPredictor.HorizontalDifferencing && !_page.Layout.IsFloatingPoint;

    private int OffsetSize => IsBigTiff ? 8 : 4;

    /// <summary>Writes the file header. The offset of the first directory is patched when that directory is written.</summary>
    /// <param name="output">The output buffer, positioned at the first byte of the file.</param>
    public void WriteHeader(ImageOutputBuffer output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var length = IsBigTiff ? TiffFormat.BigTiffHeaderLength : TiffFormat.ClassicHeaderLength;
        var span = output.GetSpan(length)[..length];
        span.Clear();
        (IsBigEndian ? TiffFormat.BigEndianMark : TiffFormat.LittleEndianMark).CopyTo(span);
        WriteUInt16(span[2..], IsBigTiff ? TiffFormat.BigTiffMagic : TiffFormat.ClassicMagic);
        if (IsBigTiff)
        {
            WriteUInt16(span[4..], 8);
            WriteUInt16(span[6..], 0);
            _pendingPointerOffset = 8;
        }
        else
        {
            _pendingPointerOffset = 4;
        }

        output.Advance(length);
    }

    /// <summary>
    /// Checks that a frame can be stored by the layout of the encoder, and prepares what the layout needs from the whole
    /// frame (the colors of a palette). Nothing is written: a rejection leaves the document as it is.
    /// </summary>
    /// <param name="frame">The frame of the page.</param>
    /// <param name="pixelFormat">The pixel format of the frame.</param>
    /// <param name="metadata">The metadata plan of the page: the colors of floating-point pixels described by an ICC profile are never written as gray samples.</param>
    /// <exception cref="UnsupportedImageFeatureException">The layout or the compression cannot store the frame exactly.</exception>
    public void PreparePage(ImageFrame frame, PixelFormat pixelFormat, MetadataWritePlan metadata)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(metadata);
        var layout = TiffSampleLayout.Create(_encoder, pixelFormat);
        var size = frame.Size;
        if (layout.IsFloatingPoint)
        {
            layout = TiffSampleLayout.ForFloatingPoint(GetFloatingPointSamplesPerPixel(frame, pixelFormat, allowGray: metadata.IccProfile is null));
        }

        if (_compression == TiffFormat.CompressionJpeg)
        {
            if (PixelFormats.GetBitsPerComponent(pixelFormat) > 8)
                throw TiffFormat.Unsupported($"JPEG compression stores 8-bit samples, so encoding {pixelFormat} pixels would discard precision. Select a lossless TiffCompression, or convert the image explicitly with CloneAs.", "Bit depth reduction");

            if (size.Width > ushort.MaxValue || size.Height > ushort.MaxValue)
                throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"A JPEG datastream stores at most 65,535 pixels per side and a JPEG-compressed page is one datastream; the page is {size.Width}x{size.Height}. Select another TiffCompression."), "Canvas size");
        }

        var prepared = new PreparedPage(frame, pixelFormat, layout);
        // The samples of floating-point pixels were chosen to hold them, alpha included
        var rejectsAlpha = !layout.IsFloatingPoint && PixelFormats.HasAlpha(pixelFormat) && !layout.HasAlpha;
        if (rejectsAlpha || layout.IsBilevel || layout.IsIndexed)
        {
            var colors = layout.IsIndexed ? new Dictionary<ulong, byte>() : null;
            var rowLength = size.Width * PixelFormats.GetBytesPerPixel(pixelFormat);
            using var lease = frame.GetStorage().AcquireLease();
            for (var y = 0; y < size.Height; y++)
            {
                var row = lease.GetRowBytes(y)[..rowLength];
                if (rejectsAlpha && PixelConverter.IndexOfNonOpaque(pixelFormat, row) is >= 0 and var x)
                    throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF layout written for TiffPixelLayout.{_encoder.PixelLayout} and TiffCompression.{_encoder.Compression} has no alpha channel and the pixel ({x}, {y}) is not fully opaque. Keep TiffPixelLayout.Auto with a lossless compression, or flatten the image explicitly."), "Alpha removal");

                if (layout.IsBilevel)
                {
                    ValidateBilevelRow(pixelFormat, row, size.Width, y);
                }
                else if (colors is not null)
                {
                    CollectColors(pixelFormat, row, size.Width, colors);
                }
            }

            if (colors is not null)
            {
                // The palette is sorted so that the output does not depend on where a color first appears
                var palette = colors.Keys.ToArray();
                System.Array.Sort(palette);
                if (PixelFormats.GetBitsPerComponent(pixelFormat) > 8 && IsReadAsEightBitColors(palette))
                    throw TiffFormat.Unsupported("The colors of the image are 16-bit values that are all multiples of 256. Readers take such a color map for 8-bit colors (some software stores an 8-bit color c as 256 c), so the page would not be read back with its colors. Keep TiffPixelLayout.Auto, or convert the image to an 8-bit pixel format explicitly.", "Color map precision");

                for (var i = 0; i < palette.Length; i++)
                {
                    colors[palette[i]] = (byte)i;
                }

                prepared.Palette = palette;
                prepared.PaletteIndexes = colors;
            }
        }

        _prepared = prepared;
    }

    /// <summary>Starts a page. Its strips are written by <see cref="EncodeStrip"/> and its directory by <see cref="CompletePage"/>.</summary>
    /// <param name="frame">The frame of the page.</param>
    /// <param name="size">The page size.</param>
    /// <param name="pixelFormat">The pixel format of the frame.</param>
    /// <param name="metadata">The metadata plan of the page.</param>
    /// <exception cref="UnsupportedImageFeatureException">The layout or the compression cannot store the frame exactly.</exception>
    public void BeginPage(ImageFrame frame, Size size, PixelFormat pixelFormat, MetadataWritePlan metadata)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(metadata);
        if (_prepared is null || !ReferenceEquals(_prepared.Frame, frame) || _prepared.PixelFormat != pixelFormat)
        {
            PreparePage(frame, pixelFormat, metadata);
        }

        var prepared = _prepared!;
        _prepared = null;
        var layout = prepared.Layout;
        var rowLength = layout.GetRowLength(size.Width);
        if (rowLength > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(_scope.Limits);

        var isJpeg = _compression == TiffFormat.CompressionJpeg;
        var isTiled = _encoder.TileWidth > 0;
        int blockWidth;
        int blockHeight;
        long blockRowLength;
        if (isJpeg)
        {
            // One strip holds the page. TIFF Technical Note 2 asks for a strip height that is a multiple of the height of a
            // minimum coded unit, which is also what makes it a multiple of the rows a chroma sample covers
            var unitRows = 8 * JpegEncoderCodec.GetSampling(pixelFormat, _encoder.JpegChromaSubsampling).V;
            blockWidth = size.Width;
            blockHeight = (size.Height + unitRows - 1) / unitRows * unitRows;
            blockRowLength = rowLength;
        }
        else if (isTiled)
        {
            // A tile is stored in full, even where it extends past the page; its width is a multiple of 16 pixels, so its rows
            // start on a byte of the row of the page whatever the sample width
            blockWidth = _encoder.TileWidth;
            blockHeight = _encoder.TileHeight;
            blockRowLength = layout.GetRowLength(blockWidth);
            if (!CheckedSizes.TryMultiply(blockRowLength, blockHeight, out var tileLength) || tileLength > CheckedSizes.MaxBufferLength
                || !CheckedSizes.TryMultiply(rowLength, blockHeight, out var bandLength) || bandLength > CheckedSizes.MaxBufferLength)
            {
                throw CheckedSizes.CreateOverflowException(_scope.Limits);
            }
        }
        else
        {
            blockWidth = size.Width;
            blockHeight = _encoder.RowsPerStrip > 0
                ? Math.Min(_encoder.RowsPerStrip, size.Height)
                : (int)Math.Clamp(TiffEncoder.DefaultStripBytes / Math.Max(1, rowLength), 1, size.Height);
            blockRowLength = rowLength;
            if (!CheckedSizes.TryMultiply(rowLength, blockHeight, out var stripLength) || stripLength > CheckedSizes.MaxBufferLength)
            {
                blockHeight = 1;
            }
        }

        var blocksAcross = (size.Width + blockWidth - 1) / blockWidth;
        var blocksDown = (size.Height + blockHeight - 1) / blockHeight;
        _page = new PageState(frame, size, pixelFormat, layout, metadata, (int)rowLength, isTiled, blockWidth, blockHeight, (int)blockRowLength, blocksAcross, blocksDown, prepared.Palette, prepared.PaletteIndexes);
        _blocksWritten = 0;
        _bandRow = -1;
        _workers?.Dispose();
        _workers = null;
        _workersChosen = false;
        _blockOffsets.Clear();
        _blockByteCounts.Clear();
        if (isJpeg)
        {
            BeginJpeg(frame, size, pixelFormat);
            return;
        }

        EnsureBuffer(ref _strip, (int)blockRowLength * blockHeight);
        if (isTiled)
        {
            EnsureBuffer(ref _band, (int)rowLength * blockHeight);
        }

        if (!layout.IsBilevel && !layout.IsIndexed && !layout.IsFloatingPoint && layout.BitsPerSample == 16)
        {
            EnsureBuffer(ref _converted, (int)rowLength);
        }

        if (UsesFloatingPointPredictor || UsesHorizontalDifferencing)
        {
            EnsureBuffer(ref _predictorRow, (int)blockRowLength);
        }

        if (_compression is TiffFormat.CompressionCcittRle or TiffFormat.CompressionCcittFax3 or TiffFormat.CompressionCcittFax4)
        {
            var listLength = TiffCcittEncoder.GetChangeListLength(blockWidth) * 2 * sizeof(int);
            if (listLength > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(_scope.Limits);

            EnsureBuffer(ref _changes, (int)listLength);
        }
    }

    /// <summary>Writes the next strip of the current page, or the next part of its JPEG datastream.</summary>
    /// <param name="output">The output buffer.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    /// <returns><see langword="true"/> when the last strip of the page was written.</returns>
    public bool EncodeStrip(ImageOutputBuffer output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (_jpeg is not null)
            return EncodeJpeg(output, cancellationToken);

        if (!_workersChosen)
        {
            _workersChosen = true;
            _workers = CreateWorkers(output);
        }

        if (_workers is not null)
            return EncodeStripOfWorkers(output, _workers, cancellationToken);

        int length;
        using (var lease = _page.Frame.GetStorage().AcquireLease())
        {
            length = FillBlock(_blocksWritten, lease, _strip!.Span, cancellationToken);
        }

        var blockRowLength = _page.BlockRowLength;
        var rows = length / blockRowLength;
        var stored = _strip.Span[..length];
        var start = output.TotalBytes;
        _blockOffsets.Add((ulong)start);
        switch (_compression)
        {
            case TiffFormat.CompressionNone:
                output.Write(stored);
                break;

            case TiffFormat.CompressionLzw:
                (_lzw ??= new TiffLzwEncoder(_scope)).Encode(stored, output);
                break;

            case TiffFormat.CompressionPackBits:
                for (var i = 0; i < rows; i++)
                {
                    TiffPackBits.EncodeRow(stored.Slice(i * blockRowLength, blockRowLength), output);
                }

                break;

            case TiffFormat.CompressionCcittRle or TiffFormat.CompressionCcittFax3 or TiffFormat.CompressionCcittFax4:
                var lists = unsafe(MemoryMarshal.Cast<byte, int>(_changes!.Span));
                var listLength = (int)TiffCcittEncoder.GetChangeListLength(_page.BlockWidth);
                TiffCcittEncoder.Encode(stored, _page.BlockWidth, rows, _compression, lists[..listLength], lists.Slice(listLength, listLength), output);
                break;

            default:
                Deflate(stored, output);
                break;
        }

        _blockByteCounts.Add((ulong)(output.TotalBytes - start));
        _blocksWritten++;
        return !HasPendingBlocks;
    }

    /// <summary>Writes the directory of the current page and links it to the previous one.</summary>
    /// <param name="output">The output buffer.</param>
    public void CompletePage(ImageOutputBuffer output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var fields = BuildFields();
        var directoryOffset = output.TotalBytes;
        WriteDirectory(output, fields);
        PatchPointer(output, directoryOffset);
        _page = default;
        PageCount++;
    }

    public void Dispose()
    {
        _workers?.Dispose();
        _workers = null;
        _strip?.Dispose();
        _strip = null;
        _band?.Dispose();
        _band = null;
        _predictorRow?.Dispose();
        _predictorRow = null;
        _converted?.Dispose();
        _converted = null;
        _changes?.Dispose();
        _changes = null;
        _lzw?.Dispose();
        _lzw = null;
        _jpeg?.Dispose();
        _jpeg = null;
        _prepared = null;
        _page = default;
    }

    private static void Deflate(ReadOnlySpan<byte> stored, ImageOutputBuffer output)
    {
        using var destination = new BufferWriterStream(output);
        using var zlib = new ZLibStream(destination, CompressionLevel.Optimal, leaveOpen: true);
        zlib.Write(stored);
    }

    /// <summary>
    /// Gets the largest number of bytes a strip of <paramref name="length"/> bytes is compressed to, when its strips are
    /// compressed by several workers (LZW and Deflate).
    /// </summary>
    /// <remarks>
    /// A Deflate block is stored, or coded with the fixed codes when they are shorter than its own: at most 9 bits per byte
    /// (a literal of 9 bits, or a match of 3 bytes coded with 25 bits at most), and a few bytes per block of at least 16,383
    /// symbols, with the 6 bytes of the zlib wrapper; the bound adds a margin to that. The bit writer of LZW asks for 4,096
    /// bytes of room when it starts.
    /// </remarks>
    private long GetMaxCompressedLength(long length) => _compression == TiffFormat.CompressionLzw
        ? TiffLzwEncoder.GetMaxEncodedLength(length) + 4096
        : length + (length / 8) + (length / 1024) + 1024;

    /// <summary>
    /// Chooses whether the strips or tiles of the current page are compressed by several workers
    /// (<see cref="ImageConfiguration.MaxDegreeOfParallelism"/>): LZW and Deflate blocks of a page of at least
    /// <see cref="RowBands.MinPixelsForParallelism"/> pixels and two blocks, and as many workers as the live bytes the scope
    /// has left can give their memory to, after the growth of the output buffer one worker would cause. Uncompressed,
    /// PackBits and CCITT strips cost about what serializing them costs.
    /// </summary>
    /// <returns>The workers, or <see langword="null"/> for one worker.</returns>
    private StripWorkers? CreateWorkers(ImageOutputBuffer output)
    {
        var page = _page;
        var stripCount = page.BlockCount;
        if (_configuration.MaxDegreeOfParallelism <= 1 || _compression is not (TiffFormat.CompressionLzw or TiffFormat.CompressionAdobeDeflate) || stripCount < 2 || (long)page.Size.Width * page.Size.Height < RowBands.MinPixelsForParallelism)
            return null;

        var stripLength = (long)page.BlockRowLength * page.BlockHeight;
        var compressedLength = GetMaxCompressedLength(stripLength);
        if (compressedLength > CheckedSizes.MaxBufferLength)
            return null;

        // One worker rents the table of LZW with its first strip, and grows the output buffer to hold the bytes written
        // between two flushes (the old and the new buffer are live together while it grows): the other workers need a strip,
        // a table and an output of their own, and the first one an output
        var encoderBytes = _compression == TiffFormat.CompressionLzw ? _scope.GetChargedSize(TiffLzwEncoder.StateLength) : 0;
        var outputBytes = _scope.GetChargedSize(compressedLength);
        var workerBytes = _scope.GetChargedSize(stripLength) + encoderBytes + outputBytes;
        var oneWorkerBytes = (_lzw is null ? encoderBytes : 0) + (3 * _scope.GetChargedSize(output.FlushThreshold + compressedLength));
        var available = _scope.Limit - _scope.LiveBytes - oneWorkerBytes - outputBytes;
        if (available < workerBytes)
            return null;

        var workers = (int)Math.Min(1 + (available / workerBytes), Math.Min(_configuration.MaxDegreeOfParallelism, stripCount));
        var created = new StripWorkers(workers);
        try
        {
            for (var i = 0; i < workers; i++)
            {
                // The first worker uses the strip and the table of the writer
                if (i == 0)
                {
                    created.Strips[i] = _strip!;
                    created.Encoders[i] = _compression == TiffFormat.CompressionLzw ? _lzw ??= new TiffLzwEncoder(_scope) : null;
                }
                else
                {
                    created.Strips[i] = _scope.Rent((int)stripLength, AllocationKind.Temporary, clear: false);
                    created.Encoders[i] = _compression == TiffFormat.CompressionLzw ? new TiffLzwEncoder(_scope) : null;
                }

                var compressed = new ImageOutputBuffer(_scope);
                created.Outputs[i] = compressed;
                _ = compressed.GetSpan((int)compressedLength);
            }
        }
        catch
        {
            created.Dispose();
            throw;
        }

        return created;
    }

    /// <summary>
    /// Writes the next block of a page whose strips or tiles several workers compress: the rows of a batch of one block per
    /// worker are serialized on this thread, the workers compress them, each into an output of its own, and the blocks are
    /// appended in order, one per call, so the file and its flushes are the ones of one worker.
    /// </summary>
    private bool EncodeStripOfWorkers(ImageOutputBuffer output, StripWorkers workers, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (workers.Next == workers.Count)
        {
            var block = _blocksWritten;
            var count = 0;
            using (var lease = _page.Frame.GetStorage().AcquireLease())
            {
                while (count < workers.Strips.Length && block < _page.BlockCount)
                {
                    workers.Lengths[count] = FillBlock(block, lease, workers.Strips[count].Span, cancellationToken);
                    block++;
                    count++;
                }
            }

            workers.Count = 0;
            workers.Next = 0;
            var compression = _compression;
            RowBands.RunParallelInBandOrder(count, worker =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var compressed = workers.Outputs[worker];
                compressed.FlushTo(Stream.Null);
                var stored = workers.Strips[worker].Span[..workers.Lengths[worker]];
                if (compression == TiffFormat.CompressionLzw)
                {
                    workers.Encoders[worker]!.Encode(stored, compressed);
                }
                else
                {
                    Deflate(stored, compressed);
                }
            });

            workers.Count = count;
        }

        var strip = workers.Next++;
        var start = output.TotalBytes;
        _blockOffsets.Add((ulong)start);
        output.Write(workers.Outputs[strip].WrittenMemory.Span);
        _blockByteCounts.Add((ulong)(output.TotalBytes - start));
        _blocksWritten++;
        if (HasPendingBlocks)
            return false;

        // The memory of the workers is released before the directory is written, as one worker's would be
        workers.Dispose();
        _workers = null;
        return true;
    }

    private void EnsureBuffer(ref PooledBuffer? buffer, int length)
    {
        if (buffer is not null && buffer.Length >= length)
            return;

        buffer?.Dispose();
        buffer = null;
        buffer = _scope.Rent(length, AllocationKind.Temporary, clear: false);
    }

    /// <summary>
    /// Reads the color of a pixel as three 16-bit components packed in one number (red in the highest bits): the exact
    /// color of a 16-bit pixel format, and the one an 8-bit sample <c>c</c> stands for (<c>257 c</c>) otherwise. Alpha is
    /// not part of the color: the layouts that read colors reject the pixels that are not opaque.
    /// </summary>
    private static ulong ReadColor(PixelFormat format, ReadOnlySpan<byte> row, int x)
    {
        switch (format)
        {
            case PixelFormat.Gray8:
                return Pack(row[x] * 257u, row[x] * 257u, row[x] * 257u);

            case PixelFormat.Gray16:
                var gray = unsafe(MemoryMarshal.Cast<byte, ushort>(row))[x];
                return Pack(gray, gray, gray);

            case PixelFormat.Rgba64:
                var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(row)).Slice(x * 4, 4);
                return Pack(samples[0], samples[1], samples[2]);

            case PixelFormat.Bgra32:
                return Pack(row[(x * 4) + 2] * 257u, row[(x * 4) + 1] * 257u, row[x * 4] * 257u);

            case PixelFormat.La16:
                return Pack(row[x * 2] * 257u, row[x * 2] * 257u, row[x * 2] * 257u);

            case PixelFormat.La32:
                var level = unsafe(MemoryMarshal.Cast<byte, ushort>(row))[x * 2];
                return Pack(level, level, level);

            case PixelFormat.Rgb48:
                var rgb = unsafe(MemoryMarshal.Cast<byte, ushort>(row)).Slice(x * 3, 3);
                return Pack(rgb[0], rgb[1], rgb[2]);

            default:
                var offset = x * PixelFormats.GetBytesPerPixel(format);
                return Pack(row[offset] * 257u, row[offset + 1] * 257u, row[offset + 2] * 257u);
        }

        static ulong Pack(uint red, uint green, uint blue) => ((ulong)red << 32) | ((ulong)green << 16) | blue;
    }

    /// <summary>
    /// Determines whether a color map that holds these 16-bit colors would be read as 8-bit colors that are not these
    /// ones: its entries are all multiples of 256 (the unused ones are zero), and not all multiples of 257.
    /// </summary>
    private static bool IsReadAsEightBitColors(ReadOnlySpan<ulong> palette)
    {
        var isMultipleOf257 = true;
        foreach (var color in palette)
        {
            for (var shift = 0; shift <= 32; shift += 16)
            {
                var sample = (color >> shift) & 0xFFFF;
                if (sample % 256 != 0)
                    return false;

                isMultipleOf257 &= sample % 257 == 0;
            }
        }

        return !isMultipleOf257;
    }

    /// <summary>Checks that every pixel of a row is exactly black or exactly white: gray levels and colors are never thresholded silently.</summary>
    private static void ValidateBilevelRow(PixelFormat format, ReadOnlySpan<byte> row, int width, int y)
    {
        for (var x = 0; x < width; x++)
        {
            if (ReadColor(format, row, x) is not (0 or WhiteColor))
                throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"A bilevel TIFF page stores black and white pixels only and the pixel ({x}, {y}) is neither. Keep TiffPixelLayout.Auto, or reduce the image to black and white explicitly."), "Bilevel reduction");
        }
    }

    /// <summary>Adds the colors of a row to the colors of the page, which a palette holds exactly or not at all.</summary>
    private static void CollectColors(PixelFormat format, ReadOnlySpan<byte> row, int width, Dictionary<ulong, byte> colors)
    {
        var hasPrevious = false;
        var previous = 0UL;
        for (var x = 0; x < width; x++)
        {
            var color = ReadColor(format, row, x);
            if (hasPrevious && color == previous)
                continue;

            if (colors.TryAdd(color, 0) && colors.Count > 256)
                throw TiffFormat.Unsupported("The image has more than 256 distinct colors, the most the color map of a palette TIFF page can hold, and the encoder never reduces colors. Keep TiffPixelLayout.Auto, or reduce the colors of the image explicitly.", "Color reduction");

            previous = color;
            hasPrevious = true;
        }
    }

    /// <summary>
    /// Reads a frame of floating-point pixels to find the fewest samples that store it exactly: 1 when the three colors of
    /// every pixel have the same bits and every pixel is opaque, 2 when they have the same bits, 3 when every pixel is
    /// opaque (its alpha is exactly 1), and 4 otherwise.
    /// </summary>
    private static int GetFloatingPointSamplesPerPixel(ImageFrame frame, PixelFormat pixelFormat, bool allowGray)
    {
        var size = frame.Size;
        var isGray = allowGray;
        var isOpaque = true;
        using var lease = frame.GetStorage().AcquireLease();
        for (var y = 0; y < size.Height && (isGray || isOpaque); y++)
        {
            var row = lease.GetRowBytes(y)[..(size.Width * PixelFormats.GetBytesPerPixel(pixelFormat))];
            if (pixelFormat == PixelFormat.RgbaHalf)
            {
                var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(row));
                var one = BitConverter.HalfToUInt16Bits(Half.One);
                for (var i = 0; i < samples.Length; i += 4)
                {
                    isGray &= samples[i] == samples[i + 1] && samples[i] == samples[i + 2];
                    isOpaque &= samples[i + 3] == one;
                }
            }
            else
            {
                var samples = unsafe(MemoryMarshal.Cast<byte, uint>(row));
                var one = BitConverter.SingleToUInt32Bits(1f);
                for (var i = 0; i < samples.Length; i += 4)
                {
                    isGray &= samples[i] == samples[i + 1] && samples[i] == samples[i + 2];
                    isOpaque &= samples[i + 3] == one;
                }
            }
        }

        return (isGray ? 1 : 3) + (isOpaque ? 0 : 1);
    }

    /// <summary>
    /// Serializes the strip or tile <paramref name="index"/> of the current page, predictors applied, into
    /// <paramref name="destination"/>: the rows of a strip, or the rows of a tile cut from the band of its row of tiles,
    /// padded with zero samples past the right and bottom edges of the page.
    /// </summary>
    /// <returns>The number of stored bytes of the block: its rows times <see cref="PageState.BlockRowLength"/>.</returns>
    private int FillBlock(int index, scoped in PixelLease lease, Span<byte> destination, CancellationToken cancellationToken)
    {
        var page = _page;
        var blockRow = index / page.BlocksAcross;
        var firstRow = blockRow * page.BlockHeight;
        var rows = Math.Min(page.BlockHeight, page.Size.Height - firstRow);
        var blockRowLength = page.BlockRowLength;
        if (!page.IsTiled)
        {
            for (var i = 0; i < rows; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = destination.Slice(i * blockRowLength, blockRowLength);
                SerializeRow(lease.GetRowBytes(firstRow + i), row);
                ApplyPredictor(row);
            }

            return rows * blockRowLength;
        }

        // The rows of a row of tiles are serialized once, for all its tiles
        var rowLength = page.RowLength;
        var band = _band!.Span;
        if (_bandRow != blockRow)
        {
            for (var i = 0; i < rows; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SerializeRow(lease.GetRowBytes(firstRow + i), band.Slice(i * rowLength, rowLength));
            }

            _bandRow = blockRow;
        }

        // Tile widths are multiples of 16 pixels, so a tile starts on a byte of the band whatever the sample width. A
        // bilevel row is serialized with zero bits past the width of the page
        var bitsPerPixel = (long)page.Layout.SamplesPerPixel * page.Layout.BitsPerSample;
        var firstColumn = (index % page.BlocksAcross) * page.BlockWidth;
        var columns = Math.Min(page.BlockWidth, page.Size.Width - firstColumn);
        var copyLength = (int)(((columns * bitsPerPixel) + 7) / 8);
        var bandOffset = (int)(firstColumn * bitsPerPixel / 8);
        var length = blockRowLength * page.BlockHeight;
        var tile = destination[..length];
        if (columns < page.BlockWidth || rows < page.BlockHeight)
        {
            tile.Clear();
        }

        for (var i = 0; i < rows; i++)
        {
            var row = tile.Slice(i * blockRowLength, blockRowLength);
            band.Slice((i * rowLength) + bandOffset, copyLength).CopyTo(row);
            ApplyPredictor(row);
        }

        // The rows of zero samples below the page stay zero samples with either predictor
        return length;
    }

    /// <summary>
    /// Applies the predictor of the page to one row of a block, whose samples are in the byte order of the file: the
    /// horizontal differencing of TIFF 6.0 (section 14), or the floating-point predictor of TIFF Technical Note 3, which
    /// splits the samples of the row into planes of bytes, the most significant byte of every sample first whatever the
    /// byte order of the file. Either way, each value is then replaced with its difference from the value
    /// <c>SamplesPerPixel</c> positions before it.
    /// </summary>
    private void ApplyPredictor(Span<byte> row)
    {
        var layout = _page.Layout;
        var samplesPerPixel = layout.SamplesPerPixel;
        if (UsesFloatingPointPredictor)
        {
            var scratch = _predictorRow!.Span[..row.Length];
            var count = row.Length / sizeof(float);
            for (var plane = 0; plane < sizeof(float); plane++)
            {
                var position = IsBigEndian ? plane : sizeof(float) - 1 - plane;
                var bytes = scratch.Slice(plane * count, count);
                for (var i = 0; i < count; i++)
                {
                    bytes[i] = row[(i * sizeof(float)) + position];
                }
            }

            // The difference from the byte on the left: the PNG Sub filter with pixels of one byte per sample
            PngFilters.Filter(PngFilters.Sub, scratch, scratch, samplesPerPixel, row);
        }
        else if (UsesHorizontalDifferencing)
        {
            if (layout.BitsPerSample == 8)
            {
                var scratch = _predictorRow!.Span[..row.Length];
                row.CopyTo(scratch);
                PngFilters.Filter(PngFilters.Sub, scratch, scratch, samplesPerPixel, row);
            }
            else
            {
                ApplyDifferencing16(row, samplesPerPixel, IsBigEndian);
            }
        }
    }

    /// <summary>
    /// Applies the horizontal differencing to one row of 16-bit samples in the byte order of the file: each sample becomes
    /// its difference from the sample of the same channel on its left, modulo 2^16 (the inverse of
    /// <see cref="TiffBlockReader.UndoDifferencing16"/>).
    /// </summary>
    internal static void ApplyDifferencing16(Span<byte> row, int samplesPerPixel, bool bigEndian)
    {
        for (var i = (row.Length / sizeof(ushort)) - 1; i >= samplesPerPixel; i--)
        {
            var offset = i * sizeof(ushort);
            var previousOffset = (i - samplesPerPixel) * sizeof(ushort);
            if (bigEndian)
            {
                BinaryPrimitives.WriteUInt16BigEndian(row[offset..], (ushort)(BinaryPrimitives.ReadUInt16BigEndian(row[offset..]) - BinaryPrimitives.ReadUInt16BigEndian(row[previousOffset..])));
            }
            else
            {
                BinaryPrimitives.WriteUInt16LittleEndian(row[offset..], (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(row[offset..]) - BinaryPrimitives.ReadUInt16LittleEndian(row[previousOffset..])));
            }
        }
    }

    /// <summary>Serializes one row of floating-point pixels as 32-bit samples in the byte order of the file.</summary>
    private void SerializeFloatingPointRow(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var width = _page.Size.Width;
        var samplesPerPixel = _page.Layout.SamplesPerPixel;
        var colors = samplesPerPixel <= 2 ? 1 : 3;
        var bigEndian = IsBigEndian;
        var samples = destination;
        var isHalf = _page.PixelFormat == PixelFormat.RgbaHalf;
        var halves = unsafe(MemoryMarshal.Cast<byte, Half>(source));
        var singles = unsafe(MemoryMarshal.Cast<byte, float>(source));
        var position = 0;
        for (var x = 0; x < width; x++)
        {
            for (var c = 0; c < samplesPerPixel; c++)
            {
                // The colors, then the alpha
                var component = c < colors ? c : 3;
                var value = isHalf ? (float)halves[(x * 4) + component] : singles[(x * 4) + component];
                var bits = BitConverter.SingleToUInt32Bits(value);
                if (bigEndian)
                {
                    BinaryPrimitives.WriteUInt32BigEndian(samples[position..], bits);
                }
                else
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(samples[position..], bits);
                }

                position += sizeof(float);
            }
        }
    }

    /// <summary>Converts one frame row to the sample layout of the page and serializes it in the byte order of the file.</summary>
    private void SerializeRow(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var width = _page.Size.Width;
        var layout = _page.Layout;
        if (layout.IsFloatingPoint)
        {
            SerializeFloatingPointRow(source, destination);
            return;
        }

        if (layout.IsBilevel)
        {
            // WhiteIsZero: a set bit is a black pixel, which is also what the CCITT compressions code as a black run
            destination.Clear();
            for (var x = 0; x < width; x++)
            {
                if (ReadColor(_page.PixelFormat, source, x) == 0)
                {
                    destination[x >> 3] |= (byte)(0x80 >> (x & 7));
                }
            }

            return;
        }

        if (layout.IsIndexed)
        {
            var indexes = _page.PaletteIndexes!;
            var previous = ReadColor(_page.PixelFormat, source, 0);
            var index = indexes[previous];
            for (var x = 0; x < width; x++)
            {
                var color = ReadColor(_page.PixelFormat, source, x);
                if (color != previous)
                {
                    previous = color;
                    index = indexes[color];
                }

                destination[x] = index;
            }

            return;
        }

        var sourceLength = width * PixelFormats.GetBytesPerPixel(_page.PixelFormat);
        if (layout.BitsPerSample == 8)
        {
            PixelConverter.ConvertRow(_page.PixelFormat, source[..sourceLength], layout.SourcePixelFormat, destination, background: null, ImageFormat.Tiff);
            return;
        }

        var converted = _converted!.Span[..(width * PixelFormats.GetBytesPerPixel(layout.SourcePixelFormat))];
        PixelConverter.ConvertRow(_page.PixelFormat, source[..sourceLength], layout.SourcePixelFormat, converted, background: null, ImageFormat.Tiff);
        var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(converted));
        if (IsBigEndian)
        {
            SampleEndianness.WriteBigEndian(samples, destination);
        }
        else
        {
            SampleEndianness.WriteLittleEndian(samples, destination);
        }
    }

    /// <summary>
    /// Starts the JPEG datastream of a page: the JPEG encoder of the library writes it as it writes a JPEG file, without
    /// metadata (the directory of the page stores it).
    /// </summary>
    private void BeginJpeg(ImageFrame frame, Size size, PixelFormat pixelFormat)
    {
        var encoder = new JpegEncoder
        {
            Quality = _encoder.JpegQuality,
            ChromaSubsampling = _encoder.JpegChromaSubsampling,
            MetadataHandling = MetadataHandling.Strip,
        };

        var capabilities = ImageOutputCapabilities.ForWriter(new ImageWriterOptions(size) { Encoder = encoder, ExpectedFrameCount = 1, Configuration = _configuration });
        var metadata = MetadataWritePlan.Create(metadata: null, ImageFormat.Jpeg, MetadataHandling.Strip, size, pixelFormat);
        var options = new ImageEncoderSessionOptions(encoder, capabilities, size, pixelFormat, ExpectedFrameCount: 1, metadata, Animation: null, _configuration, _scope);
        _jpeg?.Dispose();
        _jpeg = null;
        _jpeg = JpegEncoderCodec.Instance.CreateSession(options);
        _jpeg.BeginFrame(frame, 0);
        _jpegFrameWritten = false;
        _jpegStart = -1;
    }

    /// <summary>Lets the JPEG encoder write the next part of the datastream of the page: its only strip.</summary>
    private bool EncodeJpeg(ImageOutputBuffer output, CancellationToken cancellationToken)
    {
        var session = _jpeg!;
        session.CancellationToken = cancellationToken;
        if (_jpegStart < 0)
        {
            _jpegStart = output.TotalBytes;
        }

        if (!session.Encode(output))
            return false;

        if (!_jpegFrameWritten)
        {
            // The frame is written: the trailer (the EOI marker) follows
            _jpegFrameWritten = true;
            session.BeginComplete(frameCount: 1);
            return false;
        }

        _blockOffsets.Add((ulong)_jpegStart);
        _blockByteCounts.Add((ulong)(output.TotalBytes - _jpegStart));
        _blocksWritten = _page.BlockCount;
        session.Dispose();
        _jpeg = null;
        return true;
    }

    private List<TiffWriteField> BuildFields()
    {
        var layout = _page.Layout;
        var metadata = _page.Metadata;
        var offsetType = IsBigTiff ? TiffFieldType.Long8 : TiffFieldType.Long;
        var fields = new List<TiffWriteField>(16)
        {
            Scalar(TiffFormat.ImageWidthTag, TiffFieldType.Long, (ulong)_page.Size.Width),
            Scalar(TiffFormat.ImageLengthTag, TiffFieldType.Long, (ulong)_page.Size.Height),
            Repeated(TiffFormat.BitsPerSampleTag, TiffFieldType.Short, (ulong)layout.BitsPerSample, layout.SamplesPerPixel),
            Scalar(TiffFormat.CompressionTag, TiffFieldType.Short, (ulong)_compression),
            Scalar(TiffFormat.PhotometricInterpretationTag, TiffFieldType.Short, (ulong)layout.Photometric),
            Scalar(TiffFormat.SamplesPerPixelTag, TiffFieldType.Short, (ulong)layout.SamplesPerPixel),
            Scalar(TiffFormat.PlanarConfigurationTag, TiffFieldType.Short, TiffFormat.PlanarChunky),
            Repeated(TiffFormat.SampleFormatTag, TiffFieldType.Short, (ulong)layout.SampleFormat, layout.SamplesPerPixel),
        };

        if (_page.IsTiled)
        {
            fields.Add(Scalar(TiffFormat.TileWidthTag, TiffFieldType.Long, (ulong)_page.BlockWidth));
            fields.Add(Scalar(TiffFormat.TileLengthTag, TiffFieldType.Long, (ulong)_page.BlockHeight));
            fields.Add(Array(TiffFormat.TileOffsetsTag, offsetType, _blockOffsets));
            fields.Add(Array(TiffFormat.TileByteCountsTag, offsetType, _blockByteCounts));
        }
        else
        {
            fields.Add(Array(TiffFormat.StripOffsetsTag, offsetType, _blockOffsets));
            fields.Add(Scalar(TiffFormat.RowsPerStripTag, TiffFieldType.Long, (ulong)_page.BlockHeight));
            fields.Add(Array(TiffFormat.StripByteCountsTag, offsetType, _blockByteCounts));
        }

        if (UsesFloatingPointPredictor)
        {
            fields.Add(Scalar(TiffFormat.PredictorTag, TiffFieldType.Short, TiffFormat.PredictorFloatingPoint));
        }
        else if (UsesHorizontalDifferencing)
        {
            fields.Add(Scalar(TiffFormat.PredictorTag, TiffFieldType.Short, TiffFormat.PredictorHorizontalDifferencing));
        }

        if (metadata.Orientation != ExifOrientation.TopLeft)
        {
            fields.Add(Scalar(TiffFormat.OrientationTag, TiffFieldType.Short, (ulong)metadata.Orientation));
        }

        if (metadata.Resolution is { } resolution)
        {
            var (x, y) = ResolutionConversion.ToTiffRational(resolution);
            fields.Add(Rationals(TiffFormat.XResolutionTag, [x], ResolutionConversion.TiffResolutionDenominator));
            fields.Add(Rationals(TiffFormat.YResolutionTag, [y], ResolutionConversion.TiffResolutionDenominator));
            fields.Add(Scalar(TiffFormat.ResolutionUnitTag, TiffFieldType.Short, TiffFormat.ResolutionUnitInch));
        }

        if (layout.HasAlpha)
        {
            fields.Add(Scalar(TiffFormat.ExtraSamplesTag, TiffFieldType.Short, TiffFormat.ExtraSampleUnassociatedAlpha));
        }

        if (_page.Palette is { } palette)
        {
            // Three values for each of the 256 indexes: the red values, then the green and the blue ones
            var map = new List<ulong>(new ulong[3 * 256]);
            for (var i = 0; i < palette.Length; i++)
            {
                map[i] = (palette[i] >> 32) & 0xFFFF;
                map[256 + i] = (palette[i] >> 16) & 0xFFFF;
                map[512 + i] = palette[i] & 0xFFFF;
            }

            fields.Add(Array(TiffFormat.ColorMapTag, TiffFieldType.Short, map));
        }

        if (layout.Photometric == TiffFormat.PhotometricYCbCr)
        {
            // The samples of a JFIF datastream: full range, chroma centered on 128, one chroma sample for h x v pixels
            var (_, horizontal, vertical) = JpegEncoderCodec.GetSampling(_page.PixelFormat, _encoder.JpegChromaSubsampling);
            fields.Add(Array(TiffFormat.YCbCrSubSamplingTag, TiffFieldType.Short, [(ulong)horizontal, (ulong)vertical]));
            fields.Add(Rationals(TiffFormat.ReferenceBlackWhiteTag, [0, 255, 128, 255, 128, 255], 1));
        }

        if (metadata.XmpProfile is { } xmp)
        {
            fields.Add(new TiffWriteField(TiffFormat.XmpTag, TiffFieldType.Byte, (ulong)xmp.Data.Length, xmp.Data.Span.ToArray()));
        }

        if (metadata.IccProfile is { } icc)
        {
            fields.Add(new TiffWriteField(TiffFormat.IccProfileTag, TiffFieldType.Undefined, (ulong)icc.Data.Length, icc.Data.Span.ToArray()));
        }

        fields.Sort(static (left, right) => left.Tag.CompareTo(right.Tag));
        return fields;
    }

    private void WriteDirectory(ImageOutputBuffer output, List<TiffWriteField> fields)
    {
        var entryLength = IsBigTiff ? TiffFormat.BigTiffEntryLength : TiffFormat.ClassicEntryLength;
        var countLength = IsBigTiff ? 8 : 2;
        var tableLength = countLength + (fields.Count * entryLength) + OffsetSize;
        var directoryOffset = output.TotalBytes;

        // Out-of-line values follow the table; every block starts on an 8-byte boundary, which satisfies the word
        // alignment TIFF requires and keeps the 64-bit values of a BigTIFF naturally aligned
        var valueOffsets = new long[fields.Count];
        var valuesLength = 0L;
        for (var i = 0; i < fields.Count; i++)
        {
            if (fields[i].Data.Length <= OffsetSize)
                continue;

            valuesLength = Align(valuesLength);
            valueOffsets[i] = directoryOffset + tableLength + valuesLength;
            valuesLength += fields[i].Data.Length;
        }

        var total = tableLength + Align(valuesLength);
        if (total > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(_scope.Limits);

        // Every offset of the page (its strips, the values of its fields, the directory itself) is below this one
        if (!IsBigTiff && directoryOffset + total > uint.MaxValue)
            throw TiffFormat.Unsupported("The TIFF document would be larger than the 4 GiB its 32-bit offsets can address. Set TiffEncoder.BigTiff to write 64-bit offsets.", "File size");

        var span = output.GetSpan((int)total)[..(int)total];
        span.Clear();
        if (IsBigTiff)
        {
            WriteUInt64(span, (ulong)fields.Count);
        }
        else
        {
            WriteUInt16(span, (ushort)fields.Count);
        }

        for (var i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var entry = span.Slice(countLength + (i * entryLength), entryLength);
            WriteUInt16(entry, field.Tag);
            WriteUInt16(entry[2..], (ushort)field.Type);
            if (IsBigTiff)
            {
                WriteUInt64(entry[4..], field.Count);
            }
            else
            {
                WriteUInt32(entry[4..], (uint)field.Count);
            }

            var value = entry[(IsBigTiff ? 12 : 8)..];
            if (field.Data.Length <= OffsetSize)
            {
                field.Data.CopyTo(value);
            }
            else
            {
                WriteOffset(value, (ulong)valueOffsets[i]);
            }
        }

        // The next-directory pointer stays 0 until another page is written
        for (var i = 0; i < fields.Count; i++)
        {
            if (fields[i].Data.Length > OffsetSize)
            {
                fields[i].Data.CopyTo(span[(int)(valueOffsets[i] - directoryOffset)..]);
            }
        }

        output.Advance((int)total);
        _nextPointerOffset = directoryOffset + countLength + ((long)fields.Count * entryLength);
    }

    /// <summary>Patches the field that must point to the directory just written: the header, or the previous directory.</summary>
    private void PatchPointer(ImageOutputBuffer output, long directoryOffset)
    {
        Span<byte> pointer = stackalloc byte[8];
        WriteOffset(pointer, (ulong)directoryOffset);
        output.AddPatch(_pendingPointerOffset, pointer[..OffsetSize]);
        _pendingPointerOffset = _nextPointerOffset;
    }

    private static long Align(long value) => ((value + 7) / 8) * 8;

    private void WriteOffset(Span<byte> destination, ulong value)
    {
        if (IsBigTiff)
        {
            WriteUInt64(destination, value);
        }
        else
        {
            WriteUInt32(destination, (uint)value);
        }
    }

    private void WriteUInt16(Span<byte> destination, ushort value)
    {
        if (IsBigEndian)
        {
            BinaryPrimitives.WriteUInt16BigEndian(destination, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination, value);
        }
    }

    private void WriteUInt32(Span<byte> destination, uint value)
    {
        if (IsBigEndian)
        {
            BinaryPrimitives.WriteUInt32BigEndian(destination, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination, value);
        }
    }

    private void WriteUInt64(Span<byte> destination, ulong value)
    {
        if (IsBigEndian)
        {
            BinaryPrimitives.WriteUInt64BigEndian(destination, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt64LittleEndian(destination, value);
        }
    }

    private TiffWriteField Scalar(ushort tag, TiffFieldType type, ulong value) => Repeated(tag, type, value, 1);

    private TiffWriteField Repeated(ushort tag, TiffFieldType type, ulong value, int count)
    {
        var size = TiffFieldTypes.GetSize(type);
        var data = new byte[size * count];
        for (var i = 0; i < count; i++)
        {
            WriteValue(data.AsSpan(i * size), type, value);
        }

        return new TiffWriteField(tag, type, (ulong)count, data);
    }

    private TiffWriteField Array(ushort tag, TiffFieldType type, List<ulong> values)
    {
        var size = TiffFieldTypes.GetSize(type);
        var data = new byte[size * values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            WriteValue(data.AsSpan(i * size), type, values[i]);
        }

        return new TiffWriteField(tag, type, (ulong)values.Count, data);
    }

    private TiffWriteField Rationals(ushort tag, ReadOnlySpan<uint> numerators, uint denominator)
    {
        var data = new byte[8 * numerators.Length];
        for (var i = 0; i < numerators.Length; i++)
        {
            WriteUInt32(data.AsSpan(i * 8), numerators[i]);
            WriteUInt32(data.AsSpan((i * 8) + 4), denominator);
        }

        return new TiffWriteField(tag, TiffFieldType.Rational, (ulong)numerators.Length, data);
    }

    private void WriteValue(Span<byte> destination, TiffFieldType type, ulong value)
    {
        switch (type)
        {
            case TiffFieldType.Byte:
                destination[0] = (byte)value;
                break;

            case TiffFieldType.Short:
                WriteUInt16(destination, (ushort)value);
                break;

            case TiffFieldType.Long:
                WriteUInt32(destination, (uint)value);
                break;

            default:
                WriteUInt64(destination, value);
                break;
        }
    }

    /// <summary>The page being written.</summary>
    /// <param name="Frame">The frame of the page.</param>
    /// <param name="Size">The page size.</param>
    /// <param name="PixelFormat">The pixel format of the frame.</param>
    /// <param name="Layout">The written sample layout.</param>
    /// <param name="Metadata">The metadata plan of the page.</param>
    /// <param name="RowLength">The number of stored bytes of a row of the page.</param>
    /// <param name="IsTiled">Whether the page is written as tiles rather than strips.</param>
    /// <param name="BlockWidth">The width of a tile, or the page width for strips.</param>
    /// <param name="BlockHeight">The number of rows of a tile or a strip (<c>RowsPerStrip</c>; the last strip may hold fewer).</param>
    /// <param name="BlockRowLength">The number of stored bytes of a row of a block.</param>
    /// <param name="BlocksAcross">The number of blocks of a row of blocks (1 for strips).</param>
    /// <param name="BlocksDown">The number of rows of blocks.</param>
    /// <param name="Palette">The colors of a palette page, sorted.</param>
    /// <param name="PaletteIndexes">The index of each color of a palette page.</param>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct PageState(ImageFrame Frame, Size Size, PixelFormat PixelFormat, TiffSampleLayout Layout, MetadataWritePlan Metadata, int RowLength, bool IsTiled, int BlockWidth, int BlockHeight, int BlockRowLength, int BlocksAcross, int BlocksDown, ulong[]? Palette, Dictionary<ulong, byte>? PaletteIndexes)
    {
        /// <summary>Gets the number of strips or tiles of the page.</summary>
        public int BlockCount => BlocksAcross * BlocksDown;
    }

    /// <summary>
    /// The memory of the workers that compress the strips of a page: a strip, an LZW table and an output each (the first
    /// worker uses the strip and the table of the writer), and the strips of the current batch.
    /// </summary>
    private sealed class StripWorkers(int count) : IDisposable
    {
        public PooledBuffer[] Strips { get; } = new PooledBuffer[count];

        public TiffLzwEncoder?[] Encoders { get; } = new TiffLzwEncoder?[count];

        public ImageOutputBuffer[] Outputs { get; } = new ImageOutputBuffer[count];

        /// <summary>Gets the number of stored bytes of each block of the batch.</summary>
        public int[] Lengths { get; } = new int[count];

        /// <summary>Gets or sets the number of strips of the batch.</summary>
        public int Count { get; set; }

        /// <summary>Gets or sets the strip of the batch that is appended next.</summary>
        public int Next { get; set; }

        public void Dispose()
        {
            for (var i = 0; i < Strips.Length; i++)
            {
                if (i > 0)
                {
                    Strips[i]?.Dispose();
                    Encoders[i]?.Dispose();
                }

                Outputs[i]?.Dispose();
                Strips[i] = null!;
                Encoders[i] = null;
                Outputs[i] = null!;
            }
        }
    }

    /// <summary>What <see cref="PreparePage"/> found in a frame, kept until the page of that frame begins.</summary>
    private sealed class PreparedPage(ImageFrame frame, PixelFormat pixelFormat, TiffSampleLayout layout)
    {
        public ImageFrame Frame { get; } = frame;

        public PixelFormat PixelFormat { get; } = pixelFormat;

        public TiffSampleLayout Layout { get; } = layout;

        /// <summary>Gets or sets the colors of a palette page, sorted.</summary>
        public ulong[]? Palette { get; set; }

        /// <summary>Gets or sets the index of each color of a palette page.</summary>
        public Dictionary<ulong, byte>? PaletteIndexes { get; set; }
    }
}
