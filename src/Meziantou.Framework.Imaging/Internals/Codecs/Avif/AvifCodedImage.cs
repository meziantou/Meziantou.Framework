namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// An image of an AVIF file that is coded with AV1: an <c>av01</c> item, or a <c>grid</c> item whose tiles are <c>av01</c>
/// items (ISO/IEC 23008-12, section 6.6.2.3). The sample format comes from the <c>av1C</c> property of the coded items, which
/// their bitstream must match.
/// </summary>
internal sealed class AvifCodedImage
{
    private AvifCodedImage(AvifItem item, AvifItem[] tiles)
    {
        Item = item;
        Tiles = tiles;
        OperatingPoints = new int[tiles.Length];
        Layers = new int?[tiles.Length];
    }

    /// <summary>Gets the item of the image: the coded item, or the grid item.</summary>
    public AvifItem Item { get; }

    /// <summary>Gets the coded items: the item itself, or the tiles of the grid in row-major order.</summary>
    public AvifItem[] Tiles { get; }

    /// <summary>
    /// Gets the operating point of the bitstream of each coded item (<c>a1op</c> property, AV1 Image File Format, section
    /// 2.3.2.1), 0 without the property.
    /// </summary>
    public int[] OperatingPoints { get; }

    /// <summary>
    /// Gets the spatial layer that each coded item shows (<c>lsel</c> property, ISO/IEC 23008-12, section 6.5.11), or
    /// <see langword="null"/> for the highest layer of the operating point, which is the image at its full quality.
    /// </summary>
    public int?[] Layers { get; }

    public bool IsGrid => Item.Type is AvifFormat.Grid;

    /// <summary>Gets the size of the image (<c>ispe</c>).</summary>
    public Size Size { get; private set; }

    /// <summary>Gets the size of a coded item.</summary>
    public Size TileSize { get; private set; }

    /// <summary>Gets the number of rows of tiles; 1 before the grid descriptor is read.</summary>
    public int Rows { get; private set; } = 1;

    /// <summary>Gets the number of columns of tiles.</summary>
    public int Columns { get; private set; } = 1;

    public int BitDepth { get; private set; }

    public bool IsMonochrome { get; private set; }

    public int SubsamplingX { get; private set; }

    public int SubsamplingY { get; private set; }

    /// <summary>Gets the transformative properties of the item (<c>clap</c>, <c>irot</c>, <c>imir</c>), as the ranges of their boxes in the <c>meta</c> payload.</summary>
    public List<(uint Type, int Offset, int Length)> Transforms { get; } = [];

    public static AvifCodedImage Create(AvifMetaBox meta, ReadOnlySpan<byte> payload, AvifItem item, ImageResourceLimits limits)
    {
        AvifItem[] tiles;
        if (item.Type is AvifFormat.Av01)
        {
            tiles = [item];
        }
        else if (item.Type is AvifFormat.Grid)
        {
            AvifItemReference? reference = null;
            foreach (var candidate in meta.References)
            {
                if (candidate.Type is AvifFormat.Dimg && candidate.FromItemId == item.Id)
                {
                    if (reference is not null)
                        throw AvifFormat.Invalid("a grid has two lists of tiles.");

                    reference = candidate;
                }
            }

            if (reference is null || reference.ToItemIds.Length is 0)
                throw AvifFormat.Invalid("a grid has no tile.");

            tiles = new AvifItem[reference.ToItemIds.Length];
            for (var i = 0; i < tiles.Length; i++)
            {
                if (!meta.Items.TryGetValue(reference.ToItemIds[i], out var tile) || tile.Type is not AvifFormat.Av01)
                    throw AvifFormat.Unsupported("A tile of the AVIF grid is not an AV1 image.", "grid of derived images");

                tiles[i] = tile;
            }
        }
        else if (item.Type is AvifFormat.Iovl)
        {
            throw AvifFormat.Unsupported($"The AVIF image is a derived image of type '{AvifFormat.ToText(item.Type)}'.", "derived image");
        }
        else
        {
            throw AvifFormat.Unsupported($"The AVIF image is coded as '{AvifFormat.ToText(item.Type)}', not as AV1.", "image codec");
        }

        var image = new AvifCodedImage(item, tiles);
        image.Size = ReadProperties(meta, payload, item, limits, isCodedItem: tiles.Length is 1 && ReferenceEquals(tiles[0], item), image);
        if (!image.IsGrid)
        {
            image.TileSize = image.Size;
        }
        else
        {
            for (var i = 0; i < tiles.Length; i++)
            {
                var tile = new AvifCodedImage(tiles[i], [tiles[i]]);
                var tileSize = ReadProperties(meta, payload, tiles[i], limits, isCodedItem: true, tile);
                image.OperatingPoints[i] = tile.OperatingPoints[0];
                image.Layers[i] = tile.Layers[0];
                if (i is 0)
                {
                    image.TileSize = tileSize;
                    image.BitDepth = tile.BitDepth;
                    image.IsMonochrome = tile.IsMonochrome;
                    image.SubsamplingX = tile.SubsamplingX;
                    image.SubsamplingY = tile.SubsamplingY;
                }
                else if (tileSize != image.TileSize || tile.BitDepth != image.BitDepth || tile.IsMonochrome != image.IsMonochrome || tile.SubsamplingX != image.SubsamplingX || tile.SubsamplingY != image.SubsamplingY)
                {
                    throw AvifFormat.Invalid("the tiles of a grid do not have the same size and sample format.");
                }

                // The transformations of a grid apply to the assembled image. Writers also declare them on each tile, where
                // they are not applied a second time; a tile with other transformations would have to be transformed
                // before the grid is assembled
                if (tile.Transforms.Count is not 0 && !HasSameTransforms(image, tile, payload))
                    throw AvifFormat.Unsupported("A tile of the AVIF grid is not transformed as the grid.", "transformed grid tiles");
            }
        }

        return image;
    }

