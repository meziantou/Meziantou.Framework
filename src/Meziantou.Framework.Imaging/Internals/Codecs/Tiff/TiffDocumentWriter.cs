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
/// writer can flush between strips.
/// </para>
/// </remarks>
internal sealed class TiffDocumentWriter : IDisposable
{
    private readonly TiffEncoder _encoder;
    private readonly AllocationScope _scope;
    private readonly List<ulong> _stripOffsets = [];
    private readonly List<ulong> _stripByteCounts = [];
    private PooledBuffer? _strip;
    private PooledBuffer? _converted;
    private long _pendingPointerOffset = -1;
    private long _nextPointerOffset = -1;
    private PageState _page;
    private int _rowsWritten;

    public TiffDocumentWriter(TiffEncoder encoder, AllocationScope scope)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(scope);
        _encoder = encoder;
        _scope = scope;
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

    /// <summary>Starts a page. Its strips are written by <see cref="EncodeStrip"/> and its directory by <see cref="CompletePage"/>.</summary>
    /// <param name="size">The page size.</param>
    /// <param name="pixelFormat">The pixel format of the frame that will be encoded.</param>
    /// <param name="metadata">The metadata plan of the page.</param>
    /// <exception cref="UnsupportedImageFeatureException">The page is too large for one strip buffer.</exception>
    public void BeginPage(Size size, PixelFormat pixelFormat, MetadataWritePlan metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var layout = TiffSampleLayout.ForPixelFormat(pixelFormat);
        var rowLength = (long)size.Width * layout.BytesPerPixel;
        if (rowLength > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(_scope.Limits);

        var rowsPerStrip = _encoder.RowsPerStrip > 0
            ? Math.Min(_encoder.RowsPerStrip, size.Height)
            : (int)Math.Clamp(TiffEncoder.DefaultStripBytes / Math.Max(1, rowLength), 1, size.Height);

        if (!CheckedSizes.TryMultiply(rowLength, rowsPerStrip, out var stripLength) || stripLength > CheckedSizes.MaxBufferLength)
        {
            rowsPerStrip = 1;
            stripLength = rowLength;
        }

        _page = new PageState(size, pixelFormat, layout, metadata, (int)rowLength, rowsPerStrip);
        _rowsWritten = 0;
        _stripOffsets.Clear();
        _stripByteCounts.Clear();
        EnsureBuffer(ref _strip, (int)stripLength);
        EnsureBuffer(ref _converted, (int)rowLength);
    }

    /// <summary>Writes the next strip of the current page.</summary>
    /// <param name="output">The output buffer.</param>
    /// <param name="frame">The frame of the page.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    /// <returns><see langword="true"/> when the last strip of the page was written.</returns>
    public bool EncodeStrip(ImageOutputBuffer output, ImageFrame frame, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(frame);
        var rows = Math.Min(_page.RowsPerStrip, _page.Size.Height - _rowsWritten);
        var stored = _strip!.Span[..(rows * _page.RowLength)];
        using (var lease = frame.GetStorage().AcquireLease())
        {
            for (var i = 0; i < rows; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SerializeRow(lease.GetRowBytes(_rowsWritten + i), stored.Slice(i * _page.RowLength, _page.RowLength));
            }
        }

        _stripOffsets.Add((ulong)output.TotalBytes);
        if (_encoder.Compression == TiffCompression.Deflate)
        {
            var start = output.TotalBytes;
            using (var destination = new BufferWriterStream(output))
            using (var zlib = new ZLibStream(destination, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(stored);
            }

            _stripByteCounts.Add((ulong)(output.TotalBytes - start));
        }
        else
        {
            output.Write(stored);
            _stripByteCounts.Add((ulong)stored.Length);
        }

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
        PageCount++;
    }

    public void Dispose()
    {
        _strip?.Dispose();
        _strip = null;
        _converted?.Dispose();
        _converted = null;
    }

    private void EnsureBuffer(ref PooledBuffer? buffer, int length)
    {
        if (buffer is not null && buffer.Length >= length)
            return;

        buffer?.Dispose();
        buffer = _scope.Rent(length, AllocationKind.Temporary, clear: false);
    }

    /// <summary>Converts one frame row to the sample layout of the page and serializes it in the byte order of the file.</summary>
    private void SerializeRow(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var width = _page.Size.Width;
        var layout = _page.Layout;
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
            Scalar(TiffFormat.CompressionTag, TiffFieldType.Short, (ulong)(_encoder.Compression == TiffCompression.Deflate ? TiffFormat.CompressionAdobeDeflate : TiffFormat.CompressionNone)),
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
            fields.Add(Rational(TiffFormat.XResolutionTag, x, ResolutionConversion.TiffResolutionDenominator));
            fields.Add(Rational(TiffFormat.YResolutionTag, y, ResolutionConversion.TiffResolutionDenominator));
            fields.Add(Scalar(TiffFormat.ResolutionUnitTag, TiffFieldType.Short, TiffFormat.ResolutionUnitInch));
        }

        if (layout.HasAlpha)
        {
            fields.Add(Scalar(TiffFormat.ExtraSamplesTag, TiffFieldType.Short, TiffFormat.ExtraSampleUnassociatedAlpha));
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

    private TiffWriteField Rational(ushort tag, uint numerator, uint denominator)
    {
        var data = new byte[8];
        WriteUInt32(data, numerator);
        WriteUInt32(data.AsSpan(4), denominator);
        return new TiffWriteField(tag, TiffFieldType.Rational, 1, data);
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
    private readonly record struct PageState(Size Size, PixelFormat PixelFormat, TiffSampleLayout Layout, MetadataWritePlan Metadata, int RowLength, int RowsPerStrip);
}
