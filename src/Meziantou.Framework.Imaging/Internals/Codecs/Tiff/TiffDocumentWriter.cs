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
/// Layout per page: the strips first, in top-down order, then the directory that describes them. A directory stores the
/// offset and the stored length of every strip, so it can only be written once the strips are compressed; the offset
/// that points <em>to</em> the directory (the header's first-IFD field, or the previous directory's next-IFD field) is
/// therefore patched afterward, which is why the destination must be seekable.
/// </para>
/// <para>
/// One call to <see cref="EncodeStrip"/> writes one strip, so the memory of an encode is one strip (about
/// <see cref="TiffEncoder.DefaultStripBytes"/> of samples) whatever the size of the page or the number of pages, and the
/// writer can flush between strips. A page compressed with JPEG is one strip, written by the JPEG encoder of the library
/// in as many calls as it needs: its memory is the one of a JPEG file.
/// </para>
/// <para>
/// Nothing is lost silently. The bilevel and palette layouts store a page exactly or reject it, and so does a layout
/// without alpha given a pixel that is not opaque: <see cref="PreparePage"/> reads the whole frame before anything of
/// the page is written.
/// </para>
/// </remarks>
internal sealed class TiffDocumentWriter : IDisposable
{
    private const ulong WhiteColor = 0xFFFF_FFFF_FFFF;

    private readonly TiffEncoder _encoder;
    private readonly AllocationScope _scope;
    private readonly ImageConfiguration _configuration;
    private readonly int _compression;
    private readonly List<ulong> _stripOffsets = [];
    private readonly List<ulong> _stripByteCounts = [];
    private PooledBuffer? _strip;
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
    private int _rowsWritten;

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

    /// <summary>Gets a value indicating whether the current page still has strips to write.</summary>
    public bool HasPendingRows => _rowsWritten < _page.Size.Height;

    /// <summary>Gets the number of pages whose directory was written.</summary>
    public int PageCount { get; private set; }

    private bool IsBigTiff => _encoder.BigTiff;

