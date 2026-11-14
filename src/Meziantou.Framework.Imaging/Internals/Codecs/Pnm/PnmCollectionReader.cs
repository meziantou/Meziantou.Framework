namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The <see cref="ImageCollectionReader"/> of a Netpbm file: the images of a concatenation (PBM, PGM, PPM and PAM images
/// one after the other, of any variant) are the pages of a document.
/// </summary>
/// <remarks>
/// <para>
/// The images are located when the collection is loaded: the header of each image is read, the raster of a binary image is
/// skipped by its length (its rows have a fixed length), and the raster of a plain image is scanned, since its numbers have
/// no fixed length. The pixels of an image are read only when it is decoded.
/// </para>
/// <para>
/// White space between two images and after the last one is skipped, as Netpbm reads a concatenation
/// (<c>pnm_nextimage</c>); any other byte after an image must start the next one.
/// </para>
/// </remarks>
internal sealed class PnmCollectionReader : ImageCollectionReader
{
    private const int ScanChunkLength = 64 * 1024;

    private readonly Entry[] _entries;

    private PnmCollectionReader(RandomAccessInput input, ImageConfiguration configuration, Entry[] entries)
        : base(input, configuration)
        => _entries = entries;

    public override ImageFormat Format => ImageFormat.Pnm;

    /// <summary>Gets the kind: the images of a concatenation are the pages of a document, never animation frames.</summary>
    public override ImageCollectionKind Kind => ImageCollectionKind.Pages;

    public override int Count => _entries.Length;

    /// <summary>Locates every image of a concatenation.</summary>
    /// <param name="input">The random-access input; the reader takes ownership.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The reader.</returns>
    /// <exception cref="ImageResourceLimitException">The file has more images than <see cref="ImageResourceLimits.MaxFrames"/>.</exception>
    public static PnmCollectionReader Create(RandomAccessInput input, ImageConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(configuration);
        var context = new ImageCodecContext(configuration, "PnmDocument", CancellationToken.None);
        var source = new RandomAccessSource(input, context, ImageFormat.Pnm);
        var entries = new List<Entry>();
        var offset = 0L;
        Span<byte> next = stackalloc byte[1];
        do
        {
            if (entries.Count >= configuration.Limits.MaxFrames)
                throw new ImageResourceLimitException(ImageResourceLimitKind.Frames, configuration.Limits.MaxFrames, (long)configuration.Limits.MaxFrames + 1);

            var (info, header, headerLength) = Walk(source, offset, StructureWalk.Header, context);
            var length = headerLength;
            if (header.IsPlain)
            {
                (info, _, length) = Walk(source, offset, StructureWalk.FullScan, context);
            }
            else
            {
                var rasterLength = (long)header.StoredRowLength * header.Height;
                if (rasterLength > source.Length - offset - headerLength)
                    throw PnmFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The Netpbm image {entries.Count} is truncated: its raster needs {rasterLength} bytes after its header, and the file has {source.Length - offset - headerLength}."));

                length += rasterLength;
            }

            entries.Add(new Entry(offset, length, info, header.HasAlpha));
            offset += length;
            while (offset < source.Length)
            {
                source.Read(offset, next);
                if (!PnmFormat.IsWhiteSpace(next[0]))
                    break;

                offset++;
            }
        }
        while (offset < source.Length);

        return new PnmCollectionReader(input, configuration, [.. entries]);
    }

    public override ImageCollectionEntryDescriptor GetDescriptor(int index)
    {
        var info = _entries[index].Info;
        return new ImageCollectionEntryDescriptor(
            info.Size,
            info.PixelFormat,
            info.ColorModel,
            info.BitsPerComponent,
            _entries[index].HasAlpha,
            PayloadFormat: ImageFormat.Pnm,
            Hotspot: null,
            info.Metadata);
    }

    protected override Image DecodeCore(int index, RandomAccessSource source, ImageDecodeRequest request, ImageCodecContext context)
    {
        var entry = _entries[index];

        // The parser charges the bytes it reads itself; an input held in memory is decoded where it is
        PooledBuffer? copy = null;
        if (!source.TryGetSpan(entry.Offset, entry.Length, out var data, charge: false))
        {
            copy = source.ReadToBuffer(entry.Offset, entry.Length, charge: false);
            data = copy.Span;
        }

        using var owner = copy;
        using var parser = PnmCodec.Instance.CreateDecodeParser(request, context);
        return ImageInputPump.Run(data, parser, context);
    }

    /// <summary>Runs a structure walk of the image at <paramref name="offset"/>, reading the input in bounded chunks.</summary>
    /// <returns>The description of the image, its header, and the number of bytes the walk consumed.</returns>
    private static (ImageInfo Info, PnmImageHeader Header, long Length) Walk(RandomAccessSource source, long offset, StructureWalk walk, ImageCodecContext context)
    {
        using var parser = new PnmStructureParser(context, walk);
        var position = offset;
        var required = 1;
        while (true)
        {
            var available = source.Length - position;
            var length = (int)Math.Min(available, Math.Max(ScanChunkLength, required));
            if (length < required)
                throw parser.CreateTruncatedException(position - offset);

            // Only the bytes the parser consumes are charged: the rest of the chunk is read again by the next step
            ParseStatus status;
            int consumed;
            using (var chunk = source.ReadToBuffer(position, length, charge: false))
            {
                status = parser.Parse(chunk.Span, isEndOfInput: length == available, out consumed);
            }

            context.Tracker.ChargeEncodedBytes(consumed);
            position += consumed;
            if (status.IsComplete)
                return (parser.GetResult(), parser.Header, position - offset);

            if (consumed == 0 && status.RequiredBytes <= length)
                throw new InvalidOperationException("The Netpbm parser made no progress.");

            required = consumed == 0 ? status.RequiredBytes : 1;
        }
    }

    private sealed record Entry(long Offset, long Length, ImageInfo Info, bool HasAlpha);
}
