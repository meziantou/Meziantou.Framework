using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The image that an AVIF file presents, resolved from its <c>meta</c> box: the primary item and the items it depends on
/// (the tiles of a grid, the alpha plane, the Exif and XMP metadata), its geometry and its color description. The plan is
/// validated when it is created, before any item data is read: a file that uses a feature that is not supported fails here.
/// </summary>
internal sealed class AvifImagePlan
{
    // The identity derivations that an image may go through: more than any writer nests, and a bound on the work that a
    // file can ask for with a few bytes
    private const int MaxDerivationDepth = 8;

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

    /// <summary>Gets the <c>pasp</c> property of the primary item, or <see langword="null"/>.</summary>
    public PixelAspectRatio? PixelAspectRatio { get; private set; }

    /// <summary>Gets the <c>clli</c> property of the primary item, or <see langword="null"/>.</summary>
    public ContentLightLevel? ContentLightLevel { get; private set; }

    /// <summary>Gets the <c>mdcv</c> property of the primary item, or <see langword="null"/>.</summary>
    public MasteringDisplayColorVolume? MasteringDisplay { get; private set; }

    /// <summary>Gets the Exif item that describes the primary item, or <see langword="null"/>.</summary>
    public AvifItem? Exif { get; private set; }

    /// <summary>Gets the XMP item that describes the primary item, or <see langword="null"/>.</summary>
    public AvifItem? Xmp { get; private set; }

    public bool HasAlpha => Alpha is not null;

    /// <summary>
    /// Gets a value indicating whether the color samples are multiplied by the alpha of their pixel: a <c>prem</c>
    /// reference from the primary item to its alpha plane (ISO/IEC 23008-12, section 6.6.2.2 "Alpha plane").
    /// </summary>
    public bool IsPremultiplied { get; private set; }

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
        var derivations = new List<AvifCodedImage>();
        plan.Color = ResolveImage(meta, payload, primary, limits, derivations);
        ReadTransforms(payload, plan.Color, derivations, out var crop, out var orientation);
        plan.Crop = crop;
        plan.Size = new Size(crop.Width, crop.Height);
        plan.Orientation = orientation.ToExifOrientation();

        // The alpha plane is an auxiliary image of the primary item
        AvifItem? alphaItem = null;
        foreach (var reference in meta.References)
        {
            if (reference.Type is not AvifFormat.Auxl || !reference.ToItemIds.AsSpan().Contains(primaryId) || plan.Alpha is not null)
                continue;

            if (!meta.Items.TryGetValue(reference.FromItemId, out var auxiliary) || !IsAlphaAuxiliary(meta, payload, auxiliary))
                continue;

            alphaItem = auxiliary;
            var alphaDerivations = new List<AvifCodedImage>();
            plan.Alpha = ResolveImage(meta, payload, auxiliary, limits, alphaDerivations);
            if (plan.Alpha.Size != plan.Color.Size)
                throw AvifFormat.Unsupported("The alpha plane of the AVIF image does not have the size of the color image.", "alpha plane of a different size");

            // The alpha plane is presented as the color image: its transformations give the same samples the same places.
            // A coded image may also have an alpha plane that declares none, to which its own transformations apply (the
            // files of libavif before it copied them). That does not extend to a derived image: readers do not agree on
            // the samples of an untransformed alpha plane that a transformed derivation shows (some scale the plane to
            // the size of the derivation), so it must be transformed as the colors are
            var hasAlphaTransforms = plan.Alpha.Transforms.Count is not 0;
            foreach (var derivation in alphaDerivations)
            {
                hasAlphaTransforms |= derivation.Transforms.Count is not 0;
            }

            ReadTransforms(payload, plan.Alpha, alphaDerivations, out var alphaCrop, out var alphaOrientation);
            if ((hasAlphaTransforms || derivations.Count is not 0) && (alphaCrop != crop || alphaOrientation != orientation))
                throw AvifFormat.Unsupported("The alpha plane of the AVIF image is not transformed as the color image.", "alpha plane with other transformations");
        }

        // The colors of the primary item are premultiplied by the alpha plane that it refers to, which is its own
        foreach (var reference in meta.References)
        {
            if (reference.Type is not AvifFormat.Prem || reference.FromItemId != primaryId)
                continue;

            if (alphaItem is null || !reference.ToItemIds.AsSpan().Contains(alphaItem.Id))
                throw AvifFormat.Invalid("the image is premultiplied by an item that is not its alpha plane.");

            plan.IsPremultiplied = true;
        }

        // The properties that describe the image are those of the primary item, then those of the image it is derived from
        plan.ReadDescriptions(meta, payload, primary);
        foreach (var derivation in derivations)
        {
            if (!ReferenceEquals(derivation.Item, primary))
                plan.ReadDescriptions(meta, payload, derivation.Item);
        }

        if (!ReferenceEquals(plan.Color.Item, primary))
            plan.ReadDescriptions(meta, payload, plan.Color.Item);

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

