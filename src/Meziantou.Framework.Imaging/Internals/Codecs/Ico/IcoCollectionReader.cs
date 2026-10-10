namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The <see cref="ImageCollectionReader"/> of an icon or cursor file: the representations are resolved from the directory
/// when the collection is loaded, and the payload of a representation is read only when it is decoded
///.
/// </summary>
internal sealed class IcoCollectionReader : ImageCollectionReader
{
    private readonly IcoRepresentation[] _representations;

    private IcoCollectionReader(RandomAccessInput input, ImageConfiguration configuration, ImageFormat format, IcoRepresentation[] representations)
        : base(input, configuration)
    {
        Format = format;
        _representations = representations;
    }

    public override ImageFormat Format { get; }

    /// <summary>Gets the kind: the entries of an icon directory are alternative representations, never animation frames.</summary>
    public override ImageCollectionKind Kind => ImageCollectionKind.Representations;

    public override int Count => _representations.Length;

    /// <summary>Reads the directory and resolves every representation.</summary>
    /// <param name="input">The random-access input; the reader takes ownership.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The reader.</returns>
    public static IcoCollectionReader Create(RandomAccessInput input, ImageConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(configuration);
        var context = new ImageCodecContext(configuration, "IconDirectory", CancellationToken.None);
        var source = new RandomAccessSource(input, context, ImageFormat.Ico);
        var (format, representations) = IcoDirectory.Read(source, context);
        return new IcoCollectionReader(input, configuration, format, representations);
    }

    public override ImageCollectionEntryDescriptor GetDescriptor(int index)
    {
        var representation = _representations[index];
        return new ImageCollectionEntryDescriptor(
            representation.Size,
            representation.PixelFormat,
            representation.ColorModel,
            representation.BitsPerComponent,
            representation.MayHaveTransparency,
            representation.PayloadFormat,
            representation.Hotspot,
            representation.Metadata);
    }

    protected override Image DecodeCore(int index, RandomAccessSource source, ImageDecodeRequest request, ImageCodecContext context)
        => IcoEntryDecoder.Decode(source, Format, _representations[index], request, context);
}
