using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The structure of a KTX 2.0 file, read and validated before any texel is decoded: the header, the index, the level
/// index, the data format descriptor, the key/value data and the BasisLZ global data.
/// </summary>
/// <remarks>
/// <para>
/// Every section is located by the index and checked against the length of the input and against the other sections:
/// the data format descriptor, the key/value data and the global data follow the level index in this order, and the
/// levels follow them, from the smallest (the last level) to the largest, without overlapping. The length of a level is
/// the one its size, its layers, its faces and its depth give (section 3.13.1); a supercompressed level gives that length
/// once inflated.
/// </para>
/// <para>
/// The data format descriptor must describe the <c>vkFormat</c> of the header (section 3.10.1): its color model, the
/// dimensions of its texel blocks, the bytes of its plane (or 0, which older writers stored for supercompressed data),
/// and the channel, bit offset, bit length and qualifiers of each sample. The bounds of the samples are not compared.
/// </para>
/// </remarks>
internal sealed class Ktx2Texture
{
    private Ktx2Texture()
    {
    }

    public int VkFormat { get; private init; }

    public Ktx2VkFormat? Format { get; private init; }

    public int TypeSize { get; private init; }

    public int PixelWidth { get; private init; }

    public int PixelHeight { get; private init; }

    public int PixelDepth { get; private init; }

    public int LayerCount { get; private init; }

    public int FaceCount { get; private init; }

    public int LevelCount { get; private init; }

    public uint Scheme { get; private init; }

    /// <summary>Gets the number of layers of the level images: the layer count, or 1.</summary>
    public int Layers => Math.Max(1, LayerCount);

    /// <summary>Gets the number of levels of the level index: the level count, or 1.</summary>
    public int Levels => Math.Max(1, LevelCount);

    public Ktx2Level[] LevelIndex { get; private init; } = [];

    public int ColorModel { get; private init; }

    public int ColorPrimaries { get; private init; }

    public int TransferFunction { get; private init; }

    public int DfdFlags { get; private init; }

    public Ktx2Sample[] DfdSamples { get; private init; } = [];

    public bool IsPremultiplied => (DfdFlags & Ktx2Format.FlagAlphaPremultiplied) != 0;

    /// <summary>Gets the key/value pairs, in their order in the file.</summary>
    public KeyValuePair<string, byte[]>[] KeyValues { get; private init; } = [];

    /// <summary>Gets the value of <c>KTXorientation</c>, validated, or <see langword="null"/>.</summary>
    public string? Orientation { get; private init; }

    /// <summary>Gets the value of <c>KTXswizzle</c>, validated (four of <c>rgba01</c>), or <see langword="null"/>.</summary>
    public string? Swizzle { get; private init; }

    public string? Writer { get; private init; }

    public string? WriterScParams { get; private init; }

    public string? AstcDecodeMode { get; private init; }

    /// <summary>Gets the faces of an incomplete cube map (the bits of <c>KTXcubemapIncomplete</c>), or <see langword="null"/>.</summary>
    public int? CubemapIncompleteFaces { get; private init; }

    public Ktx2Animation? Animation { get; private init; }

    /// <summary>Gets the location of the BasisLZ global data (scheme 1), or (0, 0).</summary>
    public (long Offset, long Length) GlobalData { get; private init; }

    /// <summary>Gets the counts and lengths of the codebooks of the BasisLZ global data.</summary>
    public (int EndpointCount, int SelectorCount, long EndpointsLength, long SelectorsLength, long TablesLength) BasisCodebooks { get; private set; }

    /// <summary>Gets the image descriptors of the BasisLZ global data, in the order level, layer, face, depth slice.</summary>
    public Ktx2BasisImage[] BasisImages { get; private set; } = [];

    /// <summary>Gets the number of channels the ETC1S model or the UASTC model declares (KTX section 3.10.3).</summary>
    public int BasisChannelId { get; private init; }

    /// <summary>Gets the channel id of the second ETC1S slice, or -1 without one.</summary>
    public int BasisSecondChannelId { get; private init; } = -1;

    /// <summary>Gets the width of a level.</summary>
    public int GetLevelWidth(int level) => Math.Max(1, PixelWidth >> level);

    /// <summary>Gets the height of a level (1 for a one-dimensional texture).</summary>
    public int GetLevelHeight(int level) => Math.Max(1, PixelHeight >> level);

    /// <summary>Gets the number of depth slices of a level (1 for a texture that is not three-dimensional).</summary>
    public int GetLevelDepth(int level) => Math.Max(1, PixelDepth >> level);

    /// <summary>Gets the number of subresources: every depth slice of every level of every face of every layer.</summary>
    public long SubresourceCount
    {
        get
        {
            long slices = 0;
            for (var level = 0; level < Levels; level++)
            {
                slices += GetLevelDepth(level);
            }

            return slices * Layers * FaceCount;
        }
    }

    /// <summary>Enumerates the subresources in the order of an <see cref="ImageCollection"/>: layer, face, level, depth slice.</summary>
    public IEnumerable<Ktx2Subresource> EnumerateSubresources()
    {
        for (var layer = 0; layer < Layers; layer++)
        {
            for (var face = 0; face < FaceCount; face++)
            {
                for (var level = 0; level < Levels; level++)
                {
                    var depth = GetLevelDepth(level);
                    for (var slice = 0; slice < depth; slice++)
                    {
                        yield return new Ktx2Subresource(level, layer, face, slice);
                    }
                }
            }
        }
    }

