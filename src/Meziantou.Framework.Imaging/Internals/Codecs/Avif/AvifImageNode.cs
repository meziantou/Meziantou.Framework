using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// An image item of an AVIF file that is built from other images (ISO/IEC 23008-12, section 6.6 "Derived images"), as a
/// tree: an identity derivation, a grid or an overlay, whose inputs are coded images or other derivations. Every item
/// applies its own transformative properties (<c>clap</c>, <c>irot</c>, <c>imir</c>) to the image it reconstructs.
/// </summary>
/// <remarks>
/// The tree is built when the <c>meta</c> box is read, before any item data: a derivation that refers to itself is not
/// valid, and a tree deeper than <see cref="MaxDepth"/> is not supported. An item that several derivations use is a
/// node of each of them: it is decoded once for each use.
/// </remarks>
internal sealed class AvifImageNode
{
    /// <summary>The number of derivations between the image that is presented and a coded image: more than any writer nests.</summary>
    public const int MaxDepth = 8;

    /// <summary>The number of input images of a derivation that are read: a bound on the work that a few bytes can ask for.</summary>
    public const int MaxInputs = 1024;

    private AvifImageNode(AvifImageNodeKind kind, AvifCodedImage image, AvifImageNode[] inputs)
    {
        Kind = kind;
        Image = image;
        Inputs = inputs;
    }

    public AvifImageNodeKind Kind { get; }

    /// <summary>Gets the item: its size, its transformative properties and, for a coded image, its coded items.</summary>
    public AvifCodedImage Image { get; }

    /// <summary>Gets the alpha plane of a coded image, or <see langword="null"/>.</summary>
    public AvifCodedImage? Alpha { get; private set; }

    /// <summary>Gets a value indicating whether the colors of a coded image are multiplied by its alpha plane.</summary>
    public bool IsPremultiplied { get; private set; }

    /// <summary>Gets the <c>colr</c> property of type <c>nclx</c> of the item, or <see langword="null"/>.</summary>
    public CicpColorDescription? Nclx { get; private set; }

    /// <summary>Gets the position of the ICC profile of the <c>colr</c> property of the item in the <c>meta</c> box; its length is zero when the item has none.</summary>
    public (int Offset, int Length) IccProfile { get; private set; }

    /// <summary>Gets the input images of a derivation, in the order of its <c>dimg</c> reference.</summary>
    public AvifImageNode[] Inputs { get; }

    /// <summary>
    /// Gets the transformative properties of the item, in the order they apply to the image it reconstructs: a rectangle
    /// to keep (<c>clap</c>), or a rotation or a mirroring (<c>irot</c>, <c>imir</c>).
    /// </summary>
    public List<(Rectangle? Crop, OrientationTransform Orientation)> Operations { get; } = [];

    /// <summary>Gets the size of the image once its transformative properties are applied.</summary>
    public Size OutputSize { get; private set; }

    /// <summary>Gets the number of columns of tiles of a grid; 0 before its descriptor is read.</summary>
    public int Columns { get; private set; }

    /// <summary>
    /// Gets the color that fills the canvas of an overlay before its input images are composited: red, green, blue and
    /// alpha, 16 bits each, in the sRGB color space.
    /// </summary>
    public ushort[] Fill { get; } = new ushort[4];

    /// <summary>Gets the offset of the top-left corner of each input image of an overlay on its canvas, which it may exceed.</summary>
    public (int X, int Y)[] Offsets { get; private set; } = [];

