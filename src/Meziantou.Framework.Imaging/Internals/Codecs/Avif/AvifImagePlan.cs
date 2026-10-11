using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The image that an AVIF file presents, resolved from its <c>meta</c> box: the primary item and the items it depends on
/// (the tiles of a grid, the alpha plane, the Exif and XMP metadata), its geometry and its color description. The plan is
/// validated when it is created, before any item data is read: a file that uses a feature that is not supported fails here.
/// </summary>
internal sealed class AvifImagePlan
{
    private AvifImagePlan()
    {
    }

    /// <summary>Gets the size of the image: the size of the primary item after its clean aperture, before its orientation.</summary>
    public Size Size { get; private set; }

    /// <summary>Gets the part of the primary item that the clean aperture keeps.</summary>
    public Rectangle Crop { get; private set; }

    /// <summary>Gets the orientation that the <c>irot</c> and <c>imir</c> properties of the primary item describe.</summary>
    public ExifOrientation Orientation { get; private set; } = ExifOrientation.TopLeft;

    /// <summary>Gets the color image.</summary>
    public AvifCodedImage Color { get; private set; } = null!;

    /// <summary>Gets the alpha plane, or <see langword="null"/> for an opaque image.</summary>
    public AvifCodedImage? Alpha { get; private set; }

    /// <summary>Gets the <c>colr</c> property of type <c>nclx</c> of the primary item, or <see langword="null"/>.</summary>
    public CicpColorDescription? Nclx { get; private set; }

    /// <summary>Gets the range of the ICC profile of the primary item in the <c>meta</c> payload, or an empty range.</summary>
    public (int Offset, int Length) IccProfile { get; private set; }

    /// <summary>Gets the Exif item that describes the primary item, or <see langword="null"/>.</summary>
    public AvifItem? Exif { get; private set; }

    /// <summary>Gets the XMP item that describes the primary item, or <see langword="null"/>.</summary>
    public AvifItem? Xmp { get; private set; }

    public bool HasAlpha => Alpha is not null;

    /// <summary>Gets the largest bit depth of the color and alpha planes.</summary>
    public int BitDepth => Math.Max(Color.BitDepth, Alpha?.BitDepth ?? 0);

    /// <summary>Gets the pixel format of an untyped load, which is also the layout of the rows that the decoder produces.</summary>
    public PixelFormat PixelFormat => DefaultPixelFormats.ForAvif(Color.IsMonochrome, HasAlpha, BitDepth);

    /// <summary>Resolves the image of a <c>meta</c> box.</summary>
    /// <param name="meta">The parsed box.</param>
    /// <param name="payload">The payload of the box.</param>
    /// <param name="limits">The resource limits of the operation.</param>
    /// <exception cref="InvalidImageContentException">The description of the image is inconsistent.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The image uses a feature that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">The image exceeds a resource limit.</exception>
    public static AvifImagePlan Create(AvifMetaBox meta, ReadOnlySpan<byte> payload, ImageResourceLimits limits)
    {
        var primaryId = meta.PrimaryItemId ?? throw AvifFormat.Invalid("the file has no primary item.");
        if (!meta.Items.TryGetValue(primaryId, out var primary) || primary.Type is 0)
            throw AvifFormat.Invalid("the primary item does not exist.");

        var plan = new AvifImagePlan();
        plan.Color = AvifCodedImage.Create(meta, payload, primary, limits);

        // The alpha plane is an auxiliary image of the primary item; a premultiplied color image cannot be stored
        foreach (var reference in meta.References)
        {
            if (reference.Type is AvifFormat.Prem && (reference.FromItemId == primaryId || reference.ToItemIds.AsSpan().Contains(primaryId)))
                throw AvifFormat.Unsupported("The AVIF image has premultiplied alpha.", "premultiplied alpha");

            if (reference.Type is not AvifFormat.Auxl || !reference.ToItemIds.AsSpan().Contains(primaryId) || plan.Alpha is not null)
                continue;

            if (!meta.Items.TryGetValue(reference.FromItemId, out var auxiliary) || !IsAlphaAuxiliary(meta, payload, auxiliary))
                continue;

            plan.Alpha = AvifCodedImage.Create(meta, payload, auxiliary, limits);
            if (plan.Alpha.Size != plan.Color.Size)
                throw AvifFormat.Unsupported("The alpha plane of the AVIF image does not have the size of the color image.", "alpha plane of a different size");

            // The transformations of the color image apply to its alpha plane, which declares the same ones or none
            if (plan.Alpha.Transforms.Count is not 0 && !AvifCodedImage.HasSameTransforms(plan.Color, plan.Alpha, payload))
                throw AvifFormat.Unsupported("The alpha plane of the AVIF image is not transformed as the color image.", "alpha plane with other transformations");
        }

        plan.ReadPrimaryProperties(meta, payload, primary);
        limits.EnsureCanvasWithinLimits(plan.Size.Width, plan.Size.Height);

        foreach (var reference in meta.References)
        {
            if (reference.Type is not AvifFormat.Cdsc || !reference.ToItemIds.AsSpan().Contains(primaryId) || !meta.Items.TryGetValue(reference.FromItemId, out var item))
                continue;

            if (item.Type is AvifFormat.Exif)
            {
                plan.Exif ??= item;
            }
            else if (item.IsXmp)
            {
                plan.Xmp ??= item;
            }
        }

        return plan;
    }