    /// <summary>
    /// Reads the properties of an identity derivation (<c>iden</c> item): its size and its transformative properties. It
    /// has no coded item of its own.
    /// </summary>
    public static AvifCodedImage CreateIdentityDerivation(AvifMetaBox meta, ReadOnlySpan<byte> payload, AvifItem item, ImageResourceLimits limits)
        => CreateDerivation(meta, payload, item, limits, enforceLimits: false);

    /// <summary>
    /// Reads the properties of a derived image item (<c>iden</c>, <c>iovl</c>, or a <c>grid</c> of derived images): its
    /// size and its transformative properties. It has no coded item of its own.
    /// </summary>
    /// <param name="meta">The parsed box.</param>
    /// <param name="payload">The payload of the box.</param>
    /// <param name="item">The item.</param>
    /// <param name="limits">The resource limits of the operation.</param>
    /// <param name="enforceLimits">
    /// Whether the size of the item is checked against the canvas limits. The size of an identity derivation is not one
    /// that is decoded or allocated: it must be the size of its input, as the input is presented, and the limits apply
    /// to the coded image and to the image that is returned.
    /// </param>
    public static AvifCodedImage CreateDerivation(AvifMetaBox meta, ReadOnlySpan<byte> payload, AvifItem item, ImageResourceLimits limits, bool enforceLimits)
    {
        var image = new AvifCodedImage(item, []);
        image.Size = ReadProperties(meta, payload, item, limits, isCodedItem: false, image, enforceLimits);
        return image;
    }

    /// <summary>Returns whether two images declare the same transformative properties, in the same order.</summary>
    public static bool HasSameTransforms(AvifCodedImage first, AvifCodedImage second, ReadOnlySpan<byte> payload)
    {
        if (first.Transforms.Count != second.Transforms.Count)
            return false;

        for (var i = 0; i < first.Transforms.Count; i++)
        {
            var a = first.Transforms[i];
            var b = second.Transforms[i];
            if (a.Type != b.Type || !payload.Slice(a.Offset, a.Length).SequenceEqual(payload.Slice(b.Offset, b.Length)))
                return false;
        }

        return true;
    }