    /// <summary>Gets the number of blocks of a row of a level, and of rows.</summary>
    public (int Columns, int Rows) GetBlockCounts(int level)
    {
        var format = Format ?? throw new InvalidOperationException("The format of the texture is not decoded.");
        return (CeilDiv(GetLevelWidth(level), format.BlockWidth), CeilDiv(GetLevelHeight(level), format.BlockHeight));
    }

    /// <summary>Gets the length of one image (a depth slice of a face of a layer) of a level, once inflated.</summary>
    public long GetImageLength(int level)
    {
        var format = Format ?? throw new InvalidOperationException("The format of the texture is not decoded.");
        var (columns, rows) = GetBlockCounts(level);
        return Ktx2Format.Multiply(Ktx2Format.Multiply(columns, rows), format.BytesPerBlock);
    }

    /// <summary>Gets the index of an image in the level data: layer, face, then depth slice (section 3.13.1).</summary>
    public long GetImageIndex(Ktx2Subresource subresource)
        => ((((long)subresource.ArrayLayer * FaceCount) + subresource.Face) * GetLevelDepth(subresource.MipLevel)) + subresource.DepthSlice;

    /// <summary>Gets the index of the BasisLZ image descriptor of a subresource (Appendix C: level, layer, face, depth slice).</summary>
    public int GetBasisImageIndex(Ktx2Subresource subresource)
    {
        long index = 0;
        for (var level = 0; level < subresource.MipLevel; level++)
        {
            index += (long)Layers * FaceCount * GetLevelDepth(level);
        }

        return (int)(index + GetImageIndex(subresource));
    }

    /// <summary>Reads and validates the structure of a file.</summary>
    /// <exception cref="InvalidImageContentException">The structure is malformed or inconsistent.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The file uses a supercompression scheme or a color model the decoder does not read.</exception>
    /// <exception cref="ImageResourceLimitException">The size of the texture or its metadata exceed a limit.</exception>
    public static Ktx2Texture Read(RandomAccessSource source, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);
        if (source.Length < Ktx2Format.HeaderLength)
            throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the file has {source.Length} bytes; the header and the index have {Ktx2Format.HeaderLength}."));

        Span<byte> header = stackalloc byte[Ktx2Format.HeaderLength];
        source.Read(0, header);
        if (!header.StartsWith(Ktx2Format.Identifier))
            throw Ktx2Format.Invalid("the file does not start with the KTX 2.0 identifier.");

        var vkFormat = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
        var typeSize = BinaryPrimitives.ReadUInt32LittleEndian(header[16..]);
        var width = BinaryPrimitives.ReadUInt32LittleEndian(header[20..]);
        var height = BinaryPrimitives.ReadUInt32LittleEndian(header[24..]);
        var depth = BinaryPrimitives.ReadUInt32LittleEndian(header[28..]);
        var layerCount = BinaryPrimitives.ReadUInt32LittleEndian(header[32..]);
        var faceCount = BinaryPrimitives.ReadUInt32LittleEndian(header[36..]);
        var levelCount = BinaryPrimitives.ReadUInt32LittleEndian(header[40..]);
        var scheme = BinaryPrimitives.ReadUInt32LittleEndian(header[44..]);
        var dfdOffset = BinaryPrimitives.ReadUInt32LittleEndian(header[48..]);
        var dfdLength = BinaryPrimitives.ReadUInt32LittleEndian(header[52..]);
        var kvdOffset = BinaryPrimitives.ReadUInt32LittleEndian(header[56..]);
        var kvdLength = BinaryPrimitives.ReadUInt32LittleEndian(header[60..]);
        var sgdOffset = BinaryPrimitives.ReadUInt64LittleEndian(header[64..]);
        var sgdLength = BinaryPrimitives.ReadUInt64LittleEndian(header[72..]);

        // Section 3.8: the schemes this version defines; the others are reserved, or the vendors' own
        if (scheme > Ktx2Format.SchemeZlib)
            throw Ktx2Format.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The KTX 2.0 file uses the supercompression scheme 0x{scheme:X}, which the decoder does not read (none, BasisLZ, Zstandard and ZLIB are)."), "Supercompression: 0x" + scheme.ToString("X", CultureInfo.InvariantCulture));

        // Section 4.1: the texture types
        if (width == 0)
            throw Ktx2Format.Invalid("pixelWidth is 0.");