    private static bool IsAlphaAuxiliary(AvifMetaBox meta, ReadOnlySpan<byte> payload, AvifItem item)
    {
        foreach (var (index, _) in item.Properties)
        {
            var (type, offset, length) = meta.Properties[index - 1];
            if (type is not AvifFormat.AuxC)
                continue;

            var reader = new AvifBoxReader(payload.Slice(offset, length));
            reader.ReadFullBoxHeader();
            return reader.ReadCString().SequenceEqual(AvifFormat.AlphaAuxiliaryType);
        }

        return false;
    }

    // The transformative properties apply in the order of their association (ISO/IEC 23008-12, section 6.5)
    private void ReadPrimaryProperties(AvifMetaBox meta, ReadOnlySpan<byte> payload, AvifItem primary)
    {
        var size = Color.Size;
        var crop = new Rectangle(0, 0, size.Width, size.Height);
        var orientation = OrientationTransform.Identity;
        foreach (var (index, _) in primary.Properties)
        {
            var (type, offset, length) = meta.Properties[index - 1];
            var reader = new AvifBoxReader(payload.Slice(offset, length));
            switch (type)
            {
                case AvifFormat.Colr:
                    ReadColorInformation(ref reader, offset);
                    break;
                case AvifFormat.Clap:
                    if (!orientation.IsIdentity)
                        throw AvifFormat.Unsupported("The clean aperture of the AVIF image applies after its rotation.", "clean aperture after a rotation");

                    crop = ReadCleanAperture(ref reader, crop);
                    break;
                case AvifFormat.Irot:
                    // Anti-clockwise, by multiples of 90 degrees
                    orientation = orientation.Then((reader.ReadByte() & 3) switch
                    {
                        1 => OrientationTransform.ForRotation(RotateMode.Rotate270),
                        2 => OrientationTransform.ForRotation(RotateMode.Rotate180),
                        3 => OrientationTransform.ForRotation(RotateMode.Rotate90),
                        _ => OrientationTransform.Identity,
                    });
                    break;
                case AvifFormat.Imir:
                    // 0 exchanges the top and the bottom of the image, 1 its left and its right
                    orientation = orientation.Then(OrientationTransform.ForFlip((reader.ReadByte() & 1) is 0 ? FlipMode.Vertical : FlipMode.Horizontal));
                    break;
                case AvifFormat.Pasp:
                    if (reader.ReadUInt32() is 0 || reader.ReadUInt32() is 0)
                        throw AvifFormat.Invalid("the pixel aspect ratio is not valid.");

                    break;
                default:
                    break;
            }
        }

        Crop = crop;
        Size = new Size(crop.Width, crop.Height);
        Orientation = orientation.ToExifOrientation();
    }

    private void ReadColorInformation(ref AvifBoxReader reader, int offset)
    {
        var colorType = reader.ReadUInt32();
        if (colorType is AvifFormat.Nclx)
        {
            if (Nclx is not null)
                return;

            var primaries = reader.ReadUInt16();
            var transfer = reader.ReadUInt16();
            var matrix = reader.ReadUInt16();
            var fullRange = (reader.ReadByte() & 0x80) is not 0;
            if (primaries > 255 || transfer > 255 || matrix > 255)
                throw AvifFormat.Invalid("a color description code point is out of range.");

            Nclx = new CicpColorDescription((byte)primaries, (byte)transfer, (byte)matrix, fullRange);
        }
        else if (colorType is AvifFormat.Prof or AvifFormat.RICC && IccProfile.Length is 0)
        {
            IccProfile = (offset + reader.Position, reader.Remaining);
        }
    }

    // ISO/IEC 14496-12, section 12.1.4 "Clean Aperture Box": only whole sample rectangles are supported
    private static Rectangle ReadCleanAperture(ref AvifBoxReader reader, Rectangle current)
    {
        long widthN = reader.ReadUInt32();
        long widthD = reader.ReadUInt32();
        long heightN = reader.ReadUInt32();
        long heightD = reader.ReadUInt32();
        long horizontalOffsetN = reader.ReadInt32();
        long horizontalOffsetD = reader.ReadUInt32();
        long verticalOffsetN = reader.ReadInt32();
        long verticalOffsetD = reader.ReadUInt32();
        if (widthD is 0 || heightD is 0 || horizontalOffsetD is 0 || verticalOffsetD is 0 || widthN is 0 || heightN is 0)
            throw AvifFormat.Invalid("the clean aperture is not valid.");

        if (widthN % widthD is not 0 || heightN % heightD is not 0)
            throw AvifFormat.Unsupported("The clean aperture of the AVIF image is not a whole number of samples.", "fractional clean aperture");

        var width = widthN / widthD;
        var height = heightN / heightD;

        // The center of the aperture is at (size - 1) / 2 + offset: left = (size - width) / 2 + offset
        var left2N = ((current.Width - width) * horizontalOffsetD) + (2 * horizontalOffsetN);
        var top2N = ((current.Height - height) * verticalOffsetD) + (2 * verticalOffsetN);
        if (left2N % (2 * horizontalOffsetD) is not 0 || top2N % (2 * verticalOffsetD) is not 0)
            throw AvifFormat.Unsupported("The clean aperture of the AVIF image is not a whole number of samples.", "fractional clean aperture");

        var left = left2N / (2 * horizontalOffsetD);
        var top = top2N / (2 * verticalOffsetD);
        if (left < 0 || top < 0 || width > current.Width - left || height > current.Height - top)
            throw AvifFormat.Invalid("the clean aperture is outside the image.");

        return new Rectangle(current.X + (int)left, current.Y + (int)top, (int)width, (int)height);
    }
}
