using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The container-specific half of a loaded <see cref="ImageCollection"/>: it owns the random-access input, describes every
/// entry without decoding it, and decodes one entry on demand.
/// </summary>
/// <remarks>
/// Every decode runs in its own <see cref="ImageCodecContext"/>: its own per-input accounting (frames, pixels, encoded and
/// metadata bytes) and its own allocation scope, which the returned image keeps. Decoding the 400th page of a document
/// therefore costs exactly what decoding the first one costs, and the limits of one entry are never consumed by the
/// entries decoded before it.
/// </remarks>
internal abstract class ImageCollectionReader : IDisposable
{
    private readonly RandomAccessInput _input;
    private bool _disposed;

    private protected ImageCollectionReader(RandomAccessInput input, ImageConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(configuration);
        _input = input;
        Configuration = configuration;
    }

    /// <summary>Gets the container format.</summary>
    public abstract ImageFormat Format { get; }

    /// <summary>Gets what the entries are: document pages, or alternative representations of one drawing.</summary>
    public abstract ImageCollectionKind Kind { get; }

    /// <summary>Gets the number of entries.</summary>
    public abstract int Count { get; }

    /// <summary>Gets the configuration the collection was loaded with.</summary>
    public ImageConfiguration Configuration { get; }

    /// <summary>Gets the description of one entry.</summary>
    /// <param name="index">The index of the entry.</param>
    /// <returns>The description.</returns>
    public abstract ImageCollectionEntryDescriptor GetDescriptor(int index);

    /// <summary>Decodes one entry into a new owned image.</summary>
    /// <param name="index">The index of the entry.</param>
    /// <param name="options">The decode options.</param>
    /// <param name="pixelFormat">The requested pixel format, or <see langword="null"/> for the default working representation of the entry.</param>
    /// <returns>A new image owned by the caller.</returns>
    public Image Decode(int index, ImageDecodeOptions options, PixelFormat? pixelFormat)
    {
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var context = new ImageCodecContext(options.Configuration, "Image", CancellationToken.None);
        return DecodeCore(index, new RandomAccessSource(_input, context, Format), ImageDecodeRequest.Create(options, pixelFormat), context);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _input.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Decodes one entry; the context and the source belong to this call only.</summary>
    protected abstract Image DecodeCore(int index, RandomAccessSource source, ImageDecodeRequest request, ImageCodecContext context);
}