    private bool IsBigEndian => _encoder.BigEndian;

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
    /// <exception cref="UnsupportedImageFeatureException">The layout or the compression cannot store the frame exactly.</exception>
    public void PreparePage(ImageFrame frame, PixelFormat pixelFormat)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var layout = TiffSampleLayout.Create(_encoder, pixelFormat);
        var size = frame.Size;
        if (_compression == TiffFormat.CompressionJpeg)
        {
            if (PixelFormats.GetBitsPerComponent(pixelFormat) > 8)
                throw TiffFormat.Unsupported($"JPEG compression stores 8-bit samples, so encoding {pixelFormat} pixels would discard precision. Select a lossless TiffCompression, or convert the image explicitly with CloneAs.", "Bit depth reduction");

            if (size.Width > ushort.MaxValue || size.Height > ushort.MaxValue)
                throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"A JPEG datastream stores at most 65,535 pixels per side and a JPEG-compressed page is one datastream; the page is {size.Width}x{size.Height}. Select another TiffCompression."), "Canvas size");
        }

        var prepared = new PreparedPage(frame, pixelFormat, layout);
        var rejectsAlpha = PixelFormats.HasAlpha(pixelFormat) && !layout.HasAlpha;
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
            PreparePage(frame, pixelFormat);
        }

        var prepared = _prepared!;
        _prepared = null;
        var layout = prepared.Layout;
        var rowLength = layout.GetRowLength(size.Width);
        if (rowLength > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(_scope.Limits);

        var isJpeg = _compression == TiffFormat.CompressionJpeg;
        int rowsPerStrip;
        if (isJpeg)
        {
            // One strip holds the page. TIFF Technical Note 2 asks for a strip height that is a multiple of the height of a
            // minimum coded unit, which is also what makes it a multiple of the rows a chroma sample covers
            var unitRows = 8 * JpegEncoderCodec.GetSampling(pixelFormat, _encoder.JpegChromaSubsampling).V;
            rowsPerStrip = (size.Height + unitRows - 1) / unitRows * unitRows;
        }
        else
        {
            rowsPerStrip = _encoder.RowsPerStrip > 0
                ? Math.Min(_encoder.RowsPerStrip, size.Height)
                : (int)Math.Clamp(TiffEncoder.DefaultStripBytes / Math.Max(1, rowLength), 1, size.Height);
        }

        if (!isJpeg && (!CheckedSizes.TryMultiply(rowLength, rowsPerStrip, out var stripLength) || stripLength > CheckedSizes.MaxBufferLength))
        {
            rowsPerStrip = 1;
        }

        _page = new PageState(frame, size, pixelFormat, layout, metadata, (int)rowLength, rowsPerStrip, prepared.Palette, prepared.PaletteIndexes);
        _rowsWritten = 0;
        _stripOffsets.Clear();
        _stripByteCounts.Clear();
        if (isJpeg)
        {
            BeginJpeg(frame, size, pixelFormat);
            return;
        }

        EnsureBuffer(ref _strip, (int)rowLength * rowsPerStrip);
        if (!layout.IsBilevel && !layout.IsIndexed && layout.BitsPerSample == 16)
        {
            EnsureBuffer(ref _converted, (int)rowLength);
        }

        if (_compression is TiffFormat.CompressionCcittRle or TiffFormat.CompressionCcittFax3 or TiffFormat.CompressionCcittFax4)
        {
            var listLength = TiffCcittEncoder.GetChangeListLength(size.Width) * 2 * sizeof(int);
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

        var rows = Math.Min(_page.RowsPerStrip, _page.Size.Height - _rowsWritten);
        var rowLength = _page.RowLength;
        var stored = _strip!.Span[..(rows * rowLength)];
        using (var lease = _page.Frame.GetStorage().AcquireLease())
        {
            for (var i = 0; i < rows; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SerializeRow(lease.GetRowBytes(_rowsWritten + i), stored.Slice(i * rowLength, rowLength));
            }
        }

        var start = output.TotalBytes;
        _stripOffsets.Add((ulong)start);
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
                    TiffPackBits.EncodeRow(stored.Slice(i * rowLength, rowLength), output);
                }

                break;

            case TiffFormat.CompressionCcittRle or TiffFormat.CompressionCcittFax3 or TiffFormat.CompressionCcittFax4:
                var lists = unsafe(MemoryMarshal.Cast<byte, int>(_changes!.Span));
                var listLength = (int)TiffCcittEncoder.GetChangeListLength(_page.Size.Width);
                TiffCcittEncoder.Encode(stored, _page.Size.Width, rows, _compression, lists[..listLength], lists.Slice(listLength, listLength), output);
                break;

            default:
                using (var destination = new BufferWriterStream(output))
                using (var zlib = new ZLibStream(destination, CompressionLevel.Optimal, leaveOpen: true))
                {
                    zlib.Write(stored);
                }

                break;
        }

        _stripByteCounts.Add((ulong)(output.TotalBytes - start));
        _rowsWritten += rows;
        return !HasPendingRows;
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
        _strip?.Dispose();
        _strip = null;
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

    /// <summary>Converts one frame row to the sample layout of the page and serializes it in the byte order of the file.</summary>
    private void SerializeRow(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var width = _page.Size.Width;
        var layout = _page.Layout;
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

        _stripOffsets.Add((ulong)_jpegStart);
        _stripByteCounts.Add((ulong)(output.TotalBytes - _jpegStart));
        _rowsWritten = _page.Size.Height;
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
            Array(TiffFormat.StripOffsetsTag, offsetType, _stripOffsets),
            Scalar(TiffFormat.SamplesPerPixelTag, TiffFieldType.Short, (ulong)layout.SamplesPerPixel),
            Scalar(TiffFormat.RowsPerStripTag, TiffFieldType.Long, (ulong)_page.RowsPerStrip),
            Array(TiffFormat.StripByteCountsTag, offsetType, _stripByteCounts),
            Scalar(TiffFormat.PlanarConfigurationTag, TiffFieldType.Short, TiffFormat.PlanarChunky),
            Repeated(TiffFormat.SampleFormatTag, TiffFieldType.Short, TiffFormat.SampleFormatUnsignedInteger, layout.SamplesPerPixel),
        };

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

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct PageState(ImageFrame Frame, Size Size, PixelFormat PixelFormat, TiffSampleLayout Layout, MetadataWritePlan Metadata, int RowLength, int RowsPerStrip, ulong[]? Palette, Dictionary<ulong, byte>? PaletteIndexes);

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