    /// <summary>
    /// Follows the identity derivations of an item to the coded image they present (ISO/IEC 23008-12, section 6.6.2.1): an
    /// <c>iden</c> item has no data, and is its one input image with the transformative properties of the item applied
    /// after those of the input.
    /// </summary>
    /// <param name="meta">The parsed box.</param>
    /// <param name="payload">The payload of the box.</param>
    /// <param name="item">The item that is presented.</param>
    /// <param name="limits">The resource limits of the operation.</param>
    /// <param name="derivations">Receives the identity derivations, from <paramref name="item"/> to the one whose input is the coded image.</param>
    /// <returns>The coded image: an AV1 item or a grid of AV1 items.</returns>
    private static AvifCodedImage ResolveImage(AvifMetaBox meta, ReadOnlySpan<byte> payload, AvifItem item, ImageResourceLimits limits, List<AvifCodedImage> derivations)
    {
        while (item.Type is AvifFormat.Iden)
        {
            foreach (var derivation in derivations)
            {
                if (ReferenceEquals(derivation.Item, item))
                    throw AvifFormat.Invalid("a derived image is derived from itself.");
            }

            if (derivations.Count == MaxDerivationDepth)
                throw AvifFormat.Unsupported($"The AVIF image is derived through more than {MaxDerivationDepth} derivations.", "derivation depth");

            derivations.Add(AvifCodedImage.CreateIdentityDerivation(meta, payload, item, limits));
            AvifItemReference? reference = null;
            foreach (var candidate in meta.References)
            {
                if (candidate.Type is AvifFormat.Dimg && candidate.FromItemId == item.Id)
                {
                    if (reference is not null)
                        throw AvifFormat.Invalid("a derived image has two lists of input images.");

                    reference = candidate;
                }
            }

            if (reference is null || reference.ToItemIds.Length is not 1)
                throw AvifFormat.Invalid("an identity derivation does not have one input image.");

            if (!meta.Items.TryGetValue(reference.ToItemIds[0], out var input) || input.Type is 0)
                throw AvifFormat.Invalid("the input image of a derived image does not exist.");

            item = input;
        }

        return AvifCodedImage.Create(meta, payload, item, limits);
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

    // The transformative properties apply in the order of their association (ISO/IEC 23008-12, section 6.5): MIAF orders
    // them clap, irot, imir, and the files that do otherwise are read as they are written. Those of the coded image apply
    // first, then those of each identity derivation, from the one that has the coded image for input to the item that is
    // presented. The pixels stay as they are coded: what is computed is the part of the coded image that is presented,
    // and the orientation that presents it
    private static void ReadTransforms(ReadOnlySpan<byte> payload, AvifCodedImage coded, List<AvifCodedImage> derivations, out Rectangle crop, out OrientationTransform orientation)
    {
        crop = new Rectangle(0, 0, coded.Size.Width, coded.Size.Height);
        orientation = OrientationTransform.Identity;
        for (var i = derivations.Count; i >= 0; i--)
        {
            var image = i == derivations.Count ? coded : derivations[i];
            // The size of a derived image is the size of its input, as the input is presented
            if (i < derivations.Count && image.Size != orientation.GetOutputSize(new Size(crop.Width, crop.Height)))
                throw AvifFormat.Invalid("the size of an identity derivation is not the size of its input image.");

            foreach (var (type, offset, length) in image.Transforms)
            {
                var reader = new AvifBoxReader(payload.Slice(offset, length));
                switch (type)
                {
                    case AvifFormat.Clap:
                    {
                        // The aperture is a part of the image as the transformations before it present it. The pixels stay
                        // as they are coded: the aperture selects the samples that it shows, and the orientation is unchanged
                        var presented = orientation.GetOutputSize(new Size(crop.Width, crop.Height));
                        var aperture = orientation.MapRectangleToSource(new Size(crop.Width, crop.Height), AvifCleanAperture.Read(ref reader, presented));
                        crop = new Rectangle(crop.X + aperture.X, crop.Y + aperture.Y, aperture.Width, aperture.Height);
                        break;
                    }

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
                    default:
                        // imir: 0 exchanges the top and the bottom of the image, 1 its left and its right
                        orientation = orientation.Then(OrientationTransform.ForFlip((reader.ReadByte() & 1) is 0 ? FlipMode.Vertical : FlipMode.Horizontal));
                        break;
                }
            }
        }
    }

    // The properties that describe an image without changing its samples: the first one of each type, on the first item
    // that has one
    private void ReadDescriptions(AvifMetaBox meta, ReadOnlySpan<byte> payload, AvifItem item)
    {
        foreach (var (index, _) in item.Properties)
        {
            var (type, offset, length) = meta.Properties[index - 1];
            var reader = new AvifBoxReader(payload.Slice(offset, length));
            switch (type)
            {
                case AvifFormat.Colr:
                    ReadColorInformation(ref reader, offset);
                    break;
                case AvifFormat.Pasp:
                    var pixelAspectRatio = AvifPresentationBoxes.ReadPixelAspectRatio(ref reader);
                    PixelAspectRatio ??= pixelAspectRatio;
                    break;
                case AvifFormat.Clli:
                    ContentLightLevel ??= AvifPresentationBoxes.ReadContentLightLevel(ref reader);
                    break;
                case AvifFormat.Mdcv:
                    MasteringDisplay ??= AvifPresentationBoxes.ReadMasteringDisplay(ref reader);
                    break;
                default:
                    break;
            }
        }
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
}
