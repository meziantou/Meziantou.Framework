using System.Collections.ObjectModel;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// A disposable collection of images that are not animation frames: the pages of a document (TIFF, BigTIFF) or the
/// alternative representations of a drawing (ICO, CUR). Entries may differ in size, pixel format and metadata, and are
/// never resized or converted implicitly.
/// </summary>
/// <remarks>
/// <para>
/// This type is deliberately separate from <see cref="Image"/>, whose frames are full-canvas displayed frames of one
/// animation with a shared size and pixel format. Nothing converts one into the
/// other implicitly: <see cref="ImageCollectionEntry.Decode(ImageDecodeOptions)"/> extracts an entry into a normal, independently owned
/// <see cref="Image"/>, and <see cref="Add(Image, Point?)"/> copies an image into the collection.
/// </para>
/// <para>
/// <strong>Loading is lazy and random access.</strong> <c>Load</c> reads only the directory structure of the container,
/// so describing every entry costs a few kilobytes of reads whatever the size of the file; the pixels of an entry are
/// read when that entry is decoded, each decode with its own limits and allocation scope. A collection therefore keeps
/// its input open until it is disposed:
/// </para>
/// <list type="bullet">
/// <item><description><see cref="Load(string, ImageConfiguration?)"/> keeps the file open for reading until the collection is disposed.</description></item>
/// <item><description><see cref="Load(Stream, ImageConfiguration?)"/> needs a seekable stream, which it keeps open (it does not own it) and seeks freely: the stream position after any call is unspecified. A non-seekable stream is rejected; read it into memory yourself if you must.</description></item>
/// <item><description><see cref="Load(ReadOnlySpan{byte}, ImageConfiguration?)"/> copies the data once, because the collection cannot retain a span.</description></item>
/// </list>
/// <para>
/// <strong>Loading is synchronous.</strong> A random-access decoder issues many small reads whose offsets depend on the
/// bytes just read, so an asynchronous variant would either await every tag or buffer the whole file. The asynchronous
/// <c>Image.LoadAsync</c> and <c>Image.IdentifyAsync</c> entry points do buffer the whole input for these formats, which
/// is why a large document should be read through this type.
/// </para>
/// </remarks>
public sealed class ImageCollection : IDisposable
{
    private readonly List<ImageCollectionEntry> _entries = [];
    private readonly ReadOnlyCollection<ImageCollectionEntry> _readOnlyEntries;
    private readonly ImageCollectionReader? _reader;
    private bool _disposed;

    private ImageCollection(ImageCollectionReader? reader, ImageFormat format, ImageCollectionKind kind, ImageConfiguration configuration)
    {
        _reader = reader;
        _readOnlyEntries = _entries.AsReadOnly();
        Format = format;
        Kind = kind;
        Configuration = configuration;
        if (reader is not null)
        {
            for (var i = 0; i < reader.Count; i++)
            {
                _entries.Add(new ImageCollectionEntry(this, i, reader.GetDescriptor(i)));
            }
        }
    }

    /// <summary>Gets the format the collection was loaded from, or <see cref="ImageFormat.Unknown"/> for a collection created in memory.</summary>
    public ImageFormat Format { get; }

    /// <summary>Gets what the entries are: document pages or alternative representations.</summary>
    public ImageCollectionKind Kind { get; }

    /// <summary>Gets the configuration used to load the collection and to decode its entries by default.</summary>
    public ImageConfiguration Configuration { get; }

    /// <summary>Gets the number of entries.</summary>
    public int Count
    {
        get
        {
            ThrowIfDisposed();
            return _entries.Count;
        }
    }

    /// <summary>Gets the entries, in container order.</summary>
    /// <remarks>Entries are borrowed from the collection: they are valid until it is disposed or they are removed.</remarks>
    public IReadOnlyList<ImageCollectionEntry> Entries
    {
        get
        {
            ThrowIfDisposed();
            return _readOnlyEntries;
        }
    }

