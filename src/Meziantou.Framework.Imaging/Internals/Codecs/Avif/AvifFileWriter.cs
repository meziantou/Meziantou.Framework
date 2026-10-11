using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Writes the boxes of an AVIF file (AV1 Image File Format specification; ISO/IEC 23008-12 for the items and their
/// properties; ISO/IEC 14496-12 for the boxes). It is the writer of the encoder, independent of the builder that the tests
/// use to produce the inputs of the decoder.
/// </summary>
internal static class AvifFileWriter
{
    private const int ColorItemId = 1;
    private const int AlphaItemId = 2;

    /// <summary>What a still image file describes besides its coded payloads.</summary>
    internal sealed class StillImage
    {
        public required int Width { get; init; }

        public required int Height { get; init; }

        /// <summary>Gets the sequence header of the color image.</summary>
        public required Av1SequenceHeader Color { get; init; }

        /// <summary>Gets the number of bytes of the coded color image.</summary>
        public required int ColorLength { get; init; }

        /// <summary>Gets the sequence header of the alpha plane, or <see langword="null"/> for an opaque image.</summary>
        public Av1SequenceHeader? Alpha { get; init; }

        public int AlphaLength { get; init; }

        /// <summary>Gets the color description of the <c>colr</c> box of type <c>nclx</c>.</summary>
        public required CicpColorDescription ColorDescription { get; init; }

        public ReadOnlyMemory<byte> IccProfile { get; init; }

        public ReadOnlyMemory<byte> Exif { get; init; }

        public ReadOnlyMemory<byte> Xmp { get; init; }

        public ExifOrientation Orientation { get; init; } = ExifOrientation.TopLeft;
    }

    /// <summary>
    /// Writes everything that precedes the coded images of a still image file: the <c>ftyp</c> and <c>meta</c> boxes, the
    /// header of the <c>mdat</c> box and the EXIF and XMP items. The file continues with the coded alpha plane, when there is
    /// one, then the coded color image.
    /// </summary>
    /// <exception cref="UnsupportedImageFeatureException">The file would exceed 4 GiB.</exception>
    public static void WriteStillImagePrefix(PooledPayloadWriter output, StillImage image)
    {
        var writer = new AvifBoxWriter(output);
        var hasAlpha = image.Alpha is not null;
        var hasExif = !image.Exif.IsEmpty;
        var hasXmp = !image.Xmp.IsEmpty;
        var exifItemId = hasAlpha ? AlphaItemId + 1 : ColorItemId + 1;
        var xmpItemId = exifItemId + (hasExif ? 1 : 0);

        // ISO/IEC 14496-12, section 4.3 "File Type Box"
        var box = writer.BeginBox(AvifFormat.Ftyp);
        writer.WriteUInt32(AvifFormat.Avif);
        writer.WriteUInt32(0);
        writer.WriteUInt32(AvifFormat.Avif);
        writer.WriteUInt32(AvifFormat.Mif1);
        writer.WriteUInt32(AvifFormat.Miaf);
        if (GetProfileBrand(image.Color, image.Alpha) is { } brand)
            writer.WriteUInt32(brand);

        writer.EndBox(box);

        var meta = writer.BeginFullBox(AvifFormat.Meta, version: 0, flags: 0);
        box = writer.BeginFullBox(AvifFormat.Hdlr, version: 0, flags: 0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(AvifFormat.Pict);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteCString(default);
        writer.EndBox(box);

        box = writer.BeginFullBox(AvifFormat.Pitm, version: 0, flags: 0);
        writer.WriteUInt16(ColorItemId);
        writer.EndBox(box);

        // Item location: one extent per item, with 4-byte offsets from the start of the file and 4-byte lengths. The
        // offsets are known once the meta box ends
        Span<int> extentOffsets = stackalloc int[4];
        Span<int> lengths = [image.ColorLength, image.AlphaLength, image.Exif.Length + (hasExif ? 4 : 0), image.Xmp.Length];
        Span<int> itemIds = [ColorItemId, AlphaItemId, exifItemId, xmpItemId];
        Span<bool> present = [true, hasAlpha, hasExif, hasXmp];
        var itemCount = 1 + (hasAlpha ? 1 : 0) + (hasExif ? 1 : 0) + (hasXmp ? 1 : 0);
        box = writer.BeginFullBox(AvifFormat.Iloc, version: 0, flags: 0);
        writer.WriteByte(0x44); // offset_size and length_size
        writer.WriteByte(0x00); // base_offset_size and reserved
        writer.WriteUInt16(itemCount);
        for (var i = 0; i < 4; i++)
        {
            if (!present[i])
                continue;

            writer.WriteUInt16(itemIds[i]);
            writer.WriteUInt16(0); // data_reference_index: this file
            writer.WriteUInt16(1); // extent_count
            extentOffsets[i] = writer.Length;
            writer.WriteUInt32(0);
            writer.WriteUInt32((uint)lengths[i]);
        }

        writer.EndBox(box);

        box = writer.BeginFullBox(AvifFormat.Iinf, version: 0, flags: 0);
        writer.WriteUInt16(itemCount);
        WriteItemInfo(writer, ColorItemId, AvifFormat.Av01, contentType: default);
        if (hasAlpha)
            WriteItemInfo(writer, AlphaItemId, AvifFormat.Av01, contentType: default);

        if (hasExif)
            WriteItemInfo(writer, exifItemId, AvifFormat.Exif, contentType: default);

        if (hasXmp)
            WriteItemInfo(writer, xmpItemId, AvifFormat.Mime, AvifFormat.XmpContentType);

        writer.EndBox(box);

        if (hasAlpha || hasExif || hasXmp)
        {
            box = writer.BeginFullBox(AvifFormat.Iref, version: 0, flags: 0);
            if (hasAlpha)
                WriteItemReference(writer, AvifFormat.Auxl, AlphaItemId, ColorItemId);

            if (hasExif)
                WriteItemReference(writer, AvifFormat.Cdsc, exifItemId, ColorItemId);

            if (hasXmp)
                WriteItemReference(writer, AvifFormat.Cdsc, xmpItemId, ColorItemId);

            writer.EndBox(box);
        }

        WriteItemProperties(writer, image);
        writer.EndBox(meta);

        // The media data: the metadata items, the alpha plane, the color image
        long dataLength = 0;
        for (var i = 0; i < 4; i++)
        {
            if (present[i])
                dataLength += lengths[i];
        }

        var mediaStart = writer.Length + 8L;
        if (mediaStart + dataLength > uint.MaxValue)
            throw new UnsupportedImageFeatureException("The AVIF file would exceed 4 GiB, which this encoder does not write.", ImageFormat.Avif, "AVIF file size");

        writer.WriteUInt32((uint)(8 + dataLength));
        writer.WriteUInt32(AvifFormat.Mdat);
        var offset = mediaStart;
        Span<int> order = [2, 3, 1, 0];
        foreach (var i in order)
        {
            if (!present[i])
                continue;

            writer.PatchUInt32(extentOffsets[i], (uint)offset);
            offset += lengths[i];
        }

        if (hasExif)
        {
            // The offset of the TIFF header in the item: the data start with it
            writer.WriteUInt32(0);
            writer.WriteBytes(image.Exif.Span);
        }

        if (hasXmp)
            writer.WriteBytes(image.Xmp.Span);
    }

    /// <summary>
    /// Returns the rotation (<c>irot</c> angle, in anti-clockwise quarter turns) and the mirroring (<c>imir</c> axis, or -1)
    /// that, applied in this order, display the image upright: what the decoder composes into <paramref name="orientation"/>.
    /// </summary>
    public static (int Angle, int Axis) GetTransformations(ExifOrientation orientation)
    {
        // The fewest properties first: a rotation alone, a mirroring alone, then both
        ReadOnlySpan<(int Angle, int Axis)> candidates =
        [
            (0, -1), (1, -1), (2, -1), (3, -1),
            (0, 0), (0, 1),
            (1, 0), (2, 0), (3, 0),
            (1, 1), (2, 1), (3, 1),
        ];

        foreach (var (angle, axis) in candidates)
        {
            var transform = angle switch
            {
                1 => OrientationTransform.ForRotation(RotateMode.Rotate270),
                2 => OrientationTransform.ForRotation(RotateMode.Rotate180),
                3 => OrientationTransform.ForRotation(RotateMode.Rotate90),
                _ => OrientationTransform.Identity,
            };

            if (axis >= 0)
                transform = transform.Then(OrientationTransform.ForFlip(axis is 0 ? FlipMode.Vertical : FlipMode.Horizontal));

            if (transform.ToExifOrientation() == orientation)
                return (angle, axis);
        }

        throw new ArgumentOutOfRangeException(nameof(orientation), orientation, "The orientation must be between 1 and 8.");
    }

    // AV1 Image File Format specification, section 8 "Profiles": the Baseline profile holds AV1 Main profile images up to
    // level 5.1, the Advanced profile AV1 High profile images up to level 6.0
    private static uint? GetProfileBrand(Av1SequenceHeader color, Av1SequenceHeader? alpha)
    {
        var profile = Math.Max(color.Profile, alpha?.Profile ?? 0);
        var level = Math.Max(color.GetOperatingPoint(0).LevelIndex, alpha?.GetOperatingPoint(0).LevelIndex ?? 0);
        return profile switch
        {
            0 when level <= Av1Levels.Level51 => AvifFormat.MA1B,
            1 when level <= Av1Levels.Level60 => AvifFormat.MA1A,
            _ => null,
        };
    }

    // ISO/IEC 14496-12, section 8.11.6 "Item Information Box": an entry of version 2
    private static void WriteItemInfo(AvifBoxWriter writer, int itemId, uint itemType, ReadOnlySpan<byte> contentType)
    {
        var box = writer.BeginFullBox(AvifFormat.Infe, version: 2, flags: 0);
        writer.WriteUInt16(itemId);
        writer.WriteUInt16(0); // item_protection_index
        writer.WriteUInt32(itemType);
        writer.WriteCString(default); // item_name
        if (itemType is AvifFormat.Mime)
            writer.WriteCString(contentType);

        writer.EndBox(box);
    }

    // ISO/IEC 14496-12, section 8.11.12 "Item Reference Box"
    private static void WriteItemReference(AvifBoxWriter writer, uint type, int fromItemId, int toItemId)
    {
        var box = writer.BeginBox(type);
        writer.WriteUInt16(fromItemId);
        writer.WriteUInt16(1);
        writer.WriteUInt16(toItemId);
        writer.EndBox(box);
    }

    // ISO/IEC 23008-12, section 9.3 "Item Properties Box": the properties, then their association with the items. The
    // properties that a reader must understand to display the item are marked essential
    private static void WriteItemProperties(AvifBoxWriter writer, StillImage image)
    {
        const int Essential = 0x80;
        Span<byte> color = stackalloc byte[8];
        Span<byte> alpha = stackalloc byte[8];
        var colorCount = 0;
        var alphaCount = 0;
        var index = 0;
        var iprp = writer.BeginBox(AvifFormat.Iprp);
        var ipco = writer.BeginBox(AvifFormat.Ipco);

        // The size of the image, shared by the color image and the alpha plane
        var box = writer.BeginFullBox(AvifFormat.Ispe, version: 0, flags: 0);
        writer.WriteUInt32((uint)image.Width);
        writer.WriteUInt32((uint)image.Height);
        writer.EndBox(box);
        index++;
        color[colorCount++] = (byte)index;
        alpha[alphaCount++] = (byte)index;

        WritePixelInformation(writer, image.Color);
        color[colorCount++] = (byte)++index;
        WriteConfiguration(writer, image.Color);
        color[colorCount++] = (byte)(++index | Essential);

        // ISO/IEC 14496-12, section 12.1.5 "Colour information": the code points, then the profile that takes precedence
        box = writer.BeginBox(AvifFormat.Colr);
        writer.WriteUInt32(AvifFormat.Nclx);
        writer.WriteUInt16(image.ColorDescription.ColorPrimaries);
        writer.WriteUInt16(image.ColorDescription.TransferCharacteristics);
        writer.WriteUInt16(image.ColorDescription.MatrixCoefficients);
        writer.WriteByte((byte)(image.ColorDescription.FullRange ? 0x80 : 0));
        writer.EndBox(box);
        color[colorCount++] = (byte)++index;
        if (!image.IccProfile.IsEmpty)
        {
            box = writer.BeginBox(AvifFormat.Colr);
            writer.WriteUInt32(AvifFormat.Prof);
            writer.WriteBytes(image.IccProfile.Span);
            writer.EndBox(box);
            color[colorCount++] = (byte)++index;
        }

        // The transformations apply to the alpha plane as they do to the color image
        var (angle, axis) = GetTransformations(image.Orientation);
        if (angle is not 0)
        {
            box = writer.BeginBox(AvifFormat.Irot);
            writer.WriteByte((byte)angle);
            writer.EndBox(box);
            index++;
            color[colorCount++] = (byte)(index | Essential);
        }

        var rotationIndex = angle is not 0 ? index : 0;
        if (axis >= 0)
        {
            box = writer.BeginBox(AvifFormat.Imir);
            writer.WriteByte((byte)axis);
            writer.EndBox(box);
            index++;
            color[colorCount++] = (byte)(index | Essential);
        }

        var mirrorIndex = axis >= 0 ? index : 0;
        if (image.Alpha is { } alphaSequence)
        {
            WritePixelInformation(writer, alphaSequence);
            alpha[alphaCount++] = (byte)++index;
            WriteConfiguration(writer, alphaSequence);
            alpha[alphaCount++] = (byte)(++index | Essential);

            // ISO/IEC 23008-12, section 6.5.8 "Auxiliary type property"
            box = writer.BeginFullBox(AvifFormat.AuxC, version: 0, flags: 0);
            writer.WriteCString(AvifFormat.AlphaAuxiliaryType);
            writer.EndBox(box);
            alpha[alphaCount++] = (byte)++index;
            if (rotationIndex is not 0)
                alpha[alphaCount++] = (byte)(rotationIndex | Essential);

            if (mirrorIndex is not 0)
                alpha[alphaCount++] = (byte)(mirrorIndex | Essential);
        }

        writer.EndBox(ipco);

        box = writer.BeginFullBox(AvifFormat.Ipma, version: 0, flags: 0);
        writer.WriteUInt32(image.Alpha is null ? 1u : 2u);
        writer.WriteUInt16(ColorItemId);
        writer.WriteByte((byte)colorCount);
        writer.WriteBytes(color[..colorCount]);
        if (image.Alpha is not null)
        {
            writer.WriteUInt16(AlphaItemId);
            writer.WriteByte((byte)alphaCount);
            writer.WriteBytes(alpha[..alphaCount]);
        }

        writer.EndBox(box);
        writer.EndBox(iprp);
    }

    // ISO/IEC 23008-12, section 6.5.6 "Pixel information"
    private static void WritePixelInformation(AvifBoxWriter writer, Av1SequenceHeader sequence)
    {
        var box = writer.BeginFullBox(AvifFormat.Pixi, version: 0, flags: 0);
        writer.WriteByte((byte)sequence.PlaneCount);
        for (var i = 0; i < sequence.PlaneCount; i++)
        {
            writer.WriteByte((byte)sequence.BitDepth);
        }

        writer.EndBox(box);
    }

    // AV1 Codec ISO Media File Format Binding, section 2.3 "AV1 Codec Configuration Box": the record without
    // configuration OBU, since the sequence header is in the item
    private static void WriteConfiguration(AvifBoxWriter writer, Av1SequenceHeader sequence)
    {
        var point = sequence.GetOperatingPoint(0);
        var box = writer.BeginBox(AvifFormat.Av1C);
        writer.WriteByte(0x81); // marker and version
        writer.WriteByte((byte)((sequence.Profile << 5) | point.LevelIndex));
        writer.WriteByte((byte)((point.Tier << 7)
            | (sequence.BitDepth > 8 ? 0x40 : 0)
            | (sequence.BitDepth is 12 ? 0x20 : 0)
            | (sequence.Monochrome ? 0x10 : 0)
            | (sequence.SubsamplingX << 3)
            | (sequence.SubsamplingY << 2)
            | sequence.ChromaSamplePosition));
        writer.WriteByte(0); // no initial presentation delay
        writer.EndBox(box);
    }
}
