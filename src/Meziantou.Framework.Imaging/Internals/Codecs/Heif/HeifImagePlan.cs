using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The image that a file of the HEIF family presents, resolved from its <c>meta</c> box: the primary item and the items it depends on
/// (the tiles of a grid, the alpha plane, the Exif and XMP metadata), its geometry and its color description. The plan is
/// validated when it is created, before any item data is read: a file that uses a feature that is not supported fails here.
/// </summary>
internal sealed class HeifImagePlan
{
    private readonly HeifCodecBinding _binding;

    private HeifImagePlan(HeifCodecBinding binding)
    {
        _binding = binding;
    }

    /// <summary>Gets the size of the image: the size of the primary item after its clean aperture, before its orientation.</summary>
    public Size Size { get; private set; }

    /// <summary>Gets the part of the primary item that the clean aperture keeps.</summary>
    public Rectangle Crop { get; private set; }

    /// <summary>Gets the orientation that the <c>irot</c> and <c>imir</c> properties of the primary item describe.</summary>
    public ExifOrientation Orientation { get; private set; } = ExifOrientation.TopLeft;

    /// <summary>Gets the color image.</summary>
    public HeifCodedImage Color { get; private set; } = null!;

    /// <summary>Gets the alpha plane, or <see langword="null"/> for an opaque image.</summary>
    public HeifCodedImage? Alpha { get; private set; }

    /// <summary>Gets the <c>colr</c> property of type <c>nclx</c> of the primary item, or <see langword="null"/>.</summary>
    public CicpColorDescription? Nclx { get; private set; }

    /// <summary>Gets the range of the ICC profile of the primary item in the <c>meta</c> payload, or an empty range.</summary>
    public (int Offset, int Length) IccProfile { get; private set; }

    /// <summary>Gets the Exif item that describes the primary item, or <see langword="null"/>.</summary>
    public HeifItem? Exif { get; private set; }

    /// <summary>Gets the XMP item that describes the primary item, or <see langword="null"/>.</summary>
    public HeifItem? Xmp { get; private set; }

    public bool HasAlpha => Alpha is not null;

    /// <summary>Gets the largest bit depth of the color and alpha planes.</summary>
    public int BitDepth => Math.Max(Color.BitDepth, Alpha?.BitDepth ?? 0);

    /// <summary>Gets the pixel format of an untyped load, which is also the layout of the rows that the decoder produces.</summary>
    public PixelFormat PixelFormat => _binding.GetDefaultPixelFormat(Color.IsMonochrome, HasAlpha, BitDepth);

