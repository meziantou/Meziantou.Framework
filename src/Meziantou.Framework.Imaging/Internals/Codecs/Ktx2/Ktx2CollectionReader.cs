using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The <see cref="ImageCollectionReader"/> of a KTX 2.0 file: its subresources, ordered by layer, face, level and depth
/// slice, are described from the structure of the file, and the texels of a subresource are read when it is decoded.
/// </summary>
internal sealed class Ktx2CollectionReader : ImageCollectionReader
{
    private readonly Ktx2Texture _texture;
    private readonly Ktx2Subresource[] _subresources;
    private readonly Ktx2Output _output;
    private readonly ImageMetadata _metadata;

    private Ktx2CollectionReader(RandomAccessInput input, ImageConfiguration configuration, Ktx2Texture texture, Ktx2Subresource[] subresources)
        : base(input, configuration)
    {
        _texture = texture;
        _subresources = subresources;
        _output = Ktx2Output.Create(texture);
        _metadata = Ktx2Output.CreateMetadata(texture);
    }

    public override ImageFormat Format => ImageFormat.Ktx2;

    /// <summary>Gets the kind: the subresources of a texture.</summary>
    public override ImageCollectionKind Kind => ImageCollectionKind.TextureSurfaces;

    public override int Count => _subresources.Length;

    /// <summary>Reads the structure of the file and lists its subresources.</summary>
    /// <exception cref="ImageResourceLimitException">The texture has more subresources than <see cref="ImageResourceLimits.MaxFrames"/>.</exception>
    public static Ktx2CollectionReader Create(RandomAccessInput input, ImageConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(configuration);
        var context = new ImageCodecContext(configuration, "Texture", CancellationToken.None);
        var source = new RandomAccessSource(input, context, ImageFormat.Ktx2);
        var texture = Ktx2Texture.Read(source, context);
        Ktx2Decoder.EnsureDecodable(texture);
        var count = texture.SubresourceCount;
        if (count > configuration.Limits.MaxFrames)
            throw new ImageResourceLimitException(ImageResourceLimitKind.Frames, configuration.Limits.MaxFrames, count);

        return new Ktx2CollectionReader(input, configuration, texture, [.. texture.EnumerateSubresources()]);
    }

    public override ImageCollectionEntryDescriptor GetDescriptor(int index)
    {
        var subresource = _subresources[index];
        var size = new Size(_texture.GetLevelWidth(subresource.MipLevel), _texture.GetLevelHeight(subresource.MipLevel));
        return new ImageCollectionEntryDescriptor(size, _output.DefaultPixelFormat, _output.ColorModel, _output.BitsPerComponent, _output.MayHaveTransparency, ImageFormat.Ktx2, Hotspot: null, _metadata.Clone(), subresource.ToTextureSurface(_texture.FaceCount == 6));
    }

    protected override Image DecodeCore(int index, RandomAccessSource source, ImageDecodeRequest request, ImageCodecContext context)
    {
        // Each decode reads the structure again, with its own accounting
        var texture = Ktx2Texture.Read(source, context);
        return Ktx2Decoder.DecodeCollectionEntry(source, texture, _subresources[index], request, context);
    }
}
