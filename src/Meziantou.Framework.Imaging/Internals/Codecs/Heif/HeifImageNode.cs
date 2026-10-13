using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// An image item of a file of the HEIF family that is built from other images (ISO/IEC 23008-12, section 6.6 "Derived images"), as a
/// tree: an identity derivation, a grid or an overlay, whose inputs are coded images or other derivations. Every item
/// applies its own transformative properties (<c>clap</c>, <c>irot</c>, <c>imir</c>) to the image it reconstructs.
/// </summary>
/// <remarks>
/// The tree is built when the <c>meta</c> box is read, before any item data: a derivation that refers to itself is not
/// valid, and a tree deeper than <see cref="MaxDepth"/> is not supported. An item that several derivations use is a
/// node of each of them: it is decoded once for each use.
/// </remarks>
internal sealed class HeifImageNode
{
    /// <summary>The number of derivations between the image that is presented and a coded image: more than any writer nests.</summary>
    public const int MaxDepth = 8;

    /// <summary>The number of input images of a derivation that are read: a bound on the work that a few bytes can ask for.</summary>
    public const int MaxInputs = 1024;

    private HeifImageNode(HeifImageNodeKind kind, HeifCodedImage image, HeifImageNode[] inputs)
    {
        Kind = kind;
        Image = image;
        Inputs = inputs;
    }

    public HeifImageNodeKind Kind { get; }

    /// <summary>Gets the item: its size, its transformative properties and, for a coded image, its coded items.</summary>
    public HeifCodedImage Image { get; }

    /// <summary>Gets the alpha plane of a coded image, or <see langword="null"/>.</summary>
    public HeifCodedImage? Alpha { get; private set; }

    /// <summary>Gets a value indicating whether the colors of a coded image are multiplied by its alpha plane.</summary>
    public bool IsPremultiplied { get; private set; }

    /// <summary>Gets the <c>colr</c> property of type <c>nclx</c> of the item, or <see langword="null"/>.</summary>
    public CicpColorDescription? Nclx { get; private set; }

    /// <summary>Gets the position of the ICC profile of the <c>colr</c> property of the item in the <c>meta</c> box; its length is zero when the item has none.</summary>
    public (int Offset, int Length) IccProfile { get; private set; }

