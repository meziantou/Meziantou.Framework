namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The <see cref="ImageCollectionReader"/> of a DDS file: every surface of the texture (each mipmap level of each face of
/// each element of the array, and each depth slice of a level of a volume texture) is an entry, in file order.
/// </summary>
/// <remarks>
/// <para>
/// The headers are read when the collection is loaded; they give the size and the offset of every surface, so the file
/// must be long enough to hold them all. The data of a surface is read only when it is decoded: the headers, then the
/// surface, go through the parser of <c>Image.Load</c>.
/// </para>
/// <para>
/// The number of surfaces counts against <see cref="ImageResourceLimits.MaxFrames"/>.
/// </para>
/// </remarks>
internal sealed class DdsCollectionReader : ImageCollectionReader
{
    private readonly DdsHeader _header;
    private readonly ImageCollectionEntryDescriptor[] _descriptors;
    private readonly (int Mip, long Offset, long Length)[] _surfaces;

    private DdsCollectionReader(RandomAccessInput input, ImageConfiguration configuration, DdsHeader header, ImageCollectionEntryDescriptor[] descriptors, (int Mip, long Offset, long Length)[] surfaces)
        : base(input, configuration)
    {
        _header = header;
        _descriptors = descriptors;
        _surfaces = surfaces;
    }

    public override ImageFormat Format => ImageFormat.Dds;

    /// <summary>Gets the kind: the entries are the surfaces of a texture.</summary>
    public override ImageCollectionKind Kind => ImageCollectionKind.TextureSurfaces;

    public override int Count => _descriptors.Length;

    /// <summary>Reads the headers and locates every surface.</summary>
    /// <param name="input">The random-access input; the reader takes ownership.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The reader.</returns>
    /// <exception cref="InvalidImageContentException">The headers are malformed, or the file does not hold every surface.</exception>
    /// <exception cref="ImageResourceLimitException">The texture has more surfaces than <see cref="ImageResourceLimits.MaxFrames"/>.</exception>
    public static DdsCollectionReader Create(RandomAccessInput input, ImageConfiguration configuration)
    {
        var context = new ImageCodecContext(configuration, "DdsTexture", CancellationToken.None);
        var source = new RandomAccessSource(input, context, ImageFormat.Dds);
        var header = ReadHeader(source, context);
        var available = source.Length - header.Length;
        if (header.DataLength > available)
            throw DdsFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The DDS file is truncated: its surfaces need {header.DataLength} bytes after the headers, and the file has {available}."));

        var count = header.SurfaceCount;
        if (count > configuration.Limits.MaxFrames)
            throw new ImageResourceLimitException(ImageResourceLimitKind.Frames, configuration.Limits.MaxFrames, count);

        var layout = header.Layout;
        var descriptors = new ImageCollectionEntryDescriptor[count];
        var surfaces = new (int Mip, long Offset, long Length)[count];
        for (var i = 0; i < count; i++)
        {
            var (surface, size, offset) = header.GetSurface(i);
            descriptors[i] = new ImageCollectionEntryDescriptor(
                size,
                layout.DefaultPixelFormat,
                layout.ColorModel,
                layout.BitsPerComponent,
                layout.HasAlpha,
                PayloadFormat: ImageFormat.Dds,
                Hotspot: null,
                header.CreateMetadata(),
                surface);
            surfaces[i] = (surface.MipLevel, offset, header.GetSurfaceLength(surface.MipLevel));
        }

        return new DdsCollectionReader(input, configuration, header, descriptors, surfaces);
    }

    public override ImageCollectionEntryDescriptor GetDescriptor(int index) => _descriptors[index];

    protected override Image DecodeCore(int index, RandomAccessSource source, ImageDecodeRequest request, ImageCodecContext context)
    {
        var (mip, offset, length) = _surfaces[index];
        var headerLength = _header.Length;
        var total = headerLength + length;
        if (total > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(context.Limits);

        // The headers, then the surface: the parser reads the surface of the level right after the headers
        using var buffer = context.Scope.Rent((int)total, AllocationKind.Temporary, clear: false);
        var data = buffer.RawBuffer.AsSpan(0, (int)total);
        source.Read(0, data[..headerLength], charge: false);
        source.Read(headerLength + offset, data[headerLength..], charge: false);
        using var parser = DdsCodec.CreateDecodeParser(request, context, mip);
        return ImageInputPump.Run(data, parser, context);
    }

    private static DdsHeader ReadHeader(RandomAccessSource source, ImageCodecContext context)
    {
        if (source.Length < DdsFormat.HeaderLength)
            throw DdsFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The DDS data is truncated: the headers need {DdsFormat.HeaderLength} bytes, and the file has {source.Length}."));

        Span<byte> header = stackalloc byte[DdsFormat.HeaderLength + DdsFormat.Dx10HeaderLength];
        source.Read(0, header[..DdsFormat.HeaderLength]);
        if (!DdsFormat.MatchesSignature(header))
            throw DdsFormat.Invalid("The DDS magic number 'DDS ' is missing.");

        var length = DdsHeader.GetLength(header);
        if (length > DdsFormat.HeaderLength)
        {
            if (source.Length < length)
                throw DdsFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The DDS data is truncated: the headers need {length} bytes, and the file has {source.Length}."));

            source.Read(DdsFormat.HeaderLength, header[DdsFormat.HeaderLength..length]);
        }

        return DdsHeader.Parse(header[..length], context);
    }
}
