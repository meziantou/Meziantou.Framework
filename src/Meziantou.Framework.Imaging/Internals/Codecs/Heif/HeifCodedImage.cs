namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// An image of a file of the HEIF family that is coded with the codec of its format: a coded item (<c>av01</c> in an AVIF
/// file), or a <c>grid</c> item whose tiles are coded items (ISO/IEC 23008-12, section 6.6.2.3). The sample format comes from
/// the configuration record of the coded items (<c>av1C</c>), which their bitstream must match.
/// </summary>
internal sealed class HeifCodedImage
{
    private HeifCodedImage(HeifCodecBinding binding, HeifItem item, HeifItem[] tiles)
    {
        Binding = binding;
        Item = item;
        Tiles = tiles;
        OperatingPoints = new int[tiles.Length];
        Layers = new int?[tiles.Length];
        ConfigurationRecords = new (int Offset, int Length)[tiles.Length];
    }

    /// <summary>Gets the format of the file, which knows the codec of the coded items.</summary>
    public HeifCodecBinding Binding { get; }

    /// <summary>Gets the item of the image: the coded item, or the grid item.</summary>
    public HeifItem Item { get; }

    /// <summary>Gets the coded items: the item itself, or the tiles of the grid in row-major order.</summary>
    public HeifItem[] Tiles { get; }

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

    /// <summary>Gets the range of the configuration record of each coded item (the payload of its box) in the <c>meta</c> payload.</summary>
    public (int Offset, int Length)[] ConfigurationRecords { get; }

    public bool IsGrid => Item.Type is HeifFormat.Grid;

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

    /// <summary>Returns whether a bitstream has the sample format that the configuration record of the image declares.</summary>
    public bool HasSampleFormat(in HeifBitstreamFormat format) => format.HasSampleFormat(BitDepth, IsMonochrome, SubsamplingX, SubsamplingY);

    /// <summary>Gets the transformative properties of the item (<c>clap</c>, <c>irot</c>, <c>imir</c>), as the ranges of their boxes in the <c>meta</c> payload.</summary>
    public List<(uint Type, int Offset, int Length)> Transforms { get; } = [];

    public static HeifCodedImage Create(HeifCodecBinding binding, HeifMetaBox meta, ReadOnlySpan<byte> payload, HeifItem item, ImageResourceLimits limits)
    {
        HeifItem[] tiles;
        if (binding.IsCodedItemType(item.Type))
        {
            tiles = [item];
        }
        else if (item.Type is HeifFormat.Grid)
        {
            HeifItemReference? reference = null;
            foreach (var candidate in meta.References)
            {
                if (candidate.Type is HeifFormat.Dimg && candidate.FromItemId == item.Id)
                {
                    if (reference is not null)
                        throw binding.Invalid("a grid has two lists of tiles.");

                    reference = candidate;
                }
            }

            if (reference is null || reference.ToItemIds.Length is 0)
                throw binding.Invalid("a grid has no tile.");

            tiles = new HeifItem[reference.ToItemIds.Length];
            for (var i = 0; i < tiles.Length; i++)
            {
                if (!meta.Items.TryGetValue(reference.ToItemIds[i], out var tile))
                    throw binding.Invalid("a tile of a grid is an item that does not exist.");

                if (!binding.IsCodedItemType(tile.Type))
                    throw binding.Unsupported($"A tile of the {binding.Name} grid is not an {binding.CodecName} image.", "grid of derived images");

                tiles[i] = tile;
            }
        }
        else if (item.Type is HeifFormat.Iden or HeifFormat.Iovl)
        {
            throw binding.Unsupported($"The {binding.Name} image is a derived image of type '{HeifFormat.ToText(item.Type)}'.", "derived image");
        }
        else
        {
            throw binding.Unsupported($"The {binding.Name} image is coded as '{HeifFormat.ToText(item.Type)}', not as {binding.CodecName}.", "image codec");
        }

        var image = new HeifCodedImage(binding, item, tiles);
        image.Size = ReadProperties(binding, meta, payload, item, limits, isCodedItem: tiles.Length is 1 && ReferenceEquals(tiles[0], item), image);
        if (!image.IsGrid)
        {
            image.TileSize = image.Size;
        }
        else
        {
            for (var i = 0; i < tiles.Length; i++)
            {
                var tile = new HeifCodedImage(binding, tiles[i], [tiles[i]]);
                var tileSize = ReadProperties(binding, meta, payload, tiles[i], limits, isCodedItem: true, tile);
                image.OperatingPoints[i] = tile.OperatingPoints[0];
                image.Layers[i] = tile.Layers[0];
                image.ConfigurationRecords[i] = tile.ConfigurationRecords[0];
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
                    throw binding.Invalid("the tiles of a grid do not have the same size and sample format.");
                }

                // The transformations of a grid apply to the assembled image. Writers also declare them on each tile, where
                // they are not applied a second time; a tile with other transformations would have to be transformed
                // before the grid is assembled
                if (tile.Transforms.Count is not 0 && !HasSameTransforms(image, tile, payload))
                    throw binding.Unsupported($"A tile of the {binding.Name} grid is not transformed as the grid.", "transformed grid tiles");
            }
        }