    /// <summary>
    /// Reads the descriptor of a grid or of an overlay, which is the data of its item (ISO/IEC 23008-12, sections
    /// 6.6.2.3.2 and 6.6.2.2.2), and verifies it against the item and its input images.
    /// </summary>
    public void ReadDescriptor(ReadOnlySpan<byte> data)
    {
        var reader = new AvifBoxReader(data);
        var size = Image.Size;
        if (Kind is AvifImageNodeKind.Grid)
        {
            if (reader.ReadByte() is not 0)
                throw AvifFormat.Unsupported("The version of the AVIF grid is not supported.", "grid version");

            var isLarge = (reader.ReadByte() & 1) is not 0;
            var rows = reader.ReadByte() + 1;
            Columns = reader.ReadByte() + 1;
            long width = isLarge ? reader.ReadUInt32() : reader.ReadUInt16();
            long height = isLarge ? reader.ReadUInt32() : reader.ReadUInt16();
            if (width != size.Width || height != size.Height)
                throw AvifFormat.Invalid("the size of a grid does not match its 'ispe' property.");

            if (Inputs.Length != rows * Columns)
                throw AvifFormat.Invalid("a grid does not have one tile per cell.");

            // Every tile shows at least one sample of the image
            var tile = Inputs[0].OutputSize;
            if ((long)(Columns - 1) * tile.Width >= width || width > (long)Columns * tile.Width || (long)(rows - 1) * tile.Height >= height || height > (long)rows * tile.Height)
                throw AvifFormat.Invalid("the tiles of a grid do not cover the image.");
        }
        else
        {
            // version, flags, the fill color, the size of the canvas, then the offset of each input image; the sizes and
            // the offsets are 16-bit fields, or 32-bit ones (flags and 1)
            if (reader.ReadByte() is not 0)
                throw AvifFormat.Unsupported("The version of the AVIF overlay is not supported.", "overlay version");

            var isLarge = (reader.ReadByte() & 1) is not 0;
            for (var i = 0; i < Fill.Length; i++)
            {
                Fill[i] = reader.ReadUInt16();
            }

            long width = isLarge ? reader.ReadUInt32() : reader.ReadUInt16();
            long height = isLarge ? reader.ReadUInt32() : reader.ReadUInt16();
            if (width != size.Width || height != size.Height)
                throw AvifFormat.Invalid("the size of an overlay does not match its 'ispe' property.");

            Offsets = new (int X, int Y)[Inputs.Length];
            for (var i = 0; i < Offsets.Length; i++)
            {
                Offsets[i] = isLarge ? (reader.ReadInt32(), reader.ReadInt32()) : (unchecked((short)reader.ReadUInt16()), unchecked((short)reader.ReadUInt16()));
            }
        }
    }

    /// <summary>Returns whether an item is presented without building an image: a coded image, through identity derivations or not.</summary>
    public static bool IsDirect(AvifMetaBox meta, AvifItem item)
    {
        for (var depth = 0; depth <= MaxDepth && item.Type is AvifFormat.Iden; depth++)
        {
            if (FindInputs(meta, item) is not { ToItemIds.Length: 1 } reference || !meta.Items.TryGetValue(reference.ToItemIds[0], out var input))
                return true;

            item = input;
        }

        return item.Type is not (AvifFormat.Iovl or AvifFormat.Grid) || (item.Type is AvifFormat.Grid && IsCodedGrid(meta, item));
    }

    /// <summary>Builds the tree of an item.</summary>
    /// <exception cref="InvalidImageContentException">The description of the image is inconsistent.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The image uses a feature that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">The image exceeds a resource limit.</exception>
    public static AvifImageNode Create(AvifMetaBox meta, ReadOnlySpan<byte> payload, AvifItem item, ImageResourceLimits limits) => Create(meta, payload, item, limits, []);