    // Reads the properties of an image item: the size of the item is returned, the sample format is stored in the image
    private static Size ReadProperties(AvifMetaBox meta, ReadOnlySpan<byte> payload, AvifItem item, ImageResourceLimits limits, bool isCodedItem, AvifCodedImage image, bool enforceLimits = true)
    {
        Size? size = null;
        var hasConfiguration = false;
        foreach (var (index, isEssential) in item.Properties)
        {
            var (type, offset, length) = meta.Properties[index - 1];
            var reader = new AvifBoxReader(payload.Slice(offset, length));
            switch (type)
            {
                case AvifFormat.Ispe:
                {
                    reader.ReadFullBoxHeader();
                    var width = reader.ReadUInt32();
                    var height = reader.ReadUInt32();
                    if (width is 0 || height is 0)
                        throw AvifFormat.Invalid("the size of an image is zero.");

                    if (!enforceLimits)
                    {
                        // No coded image has a side of 2^31 samples or more
                        if (width > int.MaxValue || height > int.MaxValue)
                            throw AvifFormat.Invalid("the size of an identity derivation is not the size of its input image.");
                    }
                    else
                    {
                        if (width > int.MaxValue || height > int.MaxValue)
                            throw new ImageResourceLimitException(ImageResourceLimitKind.Width, limits.MaxWidth, width);

                        limits.EnsureCanvasWithinLimits((int)width, (int)height);
                    }

                    size = new Size((int)width, (int)height);
                    break;
                }

                case AvifFormat.Av1C when isCodedItem:
                {
                    // AV1 Codec ISO Media File Format Binding, section 2.3 "AV1 Codec Configuration Box"
                    if (reader.ReadByte() is not 0x81)
                        throw AvifFormat.Invalid("the AV1 configuration record is not valid.");

                    reader.ReadByte();
                    var flags = reader.ReadByte();
                    var highBitDepth = (flags & 0x40) is not 0;
                    var twelveBit = (flags & 0x20) is not 0;
                    image.BitDepth = highBitDepth ? (twelveBit ? 12 : 10) : 8;
                    image.IsMonochrome = (flags & 0x10) is not 0;
                    image.SubsamplingX = (flags >> 3) & 1;
                    image.SubsamplingY = (flags >> 2) & 1;
                    hasConfiguration = true;
                    break;
                }

                case AvifFormat.Clap or AvifFormat.Irot or AvifFormat.Imir:
                    image.Transforms.Add((type, offset, length));
                    break;
                case AvifFormat.A1op:
                {
                    // The index is verified against the operating points of the sequence header when it is read
                    var operatingPoint = reader.ReadByte();
                    if (isCodedItem)
                        image.OperatingPoints[0] = operatingPoint;
                    else if (operatingPoint is not 0)
                        throw AvifFormat.Invalid("a derived image selects an operating point.");

                    break;
                }

                case AvifFormat.Lsel:
                {
                    // 0xFFFF: every layer may be rendered while the data arrive, and the last one is the image
                    var layer = reader.ReadUInt16();
                    if (layer is 0xFFFF)
                        break;

                    if (layer > Av1ObuHeader.MaxSpatialId)
                        throw AvifFormat.Invalid("an image selects a layer that AV1 does not have.");

                    if (!isCodedItem)
                        throw AvifFormat.Invalid("a derived image selects a layer.");

                    image.Layers[0] = layer;
                    break;
                }

                case AvifFormat.Pixi or AvifFormat.Colr or AvifFormat.Pasp or AvifFormat.AuxC or AvifFormat.A1lx or AvifFormat.Clli or AvifFormat.Mdcv or AvifFormat.Cclv or AvifFormat.Av1C or AvifFormat.Free:
                    break;
                default:
                    if (isEssential)
                        throw AvifFormat.Unsupported($"The AVIF image requires the property '{AvifFormat.ToText(type)}'.", "essential item property");

                    break;
            }
        }

        if (size is null)
            throw AvifFormat.Invalid("an image has no size.");

        if (isCodedItem && !hasConfiguration)
            throw AvifFormat.Invalid("an AV1 image has no configuration record.");

        return size.Value;
    }

    /// <summary>
    /// Reads the descriptor of the grid (ISO/IEC 23008-12, section 6.6.2.3.2), which is the data of the grid item, and verifies
    /// that the tiles cover the image.
    /// </summary>
    public void ReadGridDescriptor(ReadOnlySpan<byte> data)
    {
        var reader = new AvifBoxReader(data);
        if (reader.ReadByte() is not 0)
            throw AvifFormat.Unsupported("The version of the AVIF grid is not supported.", "grid version");

        var flags = reader.ReadByte();
        Rows = reader.ReadByte() + 1;
        Columns = reader.ReadByte() + 1;
        long width;
        long height;
        if ((flags & 1) is not 0)
        {
            width = reader.ReadUInt32();
            height = reader.ReadUInt32();
        }
        else
        {
            width = reader.ReadUInt16();
            height = reader.ReadUInt16();
        }

        if (width != Size.Width || height != Size.Height)
            throw AvifFormat.Invalid("the size of a grid does not match its 'ispe' property.");

        if (Tiles.Length != Rows * Columns)
            throw AvifFormat.Invalid("a grid does not have one tile per cell.");

        // Every tile shows at least one sample of the image
        if ((long)(Columns - 1) * TileSize.Width >= width || width > (long)Columns * TileSize.Width || (long)(Rows - 1) * TileSize.Height >= height || height > (long)Rows * TileSize.Height)
            throw AvifFormat.Invalid("the tiles of a grid do not cover the image.");
    }
}