    /// <summary>Resolves the image of a <c>meta</c> box.</summary>
    /// <param name="binding">The format of the file.</param>
    /// <param name="meta">The parsed box.</param>
    /// <param name="payload">The payload of the box.</param>
    /// <param name="limits">The resource limits of the operation.</param>
    /// <exception cref="InvalidImageContentException">The description of the image is inconsistent.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The image uses a feature that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">The image exceeds a resource limit.</exception>
    public static HeifImagePlan Create(HeifCodecBinding binding, HeifMetaBox meta, ReadOnlySpan<byte> payload, ImageResourceLimits limits)
    {
        var primaryId = meta.PrimaryItemId ?? throw binding.Invalid("the file has no primary item.");
        if (!meta.Items.TryGetValue(primaryId, out var primary) || primary.Type is 0)
            throw binding.Invalid("the primary item does not exist.");

        var plan = new HeifImagePlan(binding);
        plan.Color = HeifCodedImage.Create(binding, meta, payload, primary, limits);

        // The alpha plane is an auxiliary image of the primary item; a premultiplied color image cannot be stored
        foreach (var reference in meta.References)
        {
            if (reference.Type is HeifFormat.Prem && (reference.FromItemId == primaryId || reference.ToItemIds.AsSpan().Contains(primaryId)))
                throw binding.Unsupported($"The {binding.Name} image has premultiplied alpha.", "premultiplied alpha");

            if (reference.Type is not HeifFormat.Auxl || !reference.ToItemIds.AsSpan().Contains(primaryId) || plan.Alpha is not null)
                continue;

            if (!meta.Items.TryGetValue(reference.FromItemId, out var auxiliary) || !IsAlphaAuxiliary(binding, meta, payload, auxiliary))
                continue;

            plan.Alpha = HeifCodedImage.Create(binding, meta, payload, auxiliary, limits);
            if (plan.Alpha.Size != plan.Color.Size)
                throw binding.Unsupported($"The alpha plane of the {binding.Name} image does not have the size of the color image.", "alpha plane of a different size");

            // The transformations of the color image apply to its alpha plane, which declares the same ones or none
            if (plan.Alpha.Transforms.Count is not 0 && !HeifCodedImage.HasSameTransforms(plan.Color, plan.Alpha, payload))
                throw binding.Unsupported($"The alpha plane of the {binding.Name} image is not transformed as the color image.", "alpha plane with other transformations");
        }

        plan.ReadPrimaryProperties(meta, payload, primary);
        limits.EnsureCanvasWithinLimits(plan.Size.Width, plan.Size.Height);

        foreach (var reference in meta.References)
        {
            if (reference.Type is not HeifFormat.Cdsc || !reference.ToItemIds.AsSpan().Contains(primaryId) || !meta.Items.TryGetValue(reference.FromItemId, out var item))
                continue;

            if (item.Type is HeifFormat.Exif)
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

    /// <summary>
    /// Returns whether the primary item of a <c>meta</c> box is an image of the codec of a format: a coded item, or a grid
    /// whose first tile is one.
    /// </summary>
    public static bool IsCodedWith(HeifCodecBinding binding, HeifMetaBox meta)
    {
        if (meta.PrimaryItemId is not { } primaryId || !meta.Items.TryGetValue(primaryId, out var primary))
            return false;

        if (primary.Type is not HeifFormat.Grid)
            return binding.IsCodedItemType(primary.Type);

        foreach (var reference in meta.References)
        {
            if (reference.Type is HeifFormat.Dimg && reference.FromItemId == primaryId && reference.ToItemIds.Length > 0)
                return meta.Items.TryGetValue(reference.ToItemIds[0], out var tile) && binding.IsCodedItemType(tile.Type);
        }

        return false;
    }

    private static bool IsAlphaAuxiliary(HeifCodecBinding binding, HeifMetaBox meta, ReadOnlySpan<byte> payload, HeifItem item)
    {
        foreach (var (index, _) in item.Properties)
        {
            var (type, offset, length) = meta.Properties[index - 1];
            if (type is not HeifFormat.AuxC)
                continue;

            var reader = new HeifBoxReader(payload.Slice(offset, length), binding);
            reader.ReadFullBoxHeader();
            return binding.IsAlphaAuxiliaryType(reader.ReadCString());
        }

        return false;
    }

    // The transformative properties apply in the order of their association (ISO/IEC 23008-12, section 6.5)
    private void ReadPrimaryProperties(HeifMetaBox meta, ReadOnlySpan<byte> payload, HeifItem primary)
    {
        var binding = _binding;
        var size = Color.Size;
        var crop = new Rectangle(0, 0, size.Width, size.Height);
        var orientation = OrientationTransform.Identity;
        foreach (var (index, _) in primary.Properties)
        {
            var (type, offset, length) = meta.Properties[index - 1];
            var reader = new HeifBoxReader(payload.Slice(offset, length), binding);
            switch (type)
            {
                case HeifFormat.Colr:
                    ReadColorInformation(ref reader, offset);
                    break;
                case HeifFormat.Clap:
                    if (!orientation.IsIdentity)
                        throw binding.Unsupported($"The clean aperture of the {binding.Name} image applies after its rotation.", "clean aperture after a rotation");

                    crop = ReadCleanAperture(ref reader, crop);
                    break;
                case HeifFormat.Irot:
                    // Anti-clockwise, by multiples of 90 degrees
                    orientation = orientation.Then((reader.ReadByte() & 3) switch
                    {
                        1 => OrientationTransform.ForRotation(RotateMode.Rotate270),
                        2 => OrientationTransform.ForRotation(RotateMode.Rotate180),
                        3 => OrientationTransform.ForRotation(RotateMode.Rotate90),
                        _ => OrientationTransform.Identity,
                    });
                    break;
                case HeifFormat.Imir:
                    // 0 exchanges the top and the bottom of the image, 1 its left and its right
                    orientation = orientation.Then(OrientationTransform.ForFlip((reader.ReadByte() & 1) is 0 ? FlipMode.Vertical : FlipMode.Horizontal));
                    break;
                case HeifFormat.Pasp:
                    if (reader.ReadUInt32() is 0 || reader.ReadUInt32() is 0)
                        throw binding.Invalid("the pixel aspect ratio is not valid.");

                    break;
                default:
                    break;
            }
        }

        Crop = crop;
        Size = new Size(crop.Width, crop.Height);
        Orientation = orientation.ToExifOrientation();
    }

    private void ReadColorInformation(ref HeifBoxReader reader, int offset)
    {
        var binding = _binding;
        var colorType = reader.ReadUInt32();
        if (colorType is HeifFormat.Nclx)
        {
            if (Nclx is not null)
                return;

            var primaries = reader.ReadUInt16();
            var transfer = reader.ReadUInt16();
            var matrix = reader.ReadUInt16();
            var fullRange = (reader.ReadByte() & 0x80) is not 0;
            if (primaries > 255 || transfer > 255 || matrix > 255)
                throw binding.Invalid("a color description code point is out of range.");

            Nclx = new CicpColorDescription((byte)primaries, (byte)transfer, (byte)matrix, fullRange);
        }
        else if (colorType is HeifFormat.Prof or HeifFormat.RICC && IccProfile.Length is 0)
        {
            IccProfile = (offset + reader.Position, reader.Remaining);
        }
    }

    // ISO/IEC 14496-12, section 12.1.4 "Clean Aperture Box": only whole sample rectangles are supported
    /// <summary>Reads a <c>clap</c> box (ISO/IEC 14496-12, section 12.1.4): the part of <paramref name="current"/> that it keeps.</summary>
    /// <exception cref="InvalidImageContentException">The clean aperture is not inside <paramref name="current"/>.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The clean aperture is not a whole number of samples.</exception>
    public static Rectangle ReadCleanAperture(ref HeifBoxReader reader, Rectangle current)
    {
        var binding = reader.Binding;
        long widthN = reader.ReadUInt32();
        long widthD = reader.ReadUInt32();
        long heightN = reader.ReadUInt32();
        long heightD = reader.ReadUInt32();
        long horizontalOffsetN = reader.ReadInt32();
        long horizontalOffsetD = reader.ReadUInt32();
        long verticalOffsetN = reader.ReadInt32();
        long verticalOffsetD = reader.ReadUInt32();
        if (widthD is 0 || heightD is 0 || horizontalOffsetD is 0 || verticalOffsetD is 0 || widthN is 0 || heightN is 0)
            throw binding.Invalid("the clean aperture is not valid.");

        if (widthN % widthD is not 0 || heightN % heightD is not 0)
            throw binding.Unsupported($"The clean aperture of the {binding.Name} image is not a whole number of samples.", "fractional clean aperture");

        var width = widthN / widthD;
        var height = heightN / heightD;

        // The center of the aperture is at (size - 1) / 2 + offset: left = (size - width) / 2 + offset
        var left2N = ((current.Width - width) * horizontalOffsetD) + (2 * horizontalOffsetN);
        var top2N = ((current.Height - height) * verticalOffsetD) + (2 * verticalOffsetN);
        if (left2N % (2 * horizontalOffsetD) is not 0 || top2N % (2 * verticalOffsetD) is not 0)
            throw binding.Unsupported($"The clean aperture of the {binding.Name} image is not a whole number of samples.", "fractional clean aperture");

        var left = left2N / (2 * horizontalOffsetD);
        var top = top2N / (2 * verticalOffsetD);
        if (left < 0 || top < 0 || width > current.Width - left || height > current.Height - top)
            throw binding.Invalid("the clean aperture is outside the image.");

        return new Rectangle(current.X + (int)left, current.Y + (int)top, (int)width, (int)height);
    }
}
