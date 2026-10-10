using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The implementation of <see cref="ImageCollection.Save(string, ImageEncoder?)"/>: it
/// validates the encoder and the destination, decodes or copies one entry at a time, and drives the multi-entry writer of
/// the output format.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Everything that does not depend on pixels is validated before the destination is created: the encoder must produce a
/// multi-entry format, the collection must not be empty, its kind must match the output (pages for TIFF, representations
/// for ICO and CUR), and TIFF needs a seekable destination.
/// </description></item>
/// <item><description>
/// Entries are materialized one at a time: an entry backed by encoded data is decoded when its turn comes and released
/// right after, so saving a 400-page document never holds more than one page in memory. An entry that owns an image is
/// borrowed, not copied.
/// </description></item>
/// <item><description>
/// A path destination is published atomically, like every other save: a failure leaves no partial file behind.
/// </description></item>
/// </list>
/// </remarks>
internal static class ImageCollectionWriterCore
{
    public static void Save(ImageCollection collection, string path, ImageEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(encoder);
        Validate(collection, encoder, seekable: true);
        var file = AtomicFileOutput.Create(path, asynchronous: false);
        try
        {
            Write(collection, encoder, file.Stream);
            file.Stream.Flush();
            file.Publish();
        }
        finally
        {
            // A failure leaves no partial file behind: the temporary file is removed and nothing is published
            file.Abort();
        }
    }

    public static void Save(ImageCollection collection, Stream stream, ImageEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(encoder);
        Validate(collection, encoder, stream.CanSeek);
        Write(collection, encoder, stream);
        stream.Flush();
    }

    private static void Validate(ImageCollection collection, ImageEncoder encoder, bool seekable)
    {
        var expectedKind = encoder switch
        {
            TiffEncoder => ImageCollectionKind.Pages,
            IcoEncoder => ImageCollectionKind.Representations,
            _ => throw new ArgumentException($"The {encoder.GetType().Name} encoder writes a single image; use Image.Save. Only TiffEncoder (pages) and IcoEncoder (representations) write a collection.", nameof(encoder)),
        };

        if (collection.Count == 0)
            throw new InvalidOperationException("The collection is empty; there is nothing to save.");

        if (collection.Kind != expectedKind)
            throw new ArgumentException($"{ImageFormatNames.Get(encoder.Format)} output stores {expectedKind}, but the collection holds {collection.Kind}.", nameof(encoder));

        if (encoder is TiffEncoder && !seekable)
            throw new ArgumentException("TIFF output requires a seekable destination: a directory stores the offsets of the strips it describes, so the pointer to it is written only once the pages are encoded.", nameof(encoder));
    }

    private static void Write(ImageCollection collection, ImageEncoder encoder, Stream stream)
    {
        var configuration = collection.Configuration;
        var scope = AllocationScope.Create(configuration, "ImageCollectionWriter");
        using var output = new ImageOutputBuffer(scope);
        var startPosition = stream.CanSeek ? stream.Position : 0;
        if (encoder is TiffEncoder tiff)
        {
            WriteTiff(collection, tiff, output, scope, stream);
        }
        else
        {
            WriteIcon(collection, (IcoEncoder)encoder, output, scope, configuration);
        }

        output.FlushTo(stream);
        ApplyPatches(output, stream, startPosition);
    }

    private static void WriteTiff(ImageCollection collection, TiffEncoder encoder, ImageOutputBuffer output, AllocationScope scope, Stream stream)
    {
        using var writer = new TiffDocumentWriter(encoder, scope);
        writer.WriteHeader(output);
        for (var i = 0; i < collection.Count; i++)
        {
            using var page = Materialize(collection[i], configuration: collection.Configuration);
            var image = page.Image;
            var frame = image.Frames[0];
            var metadata = MetadataWritePlan.Create(image.Metadata, ImageFormat.Tiff, encoder.MetadataHandling, image.Size, image.PixelFormat);
            writer.BeginPage(image.Size, image.PixelFormat, metadata);
            while (!writer.EncodeStrip(output, frame, CancellationToken.None))
            {
                FlushIfNeeded(output, stream);
            }

            writer.CompletePage(output);
            FlushIfNeeded(output, stream);
        }
    }

    private static void WriteIcon(ImageCollection collection, IcoEncoder encoder, ImageOutputBuffer output, AllocationScope scope, ImageConfiguration configuration)
    {
        // Each payload is encoded as its image is materialized, so only one representation is alive at a time; the
        // encoded payloads are then small and bounded (a representation is at most 256x256 pixels)
        var encoded = new List<IcoDocumentWriter.EncodedEntry>(collection.Count);
        try
        {
            for (var i = 0; i < collection.Count; i++)
            {
                var entry = collection[i];
                using var item = Materialize(entry, configuration);
                var image = item.Image;
                IcoDocumentWriter.ValidateSize(encoder.Format, image.Size);

                // An icon directory stores no metadata: the policy decides whether unstorable metadata is an error
                _ = MetadataWritePlan.Create(image.Metadata, encoder.Format, encoder.MetadataHandling, image.Size, image.PixelFormat);
                if (encoder.Kind == IconKind.Icon && entry.Hotspot is not null && encoder.MetadataHandling == MetadataHandling.Strict)
                    throw new UnsupportedImageFeatureException("An icon directory cannot store a hotspot. Set IcoEncoder.Kind to Cursor, remove the hotspot, or set MetadataHandling to DiscardUnsupported.", encoder.Format, "Metadata: cursor hotspot");

                encoded.Add(IcoDocumentWriter.EncodeEntry(encoder, new IcoDocumentWriter.Entry(image.Frames[0], image.PixelFormat, entry.Hotspot), scope, configuration));
            }

            IcoDocumentWriter.WriteEncoded(encoder, encoded, output);
        }
        finally
        {
            foreach (var item in encoded)
            {
                item.Payload.Dispose();
            }
        }
    }

    private static void FlushIfNeeded(ImageOutputBuffer output, Stream stream)
    {
        if (output.ShouldFlush)
        {
            output.FlushTo(stream);
        }
    }

    private static void ApplyPatches(ImageOutputBuffer output, Stream stream, long startPosition)
    {
        if (output.Patches is not { Count: > 0 } patches)
            return;

        var end = stream.Position;
        foreach (var (offset, data) in patches)
        {
            stream.Position = startPosition + offset;
            stream.Write(data);
        }

        stream.Position = end;
    }

    /// <summary>Gets the image of an entry: the image it owns (borrowed), or a decode of its encoded payload (owned).</summary>
    private static MaterializedEntry Materialize(ImageCollectionEntry entry, ImageConfiguration configuration)
    {
        if (entry.OwnedOrNull is { } owned)
            return new MaterializedEntry(owned, ownsImage: false);

        var image = entry.Decode(new ImageDecodeOptions { Configuration = configuration });
        return new MaterializedEntry(image, ownsImage: true);
    }

    private readonly struct MaterializedEntry(Image image, bool ownsImage) : IDisposable
    {
        public Image Image => image;

        public void Dispose()
        {
            if (ownsImage)
            {
                image.Dispose();
            }
        }
    }
}
