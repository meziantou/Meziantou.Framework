using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Writes a Windows icon or cursor file: the directory, then one payload per representation
///, shared by the single-image encoder session and by
/// <see cref="ImageCollectionWriterCore"/>.
/// </summary>
/// <remarks>
/// <para>
/// A directory entry stores the length and the offset of its payload, so the payloads are encoded before the directory is
/// written. That costs no seeking and no unbounded memory: a representation is at most 256x256 pixels, which is 256 KiB
/// of 32-bit samples, and the buffers are charged to the allocation scope of the writer like any other.
/// </para>
/// <para>
/// Payloads are either a still PNG, produced by the PNG encoder itself with its metadata stripped, or a 32-bit
/// <c>BITMAPINFOHEADER</c> DIB: bottom-up <c>BGRA</c> rows with straight alpha, a doubled stored height, and an all-zero
/// AND mask (the alpha channel already carries the transparency, and a decoder that reads a real alpha channel ignores
/// the mask).
/// </para>
/// <para>
/// A DIB stores 8-bit samples, a PNG up to 16: pixels with 16-bit samples are therefore written as a PNG, and asking for a
/// DIB explicitly is an error instead of a silent loss of precision (<see cref="UsesPngPayload"/>).
/// </para>
/// </remarks>
internal static class IcoDocumentWriter
{
    /// <summary>One representation to write.</summary>
    /// <param name="Frame">The frame holding the pixels.</param>
    /// <param name="PixelFormat">The pixel format of <paramref name="Frame"/>.</param>
    /// <param name="Hotspot">The hotspot of a cursor representation, or <see langword="null"/>.</param>
    [StructLayout(LayoutKind.Auto)]
    public readonly record struct Entry(ImageFrame Frame, PixelFormat PixelFormat, Point? Hotspot);

    /// <summary>One representation whose payload is already encoded: the directory entry no longer needs its pixels.</summary>
    /// <param name="Size">The size of the representation.</param>
    /// <param name="Hotspot">The hotspot of a cursor representation, or <see langword="null"/>.</param>
    /// <param name="Payload">The encoded payload. The caller owns and disposes it.</param>
    [StructLayout(LayoutKind.Auto)]
    public readonly record struct EncodedEntry(Size Size, Point? Hotspot, PooledBuffer Payload);

    /// <summary>Validates that a representation can be stored by the container.</summary>
    /// <param name="format">The output format.</param>
    /// <param name="size">The size of the representation.</param>
    /// <exception cref="UnsupportedImageFeatureException">A side exceeds <see cref="IcoFormat.MaxDimension"/>.</exception>
    public static void ValidateSize(ImageFormat format, Size size)
    {
        if (size.Width > IcoFormat.MaxDimension || size.Height > IcoFormat.MaxDimension)
            throw IcoFormat.Unsupported(format, string.Create(CultureInfo.InvariantCulture, $"{ImageFormatNames.Get(format)} output stores at most {IcoFormat.MaxDimension} pixels per side; the representation is {size.Width}x{size.Height}."), "Canvas size");
    }

    /// <summary>
    /// Selects the payload of a representation, shared by icon, cursor and animated cursor output: a PNG when it is
    /// requested, and for <see cref="IconPayloadFormat.Auto"/> when a side exceeds
    /// <see cref="IcoEncoder.AutoDibMaxDimension"/> or when the pixels have 16-bit samples, which a DIB cannot store.
    /// </summary>
    /// <param name="format">The output format, reported by the exception.</param>
    /// <param name="payloadFormat">The requested payload.</param>
    /// <param name="size">The size of the representation.</param>
    /// <param name="pixelFormat">The pixel format of the representation.</param>
    /// <returns><see langword="true"/> for a PNG payload; <see langword="false"/> for a 32-bit DIB.</returns>
    /// <exception cref="UnsupportedImageFeatureException">A DIB is requested for pixels with 16-bit samples: nothing is narrowed silently.</exception>
    public static bool UsesPngPayload(ImageFormat format, IconPayloadFormat payloadFormat, Size size, PixelFormat pixelFormat)
    {
        var isSixteenBit = PixelFormats.GetBitsPerComponent(pixelFormat) > 8;
        switch (payloadFormat)
        {
            case IconPayloadFormat.Png:
                return true;

            case IconPayloadFormat.Dib:
                if (isSixteenBit)
                    throw IcoFormat.Unsupported(format, $"A DIB payload stores 8-bit samples, so encoding {pixelFormat} pixels would discard precision. Set the encoder's PayloadFormat to Png or Auto, or convert the image explicitly with CloneAs.", "Bit depth reduction");

                return false;

            default:
                return isSixteenBit || size.Width > IcoEncoder.AutoDibMaxDimension || size.Height > IcoEncoder.AutoDibMaxDimension;
        }
    }