        return image;
    }

    /// <summary>Returns whether two images declare the same transformative properties, in the same order.</summary>
    public static bool HasSameTransforms(HeifCodedImage first, HeifCodedImage second, ReadOnlySpan<byte> payload)
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
    private static Size ReadProperties(HeifCodecBinding binding, HeifMetaBox meta, ReadOnlySpan<byte> payload, HeifItem item, ImageResourceLimits limits, bool isCodedItem, HeifCodedImage image)
    {
        Size? size = null;
        var hasConfiguration = false;
        foreach (var (index, isEssential) in item.Properties)
        {
            var (type, offset, length) = meta.Properties[index - 1];
            var reader = new HeifBoxReader(payload.Slice(offset, length), binding);
            switch (type)
            {
                case HeifFormat.Ispe:
                {
                    reader.ReadFullBoxHeader();
                    var width = reader.ReadUInt32();
                    var height = reader.ReadUInt32();
                    if (width is 0 || height is 0)
                        throw binding.Invalid("the size of an image is zero.");

                    if (width > int.MaxValue || height > int.MaxValue)
                        throw new ImageResourceLimitException(ImageResourceLimitKind.Width, limits.MaxWidth, width);

                    limits.EnsureCanvasWithinLimits((int)width, (int)height);
                    size = new Size((int)width, (int)height);
                    break;
                }

                case HeifFormat.Clap or HeifFormat.Irot or HeifFormat.Imir:
                    image.Transforms.Add((type, offset, length));
                    break;
                case HeifFormat.Pixi or HeifFormat.Colr or HeifFormat.Pasp or HeifFormat.AuxC or HeifFormat.Clli or HeifFormat.Mdcv or HeifFormat.Cclv or HeifFormat.Free:
                    break;
                default:
                    if (type == binding.ConfigurationBoxType)
                    {
                        // The configuration record of a derived image describes nothing
                        if (isCodedItem)
                        {
                            (image.BitDepth, image.IsMonochrome, image.SubsamplingX, image.SubsamplingY) = binding.ReadConfigurationRecord(ref reader);
                            image.ConfigurationRecords[0] = (offset, length);
                            hasConfiguration = true;
                        }
                    }
                    else if (!binding.TryReadItemProperty(type, ref reader, isCodedItem, image) && isEssential)
                    {
                        throw binding.Unsupported($"The {binding.Name} image requires the property '{HeifFormat.ToText(type)}'.", "essential item property");
                    }

                    break;
            }
        }

        if (size is null)
            throw binding.Invalid("an image has no size.");

        if (isCodedItem && !hasConfiguration)
            throw binding.Invalid($"an {binding.CodecName} image has no configuration record.");

        return size.Value;
    }

    /// <summary>
    /// Reads the descriptor of the grid (ISO/IEC 23008-12, section 6.6.2.3.2), which is the data of the grid item, and verifies
    /// that the tiles cover the image.
    /// </summary>
    public void ReadGridDescriptor(ReadOnlySpan<byte> data)
    {
        var binding = Binding;
        var reader = new HeifBoxReader(data, binding);
        if (reader.ReadByte() is not 0)
            throw binding.Unsupported($"The version of the {binding.Name} grid is not supported.", "grid version");

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
            throw binding.Invalid("the size of a grid does not match its 'ispe' property.");

        if (Tiles.Length != Rows * Columns)
            throw binding.Invalid("a grid does not have one tile per cell.");

        // Every tile shows at least one sample of the image
        if ((long)(Columns - 1) * TileSize.Width >= width || width > (long)Columns * TileSize.Width || (long)(Rows - 1) * TileSize.Height >= height || height > (long)Rows * TileSize.Height)
            throw binding.Invalid("the tiles of a grid do not cover the image.");
    }
}
