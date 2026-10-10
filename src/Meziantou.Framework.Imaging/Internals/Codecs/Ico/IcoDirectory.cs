using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads and validates the directory of an icon or cursor file and resolves every representation without decoding a
/// single pixel: the header, the checked payload offsets and lengths, the kind of each
/// payload, the geometry read from the payload itself and the cursor hotspots.
/// </summary>
/// <remarks>
/// <para>
/// Every payload range is checked against the length of the input and against the directory itself: a payload that starts
/// inside the header or the entry table, that runs past the end of the file, or that is empty is rejected. Representations
/// may overlap each other (some real files share a payload), but none may overlap the directory.
/// </para>
/// <para>
/// Probing a payload reads at most the PNG header chunks or the DIB header and palette, so describing a 10-representation
/// icon costs a few hundred bytes of reads.
/// </para>
/// </remarks>
internal static class IcoDirectory
{
    /// <summary>Reads the directory and resolves every representation.</summary>
    /// <param name="source">The input.</param>
    /// <param name="context">The operation context.</param>
    /// <returns>The directory type (1 or 2) and the representations, in directory order.</returns>
    /// <exception cref="InvalidImageContentException">The header or an entry is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A payload uses a recognized but unsupported variant.</exception>
    public static (ImageFormat Format, IcoRepresentation[] Representations) Read(RandomAccessSource source, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);
        Span<byte> header = stackalloc byte[IcoFormat.HeaderLength];
        source.EnsureRange(0, IcoFormat.HeaderLength);
        source.Read(0, header);
        var reserved = BinaryPrimitives.ReadUInt16LittleEndian(header);
        var type = BinaryPrimitives.ReadUInt16LittleEndian(header[2..]);
        var count = BinaryPrimitives.ReadUInt16LittleEndian(header[4..]);
        var format = IcoFormat.GetFormat(type);
        if (reserved != 0)
            throw IcoFormat.Invalid(format, string.Create(CultureInfo.InvariantCulture, $"The icon directory reserved field is {reserved}; 0 is expected."));

        if (type is not (IcoFormat.IconType or IcoFormat.CursorType))
            throw IcoFormat.Invalid(format, string.Create(CultureInfo.InvariantCulture, $"The icon directory type is {type}; 1 (icon) or 2 (cursor) is expected."));

        if (count == 0)
            throw IcoFormat.Invalid(format, "The icon directory declares no representation.");

        if (count > context.Limits.MaxFrames)
            throw new ImageResourceLimitException(ImageResourceLimitKind.Frames, context.Limits.MaxFrames, count);

        var directoryLength = IcoFormat.HeaderLength + ((long)count * IcoFormat.EntryLength);
        source.EnsureRange(0, directoryLength);
        using var entries = source.ReadToBuffer(IcoFormat.HeaderLength, (long)count * IcoFormat.EntryLength);
        var representations = new IcoRepresentation[count];
        for (var i = 0; i < count; i++)
        {
            representations[i] = ReadEntry(source, context, format, type, entries.Span.Slice(i * IcoFormat.EntryLength, IcoFormat.EntryLength), i, directoryLength);
        }

        return (format, representations);
    }

    /// <summary>
    /// Selects the default representation, used by <c>Image.Load</c> and <c>Image.Identify</c>: the largest representation,
    /// then the one with the most bits per stored pixel, then the first in directory order.
    /// </summary>
    /// <param name="representations">The representations, in directory order. At least one.</param>
    /// <returns>The default representation.</returns>
    public static IcoRepresentation SelectDefault(IcoRepresentation[] representations)
    {
        ArgumentNullException.ThrowIfNull(representations);
        var best = representations[0];
        foreach (var representation in representations)
        {
            if (IsBetter(representation, best))
            {
                best = representation;
            }
        }

        return best;
    }

    private static bool IsBetter(IcoRepresentation candidate, IcoRepresentation best)
    {
        var candidateArea = (long)candidate.Size.Width * candidate.Size.Height;
        var bestArea = (long)best.Size.Width * best.Size.Height;
        if (candidateArea != bestArea)
            return candidateArea > bestArea;

        return candidate.BitsPerPixel > best.BitsPerPixel;
    }

    private static IcoRepresentation ReadEntry(RandomAccessSource source, ImageCodecContext context, ImageFormat format, int type, ReadOnlySpan<byte> entry, int index, long directoryLength)
    {
        var declaredWidth = IcoFormat.FromDimensionByte(entry[0]);
        var declaredHeight = IcoFormat.FromDimensionByte(entry[1]);
        if (entry[3] != 0)
            throw IcoFormat.Invalid(format, string.Create(CultureInfo.InvariantCulture, $"The reserved byte of the icon directory entry {index} is {entry[3]}; 0 is expected."));

        var length = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
        if (length == 0)
            throw IcoFormat.Invalid(format, string.Create(CultureInfo.InvariantCulture, $"The icon directory entry {index} declares an empty payload."));

        if (offset < directoryLength)
            throw IcoFormat.Invalid(format, string.Create(CultureInfo.InvariantCulture, $"The icon directory entry {index} stores its payload at offset {offset}, inside the {directoryLength}-byte directory."));

        if (!source.IsInRange(offset, length))
            throw IcoFormat.Invalid(format, string.Create(CultureInfo.InvariantCulture, $"The icon directory entry {index} declares {length} bytes at offset {offset}, outside the {source.Length}-byte input."));

        Point? hotspot = null;
        if (type == IcoFormat.CursorType)
        {
            hotspot = new Point(BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]), BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]));
        }

        var builder = new IcoRepresentationBuilder(source, context, format, index, offset, length, hotspot, declaredWidth, declaredHeight);
        return builder.Resolve();
    }
}