    /// <summary>Writes the whole file.</summary>
    /// <param name="encoder">The encoder settings.</param>
    /// <param name="entries">The representations, in directory order. At least one.</param>
    /// <param name="output">The output buffer, positioned at the first byte of the file.</param>
    /// <param name="scope">The allocation scope charged for the payload buffers.</param>
    /// <param name="configuration">The configuration of the nested PNG encodes.</param>
    /// <param name="cancellationToken">The token checked between representations.</param>
    public static void Write(IcoEncoder encoder, IReadOnlyList<Entry> entries, ImageOutputBuffer output, AllocationScope scope, ImageConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(configuration);
        EnsureCount(encoder, entries.Count);
        var encoded = new List<EncodedEntry>(entries.Count);
        try
        {
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                encoded.Add(EncodeEntry(encoder, entry, scope, configuration));
            }

            WriteDirectoryAndPayloads(encoder, encoded, output);
        }
        finally
        {
            foreach (var item in encoded)
            {
                item.Payload.Dispose();
            }
        }
    }

    /// <summary>Encodes the payload of one representation, so that its pixels do not have to stay alive until the file is written.</summary>
    /// <param name="encoder">The encoder settings.</param>
    /// <param name="entry">The representation.</param>
    /// <param name="scope">The allocation scope charged for the payload buffer.</param>
    /// <param name="configuration">The configuration of a nested PNG encode.</param>
    /// <returns>The encoded representation. The caller disposes its payload.</returns>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The payload buffer is returned to the caller, who disposes it once the directory is written.")]
    public static EncodedEntry EncodeEntry(IcoEncoder encoder, Entry entry, AllocationScope scope, ImageConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(configuration);
        ValidateSize(encoder.Format, entry.Frame.Size);
        var payload = UsesPngPayload(encoder.Format, encoder.PayloadFormat, entry.Frame.Size, entry.PixelFormat)
            ? EncodePngPayload(entry, scope, configuration)
            : EncodeDibPayload(entry, scope);

        return new EncodedEntry(entry.Frame.Size, entry.Hotspot, payload);
    }

    /// <summary>Writes the directory and the already-encoded payloads.</summary>
    /// <param name="encoder">The encoder settings.</param>
    /// <param name="entries">The encoded representations, in directory order. At least one.</param>
    /// <param name="output">The output buffer, positioned at the first byte of the file.</param>
    public static void WriteEncoded(IcoEncoder encoder, IReadOnlyList<EncodedEntry> entries, ImageOutputBuffer output)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(output);
        EnsureCount(encoder, entries.Count);
        WriteDirectoryAndPayloads(encoder, entries, output);
    }

    private static void EnsureCount(IcoEncoder encoder, int count)
    {
        if (count == 0)
            throw new InvalidOperationException("An icon or cursor file stores at least one representation.");

        if (count > ushort.MaxValue)
            throw IcoFormat.Unsupported(encoder.Format, string.Create(CultureInfo.InvariantCulture, $"An icon directory stores at most {ushort.MaxValue} representations; {count} were given."), "Representation count");
    }

    private static void WriteDirectoryAndPayloads(IcoEncoder encoder, IReadOnlyList<EncodedEntry> entries, ImageOutputBuffer output)
    {
        var directoryLength = IcoFormat.HeaderLength + (entries.Count * IcoFormat.EntryLength);
        var header = output.GetSpan(directoryLength)[..directoryLength];
        header.Clear();
        BinaryPrimitives.WriteUInt16LittleEndian(header, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(header[2..], (ushort)(encoder.Kind == IconKind.Cursor ? IcoFormat.CursorType : IcoFormat.IconType));
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], (ushort)entries.Count);

        var payloadOffset = (long)directoryLength;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var directoryEntry = header.Slice(IcoFormat.HeaderLength + (i * IcoFormat.EntryLength), IcoFormat.EntryLength);
            directoryEntry[0] = IcoFormat.ToDimensionByte(entry.Size.Width);
            directoryEntry[1] = IcoFormat.ToDimensionByte(entry.Size.Height);
            directoryEntry[2] = 0;
            directoryEntry[3] = 0;
            if (encoder.Kind == IconKind.Cursor)
            {
                var hotspot = entry.Hotspot ?? new Point(0, 0);
                BinaryPrimitives.WriteUInt16LittleEndian(directoryEntry[4..], (ushort)hotspot.X);
                BinaryPrimitives.WriteUInt16LittleEndian(directoryEntry[6..], (ushort)hotspot.Y);
            }
            else
            {
                BinaryPrimitives.WriteUInt16LittleEndian(directoryEntry[4..], 1);
                BinaryPrimitives.WriteUInt16LittleEndian(directoryEntry[6..], 32);
            }

            BinaryPrimitives.WriteUInt32LittleEndian(directoryEntry[8..], (uint)entry.Payload.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(directoryEntry[12..], (uint)payloadOffset);
            payloadOffset += entry.Payload.Length;
            if (payloadOffset > uint.MaxValue)
                throw IcoFormat.Unsupported(encoder.Format, "The icon file would be larger than the 4 GiB its 32-bit payload offsets can address.", "File size");
        }

        output.Advance(directoryLength);
        foreach (var entry in entries)
        {
            output.Write(entry.Payload.Span);
        }
    }

    private static PooledBuffer EncodePngPayload(Entry entry, AllocationScope scope, ImageConfiguration configuration)
    {
        using var stream = new MemoryStream();
        var options = new ImageWriterOptions(entry.Frame.Size)
        {
            Encoder = new PngEncoder { AnimationMode = PngAnimationMode.Static, MetadataHandling = MetadataHandling.Strip },
            ExpectedFrameCount = 1,
            Configuration = configuration,
            LeaveOpen = true,
        };

        using (var writer = ImageWriterCore.Create(stream, options, entry.PixelFormat))
        {
            writer.WriteFrame(entry.Frame);
            writer.Complete();
        }

        var payload = scope.Rent((int)stream.Length, AllocationKind.Temporary, clear: false);
        try
        {
            stream.GetBuffer().AsSpan(0, (int)stream.Length).CopyTo(payload.Span);
            return payload;
        }
        catch
        {
            payload.Dispose();
            throw;
        }
    }

    private static PooledBuffer EncodeDibPayload(Entry entry, AllocationScope scope)
    {
        var size = entry.Frame.Size;
        var colorRowLength = (int)BmpFormat.GetRowLength(size.Width, 32);
        var maskRowLength = (int)BmpFormat.GetRowLength(size.Width, 1);
        var colorLength = colorRowLength * size.Height;
        var maskLength = maskRowLength * size.Height;
        var payload = scope.Rent(BmpFormat.InfoHeaderLength + colorLength + maskLength, AllocationKind.Temporary, clear: true);
        try
        {
            var span = payload.Span;
            BinaryPrimitives.WriteUInt32LittleEndian(span, BmpFormat.InfoHeaderLength);
            BinaryPrimitives.WriteInt32LittleEndian(span[4..], size.Width);

            // An icon DIB declares the color rows and the AND mask together, so its height is doubled
            BinaryPrimitives.WriteInt32LittleEndian(span[8..], size.Height * 2);
            BinaryPrimitives.WriteUInt16LittleEndian(span[12..], 1);
            BinaryPrimitives.WriteUInt16LittleEndian(span[14..], 32);
            BinaryPrimitives.WriteUInt32LittleEndian(span[16..], (uint)BmpCompression.Rgb);
            BinaryPrimitives.WriteUInt32LittleEndian(span[20..], (uint)(colorLength + maskLength));

            var colors = span.Slice(BmpFormat.InfoHeaderLength, colorLength);
            using var lease = entry.Frame.GetStorage().AcquireLease();
            var sourceLength = size.Width * PixelFormats.GetBytesPerPixel(entry.PixelFormat);
            for (var y = 0; y < size.Height; y++)
            {
                // Rows are stored bottom-up
                var destination = colors.Slice((size.Height - 1 - y) * colorRowLength, size.Width * 4);
                PixelConverter.ConvertRow(entry.PixelFormat, lease.GetRowBytes(y)[..sourceLength], PixelFormat.Bgra32, destination, background: null, ImageFormat.Ico);
            }

            // The AND mask stays zero: every pixel is "not masked" and the alpha channel carries the transparency
            return payload;
        }
        catch
        {
            payload.Dispose();
            throw;
        }
    }
}