    /// <summary>Gets the entry at an index.</summary>
    /// <param name="index">The zero-based index.</param>
    /// <returns>The entry.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the collection.</exception>
    public ImageCollectionEntry this[int index]
    {
        get
        {
            ThrowIfDisposed();
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _entries.Count);
            return _entries[index];
        }
    }

    /// <summary>Creates an empty collection to be filled with images and saved.</summary>
    /// <param name="kind">What the entries will be: <see cref="ImageCollectionKind.Pages"/> for a document, <see cref="ImageCollectionKind.Representations"/> for an icon or a cursor.</param>
    /// <param name="configuration">The configuration, or <see langword="null"/> for <see cref="ImageConfiguration.Default"/>.</param>
    /// <returns>An empty collection owned by the caller.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not <see cref="ImageCollectionKind.Pages"/> or <see cref="ImageCollectionKind.Representations"/>.</exception>
    public static ImageCollection Create(ImageCollectionKind kind, ImageConfiguration? configuration = null)
    {
        if (kind is not (ImageCollectionKind.Pages or ImageCollectionKind.Representations))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "A collection is created for pages or for representations.");

        return new ImageCollection(reader: null, ImageFormat.Unknown, kind, configuration ?? ImageConfiguration.Default);
    }

    /// <summary>Opens a multi-entry image file and describes its entries without decoding any of them.</summary>
    /// <param name="path">The path of the file. It stays open for reading until the collection is disposed.</param>
    /// <param name="configuration">The configuration, or <see langword="null"/> for <see cref="ImageConfiguration.Default"/>.</param>
    /// <returns>The collection, owned by the caller.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty.</exception>
    /// <exception cref="UnknownImageFormatException">The format is not recognized.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The format is recognized but stores a single image, or the file uses an unsupported feature.</exception>
    /// <exception cref="InvalidImageContentException">The structure of the file is malformed or truncated.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of the input is transferred to Create, which disposes it when the reader cannot be built and otherwise hands it to the collection.")]
    public static ImageCollection Load(string path, ImageConfiguration? configuration = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            BufferSize = 0,
            Options = FileOptions.RandomAccess,
        });

        try
        {
            return Create(RandomAccessInput.Create(stream, origin: 0, ownsStream: true), configuration);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Opens a multi-entry image from a seekable stream, starting at its current position.</summary>
    /// <param name="stream">The readable, seekable stream. It is left open and is seeked freely: its position after any call is unspecified.</param>
    /// <param name="configuration">The configuration, or <see langword="null"/> for <see cref="ImageConfiguration.Default"/>.</param>
    /// <returns>The collection, owned by the caller.</returns>
    /// <exception cref="ArgumentException"><paramref name="stream"/> is not readable, or is not seekable.</exception>
    /// <exception cref="UnknownImageFormatException">The format is not recognized.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The format is recognized but stores a single image.</exception>
    /// <exception cref="InvalidImageContentException">The structure of the data is malformed or truncated.</exception>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of the input is transferred to Create, which disposes it when the reader cannot be built and otherwise hands it to the collection.")]
    public static ImageCollection Load(Stream stream, ImageConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new ArgumentException("The stream is not readable.", nameof(stream));

        if (!stream.CanSeek)
            throw new ArgumentException("The stream is not seekable. A page or representation is located by a file offset, so a collection cannot be read forward only; read the data into memory and use the span overload.", nameof(stream));

        return Create(RandomAccessInput.Create(stream, stream.Position, ownsStream: false), configuration);
    }

    /// <summary>Opens a multi-entry image from in-memory encoded data.</summary>
    /// <param name="data">The encoded data. It is copied once: the collection outlives the call and cannot retain a span.</param>
    /// <param name="configuration">The configuration, or <see langword="null"/> for <see cref="ImageConfiguration.Default"/>.</param>
    /// <returns>The collection, owned by the caller.</returns>
    /// <exception cref="UnknownImageFormatException">The format is not recognized.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The format is recognized but stores a single image.</exception>
    /// <exception cref="InvalidImageContentException">The structure of the data is malformed or truncated.</exception>
    /// <exception cref="ImageResourceLimitException">The data is longer than <see cref="ImageResourceLimits.MaxEncodedBytes"/>.</exception>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of the input is transferred to Create, which disposes it when the reader cannot be built and otherwise hands it to the collection.")]
    public static ImageCollection Load(ReadOnlySpan<byte> data, ImageConfiguration? configuration = null)
    {
        configuration ??= ImageConfiguration.Default;
        if (data.Length > configuration.Limits.MaxEncodedBytes)
            throw new ImageResourceLimitException(ImageResourceLimitKind.EncodedBytes, configuration.Limits.MaxEncodedBytes, data.Length);

        return Create(RandomAccessInput.Create(data.ToArray()), configuration);
    }

    /// <summary>
    /// Selects the representation that best matches a requested size: the smallest entry at least as large as
    /// <paramref name="size"/> in both directions, or, when none is large enough, the largest entry. Ties are broken by
    /// the highest stored bit depth, then by container order.
    /// </summary>
    /// <param name="size">The requested size, in pixels.</param>
    /// <returns>The selected entry. Nothing is decoded or resized.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive.</exception>
    /// <exception cref="InvalidOperationException">The collection is empty.</exception>
    public ImageCollectionEntry SelectBySize(Size size)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size.Height);
        if (_entries.Count == 0)
            throw new InvalidOperationException("The collection is empty.");

        ImageCollectionEntry? best = null;
        var bestIsLargeEnough = false;
        foreach (var entry in _entries)
        {
            var isLargeEnough = entry.Size.Width >= size.Width && entry.Size.Height >= size.Height;
            if (best is null || IsBetter(entry, isLargeEnough, best, bestIsLargeEnough))
            {
                best = entry;
                bestIsLargeEnough = isLargeEnough;
            }
        }

        return best!;

        static bool IsBetter(ImageCollectionEntry candidate, bool candidateIsLargeEnough, ImageCollectionEntry best, bool bestIsLargeEnough)
        {
            if (candidateIsLargeEnough != bestIsLargeEnough)
                return candidateIsLargeEnough;

            var candidateArea = (long)candidate.Size.Width * candidate.Size.Height;
            var bestArea = (long)best.Size.Width * best.Size.Height;
            if (candidateArea != bestArea)
                return candidateIsLargeEnough ? candidateArea < bestArea : candidateArea > bestArea;

            return candidate.BitsPerComponent > best.BitsPerComponent;
        }
    }

    /// <summary>Appends a copy of an image as the last entry.</summary>
    /// <param name="image">The image. It is copied; the caller keeps its own image and disposes it.</param>
    /// <param name="hotspot">The hotspot of a cursor representation, or <see langword="null"/>.</param>
    /// <returns>The new entry.</returns>
    /// <exception cref="ArgumentException"><paramref name="image"/> is animated or has a poster frame: a page or a representation is a still image.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="hotspot"/> is outside the image.</exception>
    /// <exception cref="ObjectDisposedException">The collection or the image is disposed.</exception>
    public ImageCollectionEntry Add(Image image, Point? hotspot = null)
    {
        var entry = CreateOwnedEntry(image, hotspot);
        _entries.Add(entry);
        return entry;
    }

    /// <summary>Inserts a copy of an image at an index.</summary>
    /// <param name="index">The zero-based index, from 0 to <see cref="Count"/>.</param>
    /// <param name="image">The image. It is copied.</param>
    /// <param name="hotspot">The hotspot of a cursor representation, or <see langword="null"/>.</param>
    /// <returns>The new entry.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the collection, or the hotspot is outside the image.</exception>
    /// <exception cref="ArgumentException"><paramref name="image"/> is animated or has a poster frame.</exception>
    public ImageCollectionEntry Insert(int index, Image image, Point? hotspot = null)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _entries.Count);
        var entry = CreateOwnedEntry(image, hotspot);
        _entries.Insert(index, entry);
        return entry;
    }

    /// <summary>Removes an entry. An entry that owns a copied image releases it.</summary>
    /// <param name="index">The zero-based index.</param>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the collection.</exception>
    public void RemoveAt(int index)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _entries.Count);
        var entry = _entries[index];
        _entries.RemoveAt(index);
        entry.DisposeOwned();
    }

    /// <summary>Moves an entry to another index, shifting the entries in between.</summary>
    /// <param name="sourceIndex">The index of the entry to move.</param>
    /// <param name="destinationIndex">The index it must have afterward.</param>
    /// <exception cref="ArgumentOutOfRangeException">An index is outside the collection.</exception>
    public void Move(int sourceIndex, int destinationIndex)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(sourceIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(sourceIndex, _entries.Count);
        ArgumentOutOfRangeException.ThrowIfNegative(destinationIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(destinationIndex, _entries.Count);
        if (sourceIndex == destinationIndex)
            return;

        var entry = _entries[sourceIndex];
        _entries.RemoveAt(sourceIndex);
        _entries.Insert(destinationIndex, entry);
    }

    /// <summary>Encodes the collection to a file, published atomically.</summary>
    /// <param name="path">The destination path.</param>
    /// <param name="encoder">The encoder, or <see langword="null"/> to select it from the extension (<c>.tif</c>, <c>.tiff</c>, <c>.ico</c>, <c>.cur</c>).</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty, or the extension does not select a multi-entry encoder.</exception>
    /// <exception cref="InvalidOperationException">The collection is empty.</exception>
    /// <exception cref="UnsupportedImageFeatureException">An entry cannot be encoded without a loss the encoder settings do not allow.</exception>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    public void Save(string path, ImageEncoder? encoder = null)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(path);
        encoder ??= ImageEncoder.FromPath(path);
        ImageCollectionWriterCore.Save(this, path, encoder);
    }

    /// <summary>Encodes the collection to a stream, starting at its current position. The stream is left open.</summary>
    /// <param name="stream">The writable destination. TIFF output also requires a seekable stream (the directory offsets are patched once the pages are written).</param>
    /// <param name="encoder">The encoder.</param>
    /// <exception cref="ArgumentException"><paramref name="stream"/> is not writable, is not seekable when the output requires it, or the encoder does not produce a multi-entry format.</exception>
    /// <exception cref="InvalidOperationException">The collection is empty.</exception>
    /// <exception cref="UnsupportedImageFeatureException">An entry cannot be encoded without a loss the encoder settings do not allow.</exception>
    public void Save(Stream stream, ImageEncoder encoder)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(encoder);
        if (!stream.CanWrite)
            throw new ArgumentException("The stream is not writable.", nameof(stream));

        ImageCollectionWriterCore.Save(this, stream, encoder);
    }

    /// <summary>
    /// Releases the input of a loaded collection and the images of the entries that own one. Images returned by
    /// <see cref="ImageCollectionEntry.Decode(ImageDecodeOptions)"/> are owned by their caller and are not affected.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (var entry in _entries)
        {
            entry.DisposeOwned();
        }

        _entries.Clear();
        _reader?.Dispose();
    }

    /// <summary>Decodes an entry backed by the encoded input.</summary>
    internal Image DecodeSourceEntry(int sourceIndex, ImageDecodeOptions options, PixelFormat? pixelFormat)
    {
        var reader = _reader ?? throw new InvalidOperationException("The collection has no encoded input.");
        return reader.Decode(sourceIndex, options, pixelFormat);
    }

    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static ImageCollection Create(RandomAccessInput input, ImageConfiguration? configuration)
    {
        configuration ??= ImageConfiguration.Default;
        ImageCollectionReader reader;
        try
        {
            reader = CreateReader(input, configuration);
        }
        catch
        {
            input.Dispose();
            throw;
        }

        try
        {
            return new ImageCollection(reader, reader.Format, reader.Kind, configuration);
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    private static ImageCollectionReader CreateReader(RandomAccessInput input, ImageConfiguration configuration)
    {
        Span<byte> prefix = stackalloc byte[Image.FormatDetectionPrefixLength];
        var available = (int)Math.Min(prefix.Length, input.Length);
        input.Read(0, prefix[..available]);
        prefix[available..].Clear();
        if (TiffHeader.MatchesSignature(prefix[..available]))
            return TiffCollectionReader.Create(input, configuration);

        if (IcoFormat.MatchesSignature(prefix[..available], IcoFormat.IconType) || IcoFormat.MatchesSignature(prefix[..available], IcoFormat.CursorType))
            return IcoCollectionReader.Create(input, configuration);

        var format = Image.DetectFormat(prefix[..available]);
        if (format != ImageFormat.Unknown)
            throw new UnsupportedImageFeatureException($"{ImageFormatNames.Get(format)} stores a single image, not a collection of pages or representations. Use Image.Load instead.", format, "Image collection");

        throw new UnknownImageFormatException("The image format is not recognized: the data does not start with the signature of a supported multi-entry format (TIFF, BigTIFF, ICO or CUR).");
    }

    private ImageCollectionEntry CreateOwnedEntry(Image image, Point? hotspot)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(image);
        if (image.IsAnimated || image.PosterFrame is not null || image.Frames.Count != 1)
            throw new ArgumentException("A page or a representation is a still image; extract a frame with CloneFrame or remove the animation settings first.", nameof(image));

        if (hotspot is { } point && (point.X < 0 || point.Y < 0 || point.X >= image.Width || point.Y >= image.Height))
            throw new ArgumentOutOfRangeException(nameof(hotspot), hotspot, "The hotspot is outside the image.");

        return new ImageCollectionEntry(this, image.Clone(), hotspot);
    }
}