    /// <summary>Gets the input images of a derivation, in the order of its <c>dimg</c> reference.</summary>
    public HeifImageNode[] Inputs { get; }

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
        var binding = Image.Binding;
        var reader = new HeifBoxReader(data, binding);
        var size = Image.Size;
        if (Kind is HeifImageNodeKind.Grid)
        {
            if (reader.ReadByte() is not 0)
                throw binding.Unsupported($"The version of the {binding.Name} grid is not supported.", "grid version");

            var isLarge = (reader.ReadByte() & 1) is not 0;
            var rows = reader.ReadByte() + 1;
            Columns = reader.ReadByte() + 1;
            long width = isLarge ? reader.ReadUInt32() : reader.ReadUInt16();
            long height = isLarge ? reader.ReadUInt32() : reader.ReadUInt16();
            if (width != size.Width || height != size.Height)
                throw binding.Invalid("the size of a grid does not match its 'ispe' property.");

            if (Inputs.Length != rows * Columns)
                throw binding.Invalid("a grid does not have one tile per cell.");

            // Every tile shows at least one sample of the image
            var tile = Inputs[0].OutputSize;
            if ((long)(Columns - 1) * tile.Width >= width || width > (long)Columns * tile.Width || (long)(rows - 1) * tile.Height >= height || height > (long)rows * tile.Height)
                throw binding.Invalid("the tiles of a grid do not cover the image.");
        }
        else
        {
            // version, flags, the fill color, the size of the canvas, then the offset of each input image; the sizes and
            // the offsets are 16-bit fields, or 32-bit ones (flags and 1)
            if (reader.ReadByte() is not 0)
                throw binding.Unsupported($"The version of the {binding.Name} overlay is not supported.", "overlay version");

            var isLarge = (reader.ReadByte() & 1) is not 0;
            for (var i = 0; i < Fill.Length; i++)
            {
                Fill[i] = reader.ReadUInt16();
            }

            long width = isLarge ? reader.ReadUInt32() : reader.ReadUInt16();
            long height = isLarge ? reader.ReadUInt32() : reader.ReadUInt16();
            if (width != size.Width || height != size.Height)
                throw binding.Invalid("the size of an overlay does not match its 'ispe' property.");

            Offsets = new (int X, int Y)[Inputs.Length];
            for (var i = 0; i < Offsets.Length; i++)
            {
                Offsets[i] = isLarge ? (reader.ReadInt32(), reader.ReadInt32()) : (unchecked((short)reader.ReadUInt16()), unchecked((short)reader.ReadUInt16()));
            }
        }
    }

    /// <summary>Returns whether an item is presented without building an image: a coded image, through identity derivations or not.</summary>
    public static bool IsDirect(HeifCodecBinding binding, HeifMetaBox meta, HeifItem item)
    {
        for (var depth = 0; depth <= MaxDepth && item.Type is HeifFormat.Iden; depth++)
        {
            if (FindInputs(binding, meta, item) is not { ToItemIds.Length: 1 } reference || !meta.Items.TryGetValue(reference.ToItemIds[0], out var input))
                return true;

            item = input;
        }

        return item.Type is not (HeifFormat.Iovl or HeifFormat.Grid) || (item.Type is HeifFormat.Grid && IsCodedGrid(binding, meta, item));
    }

    /// <summary>Builds the tree of an item.</summary>
    /// <exception cref="InvalidImageContentException">The description of the image is inconsistent.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The image uses a feature that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">The image exceeds a resource limit.</exception>
    public static HeifImageNode Create(HeifCodecBinding binding, HeifMetaBox meta, ReadOnlySpan<byte> payload, HeifItem item, ImageResourceLimits limits) => Create(binding, meta, payload, item, limits, []);

    private static HeifImageNode Create(HeifCodecBinding binding, HeifMetaBox meta, ReadOnlySpan<byte> payload, HeifItem item, ImageResourceLimits limits, List<HeifItem> ancestors)
    {
        foreach (var ancestor in ancestors)
        {
            if (ReferenceEquals(ancestor, item))
                throw binding.Invalid("a derived image is derived from itself.");
        }

        HeifImageNode node;
        if (binding.IsCodedItemType(item.Type) || (item.Type is HeifFormat.Grid && IsCodedGrid(binding, meta, item)))
        {
            node = new HeifImageNode(HeifImageNodeKind.Coded, HeifCodedImage.Create(binding, meta, payload, item, limits), []);
            node.ReadAlpha(meta, payload, limits);
        }
        else if (item.Type is HeifFormat.Iden or HeifFormat.Grid or HeifFormat.Iovl)
        {
            if (ancestors.Count == MaxDepth)
                throw binding.Unsupported($"The {binding.Name} image is derived through more than {MaxDepth} derivations.", "derivation depth");

            var reference = FindInputs(binding, meta, item);
            if (reference is null || reference.ToItemIds.Length is 0)
                throw binding.Invalid("a derived image has no input image.");

            if (item.Type is HeifFormat.Iden && reference.ToItemIds.Length is not 1)
                throw binding.Invalid("an identity derivation does not have one input image.");

            if (reference.ToItemIds.Length > MaxInputs)
                throw binding.Unsupported($"A derived image of the {binding.Name} file has more than {MaxInputs} input images.", "derived image input count");

            // The canvas of a grid and of an overlay is allocated: its size is checked against the limits. An identity
            // derivation allocates nothing of its own
            var image = HeifCodedImage.CreateDerivation(binding, meta, payload, item, limits, enforceLimits: item.Type is not HeifFormat.Iden);
            var inputs = new HeifImageNode[reference.ToItemIds.Length];
            ancestors.Add(item);
            for (var i = 0; i < inputs.Length; i++)
            {
                if (!meta.Items.TryGetValue(reference.ToItemIds[i], out var input) || input.Type is 0)
                    throw binding.Invalid("the input image of a derived image does not exist.");

                inputs[i] = Create(binding, meta, payload, input, limits, ancestors);
            }

            ancestors.RemoveAt(ancestors.Count - 1);
            node = new HeifImageNode(item.Type switch { HeifFormat.Iden => HeifImageNodeKind.Identity, HeifFormat.Grid => HeifImageNodeKind.Grid, _ => HeifImageNodeKind.Overlay }, image, inputs);

            // The alpha plane of an image that is built is the one of its inputs: an auxiliary image of the derivation
            // would have to be built the same way
            foreach (var candidate in meta.References)
            {
                if (candidate.Type is HeifFormat.Auxl && candidate.ToItemIds.AsSpan().Contains(item.Id) && meta.Items.TryGetValue(candidate.FromItemId, out var auxiliary) && HeifImagePlan.IsAlphaAuxiliary(binding, meta, payload, auxiliary))
                    throw binding.Unsupported($"A derived image of the {binding.Name} file has an alpha plane of its own.", "alpha plane of a derived image");
            }
        }
        else
        {
            throw binding.Unsupported($"An image of the {binding.Name} file is coded as '{HeifFormat.ToText(item.Type)}', not as {binding.CodecName}.", "image codec");
        }

        node.ReadColorDescription(meta, payload);
        node.ReadOperations(payload);
        if (node.Kind is HeifImageNodeKind.Identity && node.Inputs[0].OutputSize != node.Image.Size)
            throw binding.Invalid("the size of an identity derivation is not the size of its input image.");

        if (node.Kind is HeifImageNodeKind.Grid)
        {
            foreach (var input in node.Inputs)
            {
                if (input.OutputSize != node.Inputs[0].OutputSize)
                    throw binding.Invalid("the tiles of a grid do not have the same size.");
            }
        }

        return node;
    }

    /// <summary>Visits the coded images of the tree, in the order they are rendered.</summary>
    public void ForEachCoded(Action<HeifImageNode> action)
    {
        if (Kind is HeifImageNodeKind.Coded)
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
    public void ForEach(Action<HeifImageNode> action)
    {
        action(this);
        foreach (var input in Inputs)
        {
            input.ForEach(action);
        }
    }

    /// <summary>Visits the derivations of the tree whose item has data: the grids and the overlays.</summary>
    public void ForEachDescriptor(Action<HeifImageNode> action)
    {
        if (Kind is HeifImageNodeKind.Grid or HeifImageNodeKind.Overlay)
            action(this);

        foreach (var input in Inputs)
        {
            input.ForEachDescriptor(action);
        }
    }

    private static HeifItemReference? FindInputs(HeifCodecBinding binding, HeifMetaBox meta, HeifItem item)
    {
        HeifItemReference? reference = null;
        foreach (var candidate in meta.References)
        {
            if (candidate.Type is HeifFormat.Dimg && candidate.FromItemId == item.Id)
            {
                if (reference is not null)
                    throw binding.Invalid("a derived image has two lists of input images.");

                reference = candidate;
            }
        }

        return reference;
    }

    // A grid whose tiles are all coded items is decoded as one coded image: its planes are tiled before they are converted
    private static bool IsCodedGrid(HeifCodecBinding binding, HeifMetaBox meta, HeifItem item)
    {
        if (FindInputs(binding, meta, item) is not { } reference)
            return true;

        foreach (var id in reference.ToItemIds)
        {
            if (meta.Items.TryGetValue(id, out var tile) && tile.Type is HeifFormat.Iden or HeifFormat.Grid or HeifFormat.Iovl)
                return false;
        }

        return true;
    }

    // The alpha plane of a coded image is its auxiliary image: a coded image of the same size, transformed as it is or not
    // at all
    private void ReadAlpha(HeifMetaBox meta, ReadOnlySpan<byte> payload, ImageResourceLimits limits)
    {
        var binding = Image.Binding;
        var id = Image.Item.Id;
        foreach (var reference in meta.References)
        {
            if (reference.Type is not HeifFormat.Auxl || !reference.ToItemIds.AsSpan().Contains(id) || Alpha is not null)
                continue;

            if (!meta.Items.TryGetValue(reference.FromItemId, out var auxiliary) || !HeifImagePlan.IsAlphaAuxiliary(binding, meta, payload, auxiliary))
                continue;

            if (!(binding.IsCodedItemType(auxiliary.Type) || auxiliary.Type is HeifFormat.Grid) || !IsCodedGrid(binding, meta, auxiliary))
                throw binding.Unsupported($"The alpha plane of an input image of the {binding.Name} file is a derived image.", "alpha plane of a derived image");

            Alpha = HeifCodedImage.Create(binding, meta, payload, auxiliary, limits);
            if (Alpha.Size != Image.Size)
                throw binding.Unsupported($"The alpha plane of the {binding.Name} image does not have the size of the color image.", "alpha plane of a different size");

            if (Alpha.Transforms.Count is not 0 && !HeifCodedImage.HasSameTransforms(Image, Alpha, payload))
                throw binding.Unsupported($"The alpha plane of the {binding.Name} image is not transformed as the color image.", "alpha plane with other transformations");
        }

        foreach (var reference in meta.References)
        {
            if (reference.Type is not HeifFormat.Prem || reference.FromItemId != id)
                continue;

            if (Alpha is not { } alpha || !reference.ToItemIds.AsSpan().Contains(alpha.Item.Id))
                throw binding.Invalid("the image is premultiplied by an item that is not its alpha plane.");

            IsPremultiplied = true;
        }
    }

    // The transformative properties apply in the order of their association, each to the image that the previous ones give
    private void ReadOperations(ReadOnlySpan<byte> payload)
    {
        var binding = Image.Binding;
        var size = Image.Size;
        foreach (var (type, offset, length) in Image.Transforms)
        {
            var reader = new HeifBoxReader(payload.Slice(offset, length), binding);
            if (type is HeifFormat.Clap)
            {
                var aperture = HeifCleanAperture.Read(ref reader, size);
                Operations.Add((aperture, OrientationTransform.Identity));
                size = new Size(aperture.Width, aperture.Height);
                continue;
            }

            // irot: anti-clockwise, by multiples of 90 degrees; imir: 0 exchanges the top and the bottom, 1 the left and the right
            var orientation = type is HeifFormat.Irot
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

    private void ReadColorDescription(HeifMetaBox meta, ReadOnlySpan<byte> payload)
    {
        var binding = Image.Binding;
        foreach (var (index, _) in Image.Item.Properties)
        {
            var (type, offset, length) = meta.Properties[index - 1];
            if (type is not HeifFormat.Colr)
                continue;

            var reader = new HeifBoxReader(payload.Slice(offset, length), binding);
            var colorType = reader.ReadUInt32();
            if (colorType is HeifFormat.Prof or HeifFormat.RICC && IccProfile.Length is 0)
                IccProfile = (offset + reader.Position, reader.Remaining);

            if (colorType is not HeifFormat.Nclx || Nclx is not null)
                continue;

            var primaries = reader.ReadUInt16();
            var transfer = reader.ReadUInt16();
            var matrix = reader.ReadUInt16();
            var fullRange = (reader.ReadByte() & 0x80) is not 0;
            if (primaries > 255 || transfer > 255 || matrix > 255)
                throw binding.Invalid("a color description code point is out of range.");

            Nclx = new CicpColorDescription((byte)primaries, (byte)transfer, (byte)matrix, fullRange);
            if (Kind is HeifImageNodeKind.Coded)
                HeifYuvConverter.EnsureSupported(binding, matrix, Image.IsMonochrome, Image.SubsamplingX, Image.SubsamplingY, primaries, transfer);
        }
    }
}