    private static AvifImageNode Create(AvifMetaBox meta, ReadOnlySpan<byte> payload, AvifItem item, ImageResourceLimits limits, List<AvifItem> ancestors)
    {
        foreach (var ancestor in ancestors)
        {
            if (ReferenceEquals(ancestor, item))
                throw AvifFormat.Invalid("a derived image is derived from itself.");
        }

        AvifImageNode node;
        if (item.Type is AvifFormat.Av01 || (item.Type is AvifFormat.Grid && IsCodedGrid(meta, item)))
        {
            node = new AvifImageNode(AvifImageNodeKind.Coded, AvifCodedImage.Create(meta, payload, item, limits), []);
            node.ReadAlpha(meta, payload, limits);
        }
        else if (item.Type is AvifFormat.Iden or AvifFormat.Grid or AvifFormat.Iovl)
        {
            if (ancestors.Count == MaxDepth)
                throw AvifFormat.Unsupported($"The AVIF image is derived through more than {MaxDepth} derivations.", "derivation depth");

            var reference = FindInputs(meta, item);
            if (reference is null || reference.ToItemIds.Length is 0)
                throw AvifFormat.Invalid("a derived image has no input image.");

            if (item.Type is AvifFormat.Iden && reference.ToItemIds.Length is not 1)
                throw AvifFormat.Invalid("an identity derivation does not have one input image.");

            if (reference.ToItemIds.Length > MaxInputs)
                throw AvifFormat.Unsupported($"A derived image of the AVIF file has more than {MaxInputs} input images.", "derived image input count");

            // The canvas of a grid and of an overlay is allocated: its size is checked against the limits. An identity
            // derivation allocates nothing of its own
            var image = AvifCodedImage.CreateDerivation(meta, payload, item, limits, enforceLimits: item.Type is not AvifFormat.Iden);
            var inputs = new AvifImageNode[reference.ToItemIds.Length];
            ancestors.Add(item);
            for (var i = 0; i < inputs.Length; i++)
            {
                if (!meta.Items.TryGetValue(reference.ToItemIds[i], out var input) || input.Type is 0)
                    throw AvifFormat.Invalid("the input image of a derived image does not exist.");

                inputs[i] = Create(meta, payload, input, limits, ancestors);
            }

            ancestors.RemoveAt(ancestors.Count - 1);
            node = new AvifImageNode(item.Type switch { AvifFormat.Iden => AvifImageNodeKind.Identity, AvifFormat.Grid => AvifImageNodeKind.Grid, _ => AvifImageNodeKind.Overlay }, image, inputs);

            // The alpha plane of an image that is built is the one of its inputs: an auxiliary image of the derivation
            // would have to be built the same way
            foreach (var candidate in meta.References)
            {
                if (candidate.Type is AvifFormat.Auxl && candidate.ToItemIds.AsSpan().Contains(item.Id) && meta.Items.TryGetValue(candidate.FromItemId, out var auxiliary) && AvifImagePlan.IsAlphaAuxiliary(meta, payload, auxiliary))
                    throw AvifFormat.Unsupported("A derived image of the AVIF file has an alpha plane of its own.", "alpha plane of a derived image");
            }
        }
        else
        {
            throw AvifFormat.Unsupported($"An image of the AVIF file is coded as '{AvifFormat.ToText(item.Type)}', not as AV1.", "image codec");
        }

        node.ReadColorDescription(meta, payload);
        node.ReadOperations(payload);
        if (node.Kind is AvifImageNodeKind.Identity && node.Inputs[0].OutputSize != node.Image.Size)
            throw AvifFormat.Invalid("the size of an identity derivation is not the size of its input image.");

        if (node.Kind is AvifImageNodeKind.Grid)
        {
            foreach (var input in node.Inputs)
            {
                if (input.OutputSize != node.Inputs[0].OutputSize)
                    throw AvifFormat.Invalid("the tiles of a grid do not have the same size.");
            }
        }

        return node;
    }

    /// <summary>Visits the coded images of the tree, in the order they are rendered.</summary>
    public void ForEachCoded(Action<AvifImageNode> action)
    {
        if (Kind is AvifImageNodeKind.Coded)
        {
            action(this);
            return;
        }

        foreach (var input in Inputs)
        {
            input.ForEachCoded(action);
        }
    }

    /// <summary>Visits the images of the tree: an image, then its input images.</summary>
    public void ForEach(Action<AvifImageNode> action)
    {
        action(this);
        foreach (var input in Inputs)
        {
            input.ForEach(action);
        }
    }

    /// <summary>Visits the derivations of the tree whose item has data: the grids and the overlays.</summary>
    public void ForEachDescriptor(Action<AvifImageNode> action)
    {
        if (Kind is AvifImageNodeKind.Grid or AvifImageNodeKind.Overlay)
            action(this);

        foreach (var input in Inputs)
        {
            input.ForEachDescriptor(action);
        }
    }

    private static AvifItemReference? FindInputs(AvifMetaBox meta, AvifItem item)
    {
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

        return reference;
    }

    // A grid whose tiles are all AV1 items is decoded as one coded image: its planes are tiled before they are converted
    private static bool IsCodedGrid(AvifMetaBox meta, AvifItem item)
    {
        if (FindInputs(meta, item) is not { } reference)
            return true;

        foreach (var id in reference.ToItemIds)
        {
            if (meta.Items.TryGetValue(id, out var tile) && tile.Type is AvifFormat.Iden or AvifFormat.Grid or AvifFormat.Iovl)
                return false;
        }

        return true;
    }