        if (faceCount is not (1 or 6))
            throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"faceCount is {faceCount}; 1, or 6 for a cube map, is expected."));

        if (height == 0 && depth != 0)
            throw Ktx2Format.Invalid("pixelDepth is not 0 but pixelHeight is: a three-dimensional texture has a height.");

        if (faceCount == 6 && (height != width || depth != 0))
            throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the cube map is {width}x{height}x{depth}: its faces must be square and two-dimensional."));

        var limits = context.Limits;
        if (width > limits.MaxWidth)
            throw new ImageResourceLimitException(ImageResourceLimitKind.Width, limits.MaxWidth, width);

        if (Math.Max(height, 1) > limits.MaxHeight)
            throw new ImageResourceLimitException(ImageResourceLimitKind.Height, limits.MaxHeight, height);

        if (depth > int.MaxValue || layerCount > int.MaxValue)
            throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the texture has {depth} depth slices and {layerCount} layers."));

        limits.EnsureCanvasWithinLimits((int)width, (int)Math.Max(height, 1));

        // Section 3.7: the levels, from the base level; levelCount 0 asks a loader to generate the other levels
        var maxLevels = 1 + System.Numerics.BitOperations.Log2(Math.Max(width, Math.Max(height, depth)));
        if (levelCount > maxLevels)
            throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"levelCount is {levelCount}; a {width}x{height}x{depth} texture has at most {maxLevels} levels."));

        var levels = (int)Math.Max(levelCount, 1);
        var levelIndexEnd = Ktx2Format.HeaderLength + ((long)levels * Ktx2Format.LevelIndexEntryLength);
        if (source.Length < levelIndexEnd)
            throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the file has {source.Length} bytes; its level index ends at {levelIndexEnd}."));

        var levelIndex = new Ktx2Level[levels];
        using (var index = source.ReadToBuffer(Ktx2Format.HeaderLength, (long)levels * Ktx2Format.LevelIndexEntryLength))
        {
            for (var i = 0; i < levels; i++)
            {
                var entry = index.Span.Slice(i * Ktx2Format.LevelIndexEntryLength, Ktx2Format.LevelIndexEntryLength);
                var offset = BinaryPrimitives.ReadUInt64LittleEndian(entry);
                var length = BinaryPrimitives.ReadUInt64LittleEndian(entry[8..]);
                var uncompressed = BinaryPrimitives.ReadUInt64LittleEndian(entry[16..]);
                if (offset > long.MaxValue || length > long.MaxValue || uncompressed > long.MaxValue)
                    throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the level {i} has the offset {offset} and the length {length}."));

                levelIndex[i] = new Ktx2Level((long)offset, (long)length, (long)uncompressed);
            }
        }

        // Section 3.10: the data format descriptor follows the level index
        if (dfdOffset < levelIndexEnd || dfdLength < 4 || !source.IsInRange(dfdOffset, dfdLength))
            throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the data format descriptor at {dfdOffset} ({dfdLength} bytes) is not between the level index (which ends at {levelIndexEnd}) and the end of the file ({source.Length} bytes)."));

        context.Tracker.ChargeMetadataBytes(dfdLength);
        Dfd dfd;
        using (var buffer = source.ReadToBuffer(dfdOffset, dfdLength))
        {
            dfd = ReadDataFormatDescriptor(buffer.Span);
        }

        var metadataEnd = (long)dfdOffset + dfdLength;

        // Section 3.11: the key/value data
        List<KeyValuePair<string, byte[]>> keyValues = [];
        if (kvdLength != 0)
        {
            if (kvdOffset < metadataEnd || !source.IsInRange(kvdOffset, kvdLength))
                throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the key/value data at {kvdOffset} ({kvdLength} bytes) is not between the data format descriptor (which ends at {metadataEnd}) and the end of the file."));

            context.Tracker.ChargeMetadataBytes(kvdLength);
            using var buffer = source.ReadToBuffer(kvdOffset, kvdLength);
            keyValues = ReadKeyValues(buffer.Span);
            metadataEnd = (long)kvdOffset + kvdLength;
        }
        else if (kvdOffset != 0)
        {
            throw Ktx2Format.Invalid("kvdByteOffset is not 0 but kvdByteLength is.");
        }

        // Section 3.12: the global data, at the next 8-byte boundary
        if (sgdLength != 0)
        {
            if (scheme != Ktx2Format.SchemeBasisLz)
                throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the file has supercompression global data, which the scheme {Ktx2Format.GetSchemeName(scheme)} does not define."));

            if (sgdOffset % 8 != 0 || (long)sgdOffset < metadataEnd || sgdLength > long.MaxValue || !source.IsInRange((long)sgdOffset, (long)sgdLength))
                throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the supercompression global data at {sgdOffset} ({sgdLength} bytes) is not on an 8-byte boundary after the key/value data (which ends at {metadataEnd}) and before the end of the file."));

            metadataEnd = (long)sgdOffset + (long)sgdLength;
        }
        else if (sgdOffset != 0)
        {
            throw Ktx2Format.Invalid("sgdByteOffset is not 0 but sgdByteLength is.");
        }

        // The format: a VkFormat, or the ETC1S and UASTC color models of VK_FORMAT_UNDEFINED (section 4.2)
        var format = ResolveFormat(vkFormat, scheme, dfd);
        if (format is not null)
        {
            ValidateDataFormatDescriptor(format, scheme, dfd);
            if (typeSize != format.TypeSize)
                throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"typeSize is {typeSize}; {format.TypeSize} is expected for {format.Name}."));

            if (format.Encoding != Ktx2Encoding.Uncompressed && height == 0)
                throw Ktx2Format.Invalid("pixelHeight is 0: block-compressed textures are two-dimensional at least.");

            if (format.Encoding != Ktx2Encoding.Uncompressed && levelCount == 0)
                throw Ktx2Format.Invalid("levelCount is 0, which block-compressed textures do not allow.");
        }

        var pairs = keyValues.ToArray();
        var texture = new Ktx2Texture
        {
            VkFormat = (int)Math.Min(vkFormat, int.MaxValue),
            Format = format,
            TypeSize = (int)Math.Min(typeSize, int.MaxValue),
            PixelWidth = (int)width,
            PixelHeight = (int)height,
            PixelDepth = (int)depth,
            LayerCount = (int)layerCount,
            FaceCount = (int)faceCount,
            LevelCount = (int)levelCount,
            Scheme = scheme,
            LevelIndex = levelIndex,
            ColorModel = dfd.ColorModel,
            ColorPrimaries = dfd.Primaries,
            TransferFunction = dfd.Transfer,
            DfdFlags = dfd.Flags,
            DfdSamples = dfd.Samples,
            KeyValues = pairs,
            Orientation = ReadOrientation(pairs, height, depth),
            Swizzle = ReadSwizzle(pairs),
            Writer = ReadString(pairs, Ktx2Format.WriterKey),
            WriterScParams = ReadString(pairs, Ktx2Format.WriterScParamsKey),
            AstcDecodeMode = ReadAstcDecodeMode(pairs),
            CubemapIncompleteFaces = ReadCubemapIncomplete(pairs, faceCount),
            Animation = ReadAnimation(pairs, layerCount),
            GlobalData = ((long)sgdOffset, (long)sgdLength),
            BasisChannelId = format?.Encoding is Ktx2Encoding.Etc1S or Ktx2Encoding.Uastc ? dfd.Samples[0].Channel : -1,
            BasisSecondChannelId = format?.Encoding == Ktx2Encoding.Etc1S && dfd.Samples.Length == 2 ? dfd.Samples[1].Channel : -1,
        };

        if (texture.Animation is not null && texture.CubemapIncompleteFaces is not null)
            throw Ktx2Format.Invalid("KTXanimData and KTXcubemapIncomplete are both present.");

        if (format is null)
            return texture;

        texture.CheckSubresourceCount();
        if (scheme == Ktx2Format.SchemeBasisLz)
        {
            (texture.BasisImages, texture.BasisCodebooks) = texture.ReadBasisGlobalData(source, context);
        }

        texture.ValidateLevels(source, metadataEnd);
        return texture;
    }

    private static Ktx2VkFormat? ResolveFormat(uint vkFormat, uint scheme, Dfd dfd)
    {
        if (vkFormat == Ktx2VkFormats.Undefined)
        {
            if (scheme == Ktx2Format.SchemeBasisLz)
            {
                if (dfd.ColorModel != Ktx2Format.ModelEtc1S)
                    throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the BasisLZ data has the color model {dfd.ColorModel}; ETC1S (163) is expected."));

                return Ktx2VkFormats.Etc1S;
            }

            return dfd.ColorModel switch
            {
                Ktx2Format.ModelUastc => Ktx2VkFormats.Uastc,
                Ktx2Format.ModelEtc1S => throw Ktx2Format.Invalid("the ETC1S data is not supercompressed with BasisLZ."),
                _ => throw Ktx2Format.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The KTX 2.0 file stores data of VK_FORMAT_UNDEFINED with the color model {dfd.ColorModel}, which the decoder does not read (ETC1S and UASTC are)."), "Format: UNDEFINED with color model " + dfd.ColorModel.ToString(CultureInfo.InvariantCulture)),
            };
        }

        if (scheme == Ktx2Format.SchemeBasisLz)
            throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the BasisLZ data has the vkFormat {vkFormat}; VK_FORMAT_UNDEFINED is expected."));

        if (vkFormat <= int.MaxValue && Ktx2VkFormats.TryGet((int)vkFormat, out var format))
            return format;

        // Section 3.2: the formats KTX prohibits
        if (vkFormat is 11 or 12 or 18 or 19 or 25 or 26 or 32 or 33 or 39 or 40 or 46 or 47 or 53 or 54 or 60 or 61 or 66 or 67 or 72 or 73 or 79 or 80 or 86 or 87 or 93 or 94
            or (>= 1000156002 and <= 1000156006) or (>= 1000156012 and <= 1000156016) or (>= 1000156022 and <= 1000156026) or (>= 1000156029 and <= 1000156033) or (>= 1000330000 and <= 1000330003))
        {
            throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the vkFormat {vkFormat} is one KTX prohibits (a SCALED or multi-planar format)."));
        }

        return null;
    }

    private void CheckSubresourceCount()
    {
        // Every image of every level is a subresource; a level of a 3D texture has one image per depth slice
        var count = SubresourceCount;
        if (count > int.MaxValue)
            throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the texture has {count} images."));
    }

    private (Ktx2BasisImage[] Images, (int EndpointCount, int SelectorCount, long EndpointsLength, long SelectorsLength, long TablesLength) Codebooks) ReadBasisGlobalData(RandomAccessSource source, ImageCodecContext context)
    {
        var (offset, length) = GlobalData;
        if (length < Ktx2Format.BasisLzGlobalHeaderLength)
            throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the BasisLZ global data has {length} bytes; its header has {Ktx2Format.BasisLzGlobalHeaderLength}."));

        Span<byte> header = stackalloc byte[Ktx2Format.BasisLzGlobalHeaderLength];
        source.Read(offset, header);
        var endpointCount = BinaryPrimitives.ReadUInt16LittleEndian(header);
        var selectorCount = BinaryPrimitives.ReadUInt16LittleEndian(header[2..]);
        var endpointsLength = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        var selectorsLength = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        var tablesLength = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
        var extendedLength = BinaryPrimitives.ReadUInt32LittleEndian(header[16..]);

        // Appendix C: one descriptor per image of the mip level array, and no byte after the extended data
        var imageCount = SubresourceCount;
        var expected = Ktx2Format.BasisLzGlobalHeaderLength + (imageCount * Ktx2Format.BasisLzImageDescLength) + endpointsLength + selectorsLength + tablesLength + extendedLength;
        if (expected != length)
            throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the BasisLZ global data has {length} bytes; its header and {imageCount} image descriptors give {expected}."));

        if (extendedLength != 0)
            throw Ktx2Format.Invalid("the BasisLZ global data of ETC1S has extended data.");

        if (endpointCount == 0 || selectorCount == 0)
            throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the BasisLZ codebooks have {endpointCount} endpoints and {selectorCount} selectors."));

        // The descriptors count as metadata before anything is allocated for them
        context.Tracker.ChargeMetadataBytes(imageCount * Ktx2Format.BasisLzImageDescLength);
        var images = new Ktx2BasisImage[imageCount];
        using var descriptors = source.ReadToBuffer(offset + Ktx2Format.BasisLzGlobalHeaderLength, imageCount * Ktx2Format.BasisLzImageDescLength);
        var hasAlpha = BasisSecondChannelId >= 0;
        var animated = Animation is not null;
        for (var i = 0; i < images.Length; i++)
        {
            var span = descriptors.Span.Slice(i * Ktx2Format.BasisLzImageDescLength, Ktx2Format.BasisLzImageDescLength);
            var image = new Ktx2BasisImage(BinaryPrimitives.ReadUInt32LittleEndian(span), BinaryPrimitives.ReadUInt32LittleEndian(span[4..]), BinaryPrimitives.ReadUInt32LittleEndian(span[8..]), BinaryPrimitives.ReadUInt32LittleEndian(span[12..]), BinaryPrimitives.ReadUInt32LittleEndian(span[16..]));
            if ((image.Flags & ~Ktx2Format.BasisLzPFrameFlag) != 0)
                throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the BasisLZ image {i} has the flags 0x{image.Flags:X}."));

            if (image.RgbLength == 0)
                throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the BasisLZ image {i} has an empty slice."));

            if (hasAlpha != (image.AlphaLength != 0) || (!hasAlpha && image.AlphaOffset != 0))
                throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the BasisLZ image {i} has {(image.AlphaLength != 0 ? "a" : "no")} second slice; the data format descriptor declares {(hasAlpha ? "two" : "one")}."));

            images[i] = image;
        }

        // P-frames refer to the previous image of an animation (Appendix C.7.1): only an animation has them, never its first frame
        if (images.Length > 0 && images[0].IsPFrame)
            throw Ktx2Format.Invalid("the first BasisLZ image is a P-frame.");

        if (!animated && Array.Exists(images, image => image.IsPFrame))
            throw Ktx2Format.Invalid("the BasisLZ data has P-frames but the texture is not an animation (KTXanimData).");

        if (animated && (Levels != 1 || FaceCount != 1 || PixelDepth != 0) && Array.Exists(images, image => image.IsPFrame))
            throw Ktx2Format.Unsupported("The KTX 2.0 animation has P-frames and several levels, faces or depth slices: only the images of one level of a two-dimensional array are frames.", "BasisLZ P-frames");

        return (images, (endpointCount, selectorCount, endpointsLength, selectorsLength, tablesLength));
    }

    private void ValidateLevels(RandomAccessSource source, long metadataEnd)
    {
        var format = Format!;
        var alignment = Scheme == Ktx2Format.SchemeNone ? LeastCommonMultiple(format.BytesPerBlock, 4) : 1;
        var previousEnd = metadataEnd;
        var faceLayers = (long)FaceCount * Layers;
        for (var level = Levels - 1; level >= 0; level--)
        {
            var entry = LevelIndex[level];
            var images = faceLayers * GetLevelDepth(level);
            var expected = Ktx2Format.Multiply(GetImageLength(level), images);

            // Section 3.9.7: the lengths a scheme gives; BasisLZ levels hold the slices of their images
            switch (Scheme)
            {
                case Ktx2Format.SchemeNone:
                    if (entry.Length != expected || entry.UncompressedLength != expected)
                        throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the level {level} has {entry.Length} bytes (uncompressed: {entry.UncompressedLength}); its {images} images of {GetLevelWidth(level)}x{GetLevelHeight(level)} {format.Name} texels have {expected}."));

                    if (entry.Offset % alignment != 0)
                        throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the level {level} is at the offset {entry.Offset}, which is not a multiple of {alignment}."));

                    break;
                case Ktx2Format.SchemeBasisLz:
                    if (entry.UncompressedLength != 0 || entry.Length == 0)
                        throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the BasisLZ level {level} has {entry.Length} bytes and an uncompressed length of {entry.UncompressedLength}; 0 is expected."));

                    break;
                default:
                    if (entry.UncompressedLength != expected || entry.Length == 0)
                        throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the supercompressed level {level} has an uncompressed length of {entry.UncompressedLength}; its {images} images of {GetLevelWidth(level)}x{GetLevelHeight(level)} {format.Name} texels have {expected}."));

                    break;
            }

            // Section 3.13: the levels follow the metadata, from the smallest images to the largest, without overlapping
            if (entry.Offset < previousEnd || !source.IsInRange(entry.Offset, entry.Length))
                throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the level {level} at {entry.Offset} ({entry.Length} bytes) does not start after the metadata and the smaller levels (which end at {previousEnd}), or ends after the end of the file ({source.Length} bytes)."));

            previousEnd = entry.Offset + entry.Length;
        }

        if (Scheme != Ktx2Format.SchemeBasisLz)
            return;

        var index = 0;
        for (var level = 0; level < Levels; level++)
        {
            var length = LevelIndex[level].Length;
            var count = faceLayers * GetLevelDepth(level);
            for (var i = 0; i < count; i++, index++)
            {
                var image = BasisImages[index];
                if (image.RgbOffset + (long)image.RgbLength > length || image.AlphaOffset + (long)image.AlphaLength > length)
                    throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"a slice of the BasisLZ image {index} ends after its level ({length} bytes)."));
            }
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Data format descriptor (Khronos Data Format Specification, sections 4 and 5)
    // -----------------------------------------------------------------------------------------------------------------

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Dfd(int ColorModel, int Primaries, int Transfer, int Flags, int[] BlockDimensions, int[] BytesPlane, Ktx2Sample[] Samples);

    private static Dfd ReadDataFormatDescriptor(ReadOnlySpan<byte> data)
    {
        // Section 3.10: the total size, then the descriptor blocks; the basic block is the first one
        var total = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (total != data.Length)
            throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"dfdTotalSize is {total}; dfdByteLength is {data.Length}."));

        Dfd? basic = null;
        var position = 4;
        while (position < data.Length)
        {
            if (data.Length - position < 8)
                throw Ktx2Format.Invalid("a descriptor block of the data format descriptor is truncated.");

            var word0 = BinaryPrimitives.ReadUInt32LittleEndian(data[position..]);
            var word1 = BinaryPrimitives.ReadUInt32LittleEndian(data[(position + 4)..]);
            var vendorId = word0 & 0x1FFFF;
            var descriptorType = word0 >> 17;
            var version = word1 & 0xFFFF;
            var size = (int)(word1 >> 16);
            if (size < 8 || size > data.Length - position)
                throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"a descriptor block of the data format descriptor declares {size} bytes; {data.Length - position} remain."));

            if (vendorId == 0 && descriptorType == 0)
            {
                if (basic is not null || position != 4)
                    throw Ktx2Format.Invalid("the basic descriptor block is not the first and only one.");

                // Section 5: version 2 (1.3), and 24 bytes plus 16 per sample
                if (version != 2)
                    throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the basic descriptor block has the version {version}; 2 is expected."));

                if (size < 24 || (size - 24) % 16 != 0)
                    throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the basic descriptor block has {size} bytes; 24 and 16 per sample are expected."));

                var block = data.Slice(position, size);
                var model = block[8];
                var primaries = block[9];
                var transfer = block[10];
                var flags = block[11];
                int[] dimensions = [block[12] + 1, block[13] + 1, block[14] + 1, block[15] + 1];
                var bytesPlane = block.Slice(16, 8).ToArray().Select(value => (int)value).ToArray();
                var samples = new Ktx2Sample[(size - 24) / 16];
                for (var i = 0; i < samples.Length; i++)
                {
                    var sample = block.Slice(24 + (i * 16), 16);
                    var bits = BinaryPrimitives.ReadUInt32LittleEndian(sample);
                    samples[i] = new Ktx2Sample((int)((bits >> 24) & 0xF), (int)(bits & 0xFFFF), (int)((bits >> 16) & 0xFF) + 1, (int)((bits >> 24) & 0xF0));
                }

                basic = new Dfd(model, primaries, transfer, flags, dimensions, bytesPlane, samples);
            }
            else if (position == 4)
            {
                throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the first descriptor block of the data format descriptor has the vendor {vendorId} and the type {descriptorType}; the basic block (0, 0) is expected."));
            }

            position += size;
        }

        return basic ?? throw Ktx2Format.Invalid("the data format descriptor has no descriptor block.");
    }

    private static void ValidateDataFormatDescriptor(Ktx2VkFormat format, uint scheme, Dfd dfd)
    {
        switch (format.Encoding)
        {
            case Ktx2Encoding.Etc1S:
                ValidateBasisSamples(dfd, 8, allowAlphaPlane: true, [Ktx2Format.Etc1SChannelRgb, Ktx2Format.Etc1SChannelRrr]);
                if (dfd.Samples.Length == 2)
                {
                    var (first, second) = (dfd.Samples[0].Channel, dfd.Samples[1].Channel);
                    if (!(first == Ktx2Format.Etc1SChannelRgb && second == Ktx2Format.Etc1SChannelAaa) && !(first == Ktx2Format.Etc1SChannelRrr && second == Ktx2Format.Etc1SChannelGgg))
                        throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the ETC1S slices have the channels {first} and {second}; RGB and AAA, or RRR and GGG, are expected."));
                }

                break;
            case Ktx2Encoding.Uastc:
                ValidateBasisSamples(dfd, 16, allowAlphaPlane: false, [Ktx2Format.UastcChannelRgb, Ktx2Format.UastcChannelRgba, Ktx2Format.UastcChannelRrr, Ktx2Format.UastcChannelRrrg, Ktx2Format.UastcChannelRg]);
                break;
            default:
                if (dfd.ColorModel == Ktx2Format.ModelUastcHdr && format.Encoding == Ktx2Encoding.Astc)
                    throw Ktx2Format.Unsupported("The KTX 2.0 file stores UASTC HDR blocks (the color model 167 of basisu, which the KTX 2.0 specification does not define).", "Format: UASTC HDR");

                if (dfd.ColorModel != format.ColorModel)
                    throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the data format descriptor has the color model {dfd.ColorModel}; {format.ColorModel} is expected for {format.Name}."));

                if (dfd.BlockDimensions[0] != format.BlockWidth || dfd.BlockDimensions[1] != format.BlockHeight || dfd.BlockDimensions[2] != 1 || dfd.BlockDimensions[3] != 1)
                    throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the data format descriptor has texel blocks of {dfd.BlockDimensions[0]}x{dfd.BlockDimensions[1]}x{dfd.BlockDimensions[2]}x{dfd.BlockDimensions[3]}; {format.Name} has {format.BlockWidth}x{format.BlockHeight}x1x1."));

                ValidateBytesPlane(dfd, format.BytesPerBlock, 0, scheme);
                if (dfd.Samples.Length != format.Samples.Length)
                    throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the data format descriptor has {dfd.Samples.Length} samples; {format.Name} has {format.Samples.Length}."));

                for (var i = 0; i < dfd.Samples.Length; i++)
                {
                    if (dfd.Samples[i] != format.Samples[i])
                        throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the sample {i} of the data format descriptor is {Describe(dfd.Samples[i])}; {format.Name} has {Describe(format.Samples[i])}."));
                }

                break;
        }

        // Section 3.10.1: the transfer function of the sRGB formats
        if (format.IsSrgb && dfd.Transfer != Ktx2Format.TransferSrgb)
            throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the transfer function is {dfd.Transfer}; {format.Name} is sRGB (2)."));
    }

    private static void ValidateBasisSamples(Dfd dfd, int bytesPerBlock, bool allowAlphaPlane, int[] channels)
    {
        if (dfd.BlockDimensions[0] != 4 || dfd.BlockDimensions[1] != 4 || dfd.BlockDimensions[2] != 1 || dfd.BlockDimensions[3] != 1)
            throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the data format descriptor has texel blocks of {dfd.BlockDimensions[0]}x{dfd.BlockDimensions[1]}; 4x4 is expected."));

        var maxSamples = allowAlphaPlane ? 2 : 1;
        if (dfd.Samples.Length == 0 || dfd.Samples.Length > maxSamples)
            throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the data format descriptor has {dfd.Samples.Length} samples; 1 to {maxSamples} are expected."));

        for (var i = 0; i < dfd.Samples.Length; i++)
        {
            var sample = dfd.Samples[i];
            if (sample.BitOffset != i * bytesPerBlock * 8 * (allowAlphaPlane ? 1 : 0) || sample.BitLength != bytesPerBlock * 8 || (sample.Qualifiers & Ktx2Format.QualifierFloat) != 0)
                throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the sample {i} of the data format descriptor is {Describe(sample)}."));

            ValidateBytesPlane(dfd, bytesPerBlock, i, Ktx2Format.SchemeBasisLz);
        }

        if (Array.IndexOf(channels, dfd.Samples[0].Channel) < 0)
            throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the first sample of the data format descriptor has the channel {dfd.Samples[0].Channel}."));
    }

    private static void ValidateBytesPlane(Dfd dfd, int bytesPerBlock, int plane, uint scheme)
    {
        // Section 3.10.3.1: earlier revisions stored 0 for supercompressed data
        var bytes = dfd.BytesPlane[plane];
        if (bytes != bytesPerBlock && !(bytes == 0 && scheme != Ktx2Format.SchemeNone))
            throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"bytesPlane{plane} is {bytes}; {bytesPerBlock} is expected."));

        for (var i = plane + 1; i < dfd.BytesPlane.Length; i++)
        {
            if (dfd.BytesPlane[i] != 0 && !(i == plane + 1 && dfd.ColorModel == Ktx2Format.ModelEtc1S))
                throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"bytesPlane{i} is {dfd.BytesPlane[i]}; the format has one plane."));
        }
    }

    private static string Describe(Ktx2Sample sample) => string.Create(CultureInfo.InvariantCulture, $"channel {sample.Channel}, {sample.BitLength} bits at {sample.BitOffset}, qualifiers 0x{sample.Qualifiers:X2}");

    // -----------------------------------------------------------------------------------------------------------------
    // Key/value data (section 3.11 and section 5)
    // -----------------------------------------------------------------------------------------------------------------

    private static List<KeyValuePair<string, byte[]>> ReadKeyValues(ReadOnlySpan<byte> data)
    {
        var pairs = new List<KeyValuePair<string, byte[]>>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var position = 0;
        while (position < data.Length)
        {
            if (data.Length - position < 4)
                throw Ktx2Format.Invalid("the key/value data ends inside the length of a pair.");

            var length = BinaryPrimitives.ReadUInt32LittleEndian(data[position..]);
            position += 4;
            if (length < 2 || length > data.Length - position)
                throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"a key/value pair declares {length} bytes; {data.Length - position} remain."));

            var pair = data.Slice(position, (int)length);
            var terminator = pair.IndexOf((byte)0);
            if (terminator <= 0)
                throw Ktx2Format.Invalid("a key of the key/value data is empty or not terminated by NUL.");

            string key;
            try
            {
                key = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(pair[..terminator]);
            }
            catch (DecoderFallbackException)
            {
                throw Ktx2Format.Invalid("a key of the key/value data is not UTF-8.");
            }

            if (!keys.Add(key))
                throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the key '{key}' appears more than once in the key/value data."));

            pairs.Add(new KeyValuePair<string, byte[]>(key, pair[(terminator + 1)..].ToArray()));

            // The pairs are aligned on 4 bytes, padded with zeros counted in kvdByteLength (section 3.11.3)
            var padded = (length + 3) & ~3u;
            if (padded > data.Length - position)
                throw Ktx2Format.Invalid("the padding of the last key/value pair is outside the key/value data.");

            position += (int)padded;
        }

        return pairs;
    }

    private static byte[]? Find(KeyValuePair<string, byte[]>[] pairs, string key)
    {
        foreach (var pair in pairs)
        {
            if (pair.Key == key)
                return pair.Value;
        }

        return null;
    }

    /// <summary>Reads a value that is a UTF-8 string, with or without its NUL terminator.</summary>
    private static string? ReadString(KeyValuePair<string, byte[]>[] pairs, string key)
    {
        var value = Find(pairs, key);
        if (value is null)
            return null;

        var span = value.AsSpan();
        if (span.Length > 0 && span[^1] == 0)
        {
            span = span[..^1];
        }

        return Encoding.UTF8.GetString(span);
    }

    private static string? ReadOrientation(KeyValuePair<string, byte[]>[] pairs, uint height, uint depth)
    {
        // Section 5.2: r or l, then d or u for 2D textures and cube maps, then o or i for 3D textures
        var value = ReadString(pairs, Ktx2Format.OrientationKey);
        if (value is null)
            return null;

        var length = height == 0 ? 1 : depth == 0 ? 2 : 3;
        var valid = value.Length == length && value[0] is 'r' or 'l' && (length < 2 || value[1] is 'd' or 'u') && (length < 3 || value[2] is 'o' or 'i');
        if (!valid)
            throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"KTXorientation is '{value}'; a {length}-letter value of the table of section 5.2 is expected."));

        return value;
    }

    private static string? ReadSwizzle(KeyValuePair<string, byte[]>[] pairs)
    {
        // Section 5.4: four of r, g, b, a, 0, 1
        var value = ReadString(pairs, Ktx2Format.SwizzleKey);
        if (value is null)
            return null;

        if (value.Length != 4 || value.AsSpan().IndexOfAnyExcept("rgba01") >= 0)
            throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"KTXswizzle is '{value}'; four of 'rgba01' are expected."));

        return value;
    }

    private static string? ReadAstcDecodeMode(KeyValuePair<string, byte[]>[] pairs)
    {
        var value = ReadString(pairs, Ktx2Format.AstcDecodeModeKey);
        if (value is not (null or "rgb9e5" or "unorm8"))
            throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"KTXastcDecodeMode is '{value}'; 'rgb9e5' or 'unorm8' is expected."));

        return value;
    }

    private static int? ReadCubemapIncomplete(KeyValuePair<string, byte[]>[] pairs, uint faceCount)
    {
        // Section 5.1: one byte, 00xxxxxx, not 0, and the faces are flattened into layers
        var value = Find(pairs, Ktx2Format.CubemapIncompleteKey);
        if (value is null)
            return null;

        if (value.Length != 1 || value[0] == 0 || (value[0] & 0xC0) != 0 || faceCount != 1)
            throw Ktx2Format.Invalid("KTXcubemapIncomplete is not one byte of the faces of section 5.1, or the faces are not flattened into layers.");

        return value[0];
    }

    private static Ktx2Animation? ReadAnimation(KeyValuePair<string, byte[]>[] pairs, uint layerCount)
    {
        // Section 5.8: duration, timescale and loop count; the layers of an array texture are the frames
        var value = Find(pairs, Ktx2Format.AnimDataKey);
        if (value is null)
            return null;

        if (value.Length != 12)
            throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"KTXanimData has {value.Length} bytes; 12 are expected."));

        var animation = new Ktx2Animation(BinaryPrimitives.ReadUInt32LittleEndian(value), BinaryPrimitives.ReadUInt32LittleEndian(value.AsSpan(4)), BinaryPrimitives.ReadUInt32LittleEndian(value.AsSpan(8)));
        if (animation.Timescale == 0)
            throw Ktx2Format.Invalid("the timescale of KTXanimData is 0.");

        if (layerCount == 0)
            throw Ktx2Format.Invalid("KTXanimData is present but the texture is not an array: its layers are the frames.");

        return animation;
    }

    private static int CeilDiv(int value, int divisor) => (value + divisor - 1) / divisor;

    private static int LeastCommonMultiple(int a, int b)
    {
        var (x, y) = (a, b);
        while (y != 0)
        {
            (x, y) = (y, x % y);
        }

        return a / x * b;
    }
}