    // The alpha plane of a coded image is its auxiliary image: a coded image of the same size, transformed as it is or not
    // at all
    private void ReadAlpha(AvifMetaBox meta, ReadOnlySpan<byte> payload, ImageResourceLimits limits)
    {
        var id = Image.Item.Id;
        foreach (var reference in meta.References)
        {
            if (reference.Type is not AvifFormat.Auxl || !reference.ToItemIds.AsSpan().Contains(id) || Alpha is not null)
                continue;

            if (!meta.Items.TryGetValue(reference.FromItemId, out var auxiliary) || !AvifImagePlan.IsAlphaAuxiliary(meta, payload, auxiliary))
                continue;

            if (auxiliary.Type is not (AvifFormat.Av01 or AvifFormat.Grid) || !IsCodedGrid(meta, auxiliary))
                throw AvifFormat.Unsupported("The alpha plane of an input image of the AVIF file is a derived image.", "alpha plane of a derived image");

            Alpha = AvifCodedImage.Create(meta, payload, auxiliary, limits);
            if (Alpha.Size != Image.Size)
                throw AvifFormat.Unsupported("The alpha plane of the AVIF image does not have the size of the color image.", "alpha plane of a different size");

            if (Alpha.Transforms.Count is not 0 && !AvifCodedImage.HasSameTransforms(Image, Alpha, payload))
                throw AvifFormat.Unsupported("The alpha plane of the AVIF image is not transformed as the color image.", "alpha plane with other transformations");
        }

        foreach (var reference in meta.References)
        {
            if (reference.Type is not AvifFormat.Prem || reference.FromItemId != id)
                continue;

            if (Alpha is not { } alpha || !reference.ToItemIds.AsSpan().Contains(alpha.Item.Id))
                throw AvifFormat.Invalid("the image is premultiplied by an item that is not its alpha plane.");

            IsPremultiplied = true;
        }
    }

    // The transformative properties apply in the order of their association, each to the image that the previous ones give
    private void ReadOperations(ReadOnlySpan<byte> payload)
    {
        var size = Image.Size;
        foreach (var (type, offset, length) in Image.Transforms)
        {
            var reader = new AvifBoxReader(payload.Slice(offset, length));
            if (type is AvifFormat.Clap)
            {
                var aperture = AvifCleanAperture.Read(ref reader, size);
                Operations.Add((aperture, OrientationTransform.Identity));
                size = new Size(aperture.Width, aperture.Height);
                continue;
            }

            // irot: anti-clockwise, by multiples of 90 degrees; imir: 0 exchanges the top and the bottom, 1 the left and the right
            var orientation = type is AvifFormat.Irot
                ? (reader.ReadByte() & 3) switch
                {
                    1 => OrientationTransform.ForRotation(RotateMode.Rotate270),
                    2 => OrientationTransform.ForRotation(RotateMode.Rotate180),
                    3 => OrientationTransform.ForRotation(RotateMode.Rotate90),
                    _ => OrientationTransform.Identity,
                }
                : OrientationTransform.ForFlip((reader.ReadByte() & 1) is 0 ? FlipMode.Vertical : FlipMode.Horizontal);
            Operations.Add((null, orientation));
            size = orientation.GetOutputSize(size);
        }

        OutputSize = size;
    }

    private void ReadColorDescription(AvifMetaBox meta, ReadOnlySpan<byte> payload)
    {
        foreach (var (index, _) in Image.Item.Properties)
        {
            var (type, offset, length) = meta.Properties[index - 1];
            if (type is not AvifFormat.Colr)
                continue;

            var reader = new AvifBoxReader(payload.Slice(offset, length));
            var colorType = reader.ReadUInt32();
            if (colorType is AvifFormat.Prof or AvifFormat.RICC && IccProfile.Length is 0)
                IccProfile = (offset + reader.Position, reader.Remaining);

            if (colorType is not AvifFormat.Nclx || Nclx is not null)
                continue;

            var primaries = reader.ReadUInt16();
            var transfer = reader.ReadUInt16();
            var matrix = reader.ReadUInt16();
            var fullRange = (reader.ReadByte() & 0x80) is not 0;
            if (primaries > 255 || transfer > 255 || matrix > 255)
                throw AvifFormat.Invalid("a color description code point is out of range.");

            Nclx = new CicpColorDescription((byte)primaries, (byte)transfer, (byte)matrix, fullRange);
            if (Kind is AvifImageNodeKind.Coded)
                AvifYuvConverter.EnsureSupported(matrix, Image.IsMonochrome, Image.SubsamplingX, Image.SubsamplingY, primaries, transfer);
        }
    }
}
