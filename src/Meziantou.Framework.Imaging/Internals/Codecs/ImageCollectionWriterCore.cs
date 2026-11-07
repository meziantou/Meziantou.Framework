using System.Diagnostics;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The implementation of <see cref="ImageCollection.Save(string, ImageEncoder?)"/> and its stream and asynchronous variants: it
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
/// A path destination is published atomically, like every other save: a failure or a cancellation leaves no partial
/// file behind.
/// </description></item>
/// <item><description>
/// The synchronous and asynchronous saves share one core: with <c>asynchronous: false</c> every write is synchronous and
/// the core completes before it returns. The asynchronous saves write the output asynchronously; the entries backed by
/// encoded data are decoded synchronously between writes, because the collection reads its input synchronously.
/// </description></item>
/// </list>
/// </remarks>
internal static class ImageCollectionWriterCore
{
    public static void Save(ImageCollection collection, string path, ImageEncoder encoder)
    {
        ValidatePath(collection, path, encoder);
        Complete(SaveCoreAsync(collection, path, encoder, asynchronous: false, CancellationToken.None));
    }

    public static void Save(ImageCollection collection, Stream stream, ImageEncoder encoder)
    {
        ValidateStream(collection, stream, encoder);
        Complete(SaveCoreAsync(collection, stream, encoder, asynchronous: false, CancellationToken.None));
    }

    /// <summary>Validates the arguments synchronously, then saves asynchronously.</summary>
    public static Task SaveAsync(ImageCollection collection, string path, ImageEncoder encoder, CancellationToken cancellationToken)
    {
        ValidatePath(collection, path, encoder);
        return SaveCoreAsync(collection, path, encoder, asynchronous: true, cancellationToken).AsTask();
    }

    /// <summary>Validates the arguments synchronously, then saves asynchronously.</summary>
    public static Task SaveAsync(ImageCollection collection, Stream stream, ImageEncoder encoder, CancellationToken cancellationToken)
    {
        ValidateStream(collection, stream, encoder);
        return SaveCoreAsync(collection, stream, encoder, asynchronous: true, cancellationToken).AsTask();
    }

    private static void ValidatePath(ImageCollection collection, string path, ImageEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(encoder);
        Validate(collection, encoder, seekable: true);
    }

    private static void ValidateStream(ImageCollection collection, Stream stream, ImageEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(encoder);
        Validate(collection, encoder, stream.CanSeek);
    }

    /// <summary>
    /// Ends a synchronous save. With <c>asynchronous: false</c> the core never awaits anything that is not already
    /// complete (every write is synchronous), so the task is complete when the core returns.
    /// </summary>
    private static void Complete(ValueTask task)
    {
        Debug.Assert(task.IsCompleted, "A synchronous save must complete synchronously.");
        task.GetAwaiter().GetResult();
    }

    private static async ValueTask SaveCoreAsync(ImageCollection collection, string path, ImageEncoder encoder, bool asynchronous, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = AtomicFileOutput.Create(path, asynchronous);
        try
        {
            await WriteAsync(collection, encoder, file.Stream, asynchronous, cancellationToken).ConfigureAwait(false);
            if (asynchronous)
            {
                await file.PublishAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                file.Publish();
            }
        }
        finally
        {
            // A failure or a cancellation leaves no partial file behind: the temporary file is removed and nothing is
            // published (no effect once published)
            if (asynchronous)
            {
                await file.AbortAsync().ConfigureAwait(false);
            }
            else
            {
                file.Abort();
            }
        }
    }

    private static async ValueTask SaveCoreAsync(ImageCollection collection, Stream stream, ImageEncoder encoder, bool asynchronous, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await WriteAsync(collection, encoder, stream, asynchronous, cancellationToken).ConfigureAwait(false);
        if (asynchronous)
        {
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            stream.Flush();
        }
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

        // Floating-point entries are reduced to 16 bits only with the policy of the encoder, unless it stores them
        if (encoder.FloatingPointConversion is null && !encoder.StoresFloatingPointSamples)
        {
            for (var i = 0; i < collection.Count; i++)
            {
                if (PixelFormats.IsFloatingPoint(collection[i].PixelFormat))
                {
                    throw new UnsupportedImageFeatureException(
                        string.Create(CultureInfo.InvariantCulture, $"{ImageFormatNames.Get(encoder.Format)} output does not store floating-point samples, so encoding the {collection[i].PixelFormat} pixels of entry {i} would discard their range and precision. Set {encoder.GetType().Name}.FloatingPointConversion to reduce them to 16-bit integers (Rgba64), or convert the image explicitly with CloneAs."),
                        encoder.Format,
                        "Floating-point samples");
                }
            }
        }
    }

    private static async ValueTask WriteAsync(ImageCollection collection, ImageEncoder encoder, Stream stream, bool asynchronous, CancellationToken cancellationToken)
    {
        var configuration = collection.Configuration;
        var scope = AllocationScope.Create(configuration, "ImageCollectionWriter");
        using var output = new ImageOutputBuffer(scope);
        var startPosition = stream.CanSeek ? stream.Position : 0;
        if (encoder is TiffEncoder tiff)
        {
            await WriteTiffAsync(collection, tiff, output, scope, stream, asynchronous, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            WriteIcon(collection, (IcoEncoder)encoder, output, scope, configuration, cancellationToken);
        }

        await FlushAsync(output, stream, asynchronous, cancellationToken).ConfigureAwait(false);
        await ApplyPatchesAsync(output, stream, startPosition, asynchronous, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask WriteTiffAsync(ImageCollection collection, TiffEncoder encoder, ImageOutputBuffer output, AllocationScope scope, Stream stream, bool asynchronous, CancellationToken cancellationToken)
    {
        using var writer = new TiffDocumentWriter(encoder, scope, collection.Configuration);
        writer.WriteHeader(output);
        for (var i = 0; i < collection.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var page = Materialize(collection[i], collection.Configuration, encoder, cancellationToken);
            var image = page.Image;
            var frame = image.Frames[0];
            var metadata = MetadataWritePlan.Create(image.Metadata, ImageFormat.Tiff, encoder.MetadataHandling, image.Size, TiffSampleLayout.GetLabeledPixelFormat(encoder.PixelLayout, image.PixelFormat));
            MetadataWritePlan.ValidateFrameMetadata(frame.MetadataCore, ImageFormat.Tiff, encoder.MetadataHandling);
            writer.BeginPage(frame, image.Size, image.PixelFormat, metadata);
            while (!writer.EncodeStrip(output, cancellationToken))
            {
                await FlushIfNeededAsync(output, stream, asynchronous, cancellationToken).ConfigureAwait(false);
            }

            writer.CompletePage(output);
            await FlushIfNeededAsync(output, stream, asynchronous, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void WriteIcon(ImageCollection collection, IcoEncoder encoder, ImageOutputBuffer output, AllocationScope scope, ImageConfiguration configuration, CancellationToken cancellationToken)
    {
        // Each payload is encoded as its image is materialized, so only one representation is alive at a time; the
        // encoded payloads are then small and bounded (a representation is at most 256x256 pixels), and the whole
        // directory is written at the end
        var encoded = new List<IcoDocumentWriter.EncodedEntry>(collection.Count);
        try
        {
            for (var i = 0; i < collection.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = collection[i];
                using var item = Materialize(entry, configuration, encoder, cancellationToken);
                var image = item.Image;
                IcoDocumentWriter.ValidateSize(encoder.Format, image.Size);

                // An icon directory stores no metadata: the policy decides whether unstorable metadata is an error
                _ = MetadataWritePlan.Create(image.Metadata, encoder.Format, encoder.MetadataHandling, image.Size, image.PixelFormat);
                var frame = image.Frames[0];

                // The materialized frame carries the hotspot of its entry; only a cursor directory stores it
                MetadataWritePlan.ValidateFrameMetadata(frame.MetadataCore, encoder.Format, encoder.MetadataHandling);
                var hotspot = ImageOutputCapabilities.StoresFrameHotspot(encoder.Format) ? frame.MetadataCore.Hotspot : null;
                encoded.Add(IcoDocumentWriter.EncodeEntry(encoder, new IcoDocumentWriter.Entry(frame, image.PixelFormat, hotspot), scope, configuration));
            }

            cancellationToken.ThrowIfCancellationRequested();
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

    private static ValueTask FlushIfNeededAsync(ImageOutputBuffer output, Stream stream, bool asynchronous, CancellationToken cancellationToken)
        => output.ShouldFlush ? FlushAsync(output, stream, asynchronous, cancellationToken) : ValueTask.CompletedTask;

    private static ValueTask FlushAsync(ImageOutputBuffer output, Stream stream, bool asynchronous, CancellationToken cancellationToken)
    {
        if (asynchronous)
            return output.FlushToAsync(stream, cancellationToken);

        output.FlushTo(stream);
        return ValueTask.CompletedTask;
    }

    private static async ValueTask ApplyPatchesAsync(ImageOutputBuffer output, Stream stream, long startPosition, bool asynchronous, CancellationToken cancellationToken)
    {
        if (output.Patches is not { Count: > 0 } patches)
            return;

        var end = stream.Position;
        foreach (var (offset, data) in patches)
        {
            stream.Position = startPosition + offset;
            if (asynchronous)
            {
                await stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                stream.Write(data);
            }
        }

        stream.Position = end;
    }

    /// <summary>
    /// Gets the image of an entry: the image it owns (borrowed), or a decode of its encoded payload (owned), or the 16-bit
    /// reduction of a floating-point image that the encoder does not store (owned, <see cref="ImageEncoder.FloatingPointConversion"/>),
    /// or the lossless widening of a pixel format the encoder does not store (owned, <see cref="PixelFormatWidening"/>).
    /// </summary>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The reduced image is owned by the returned entry, which the caller disposes.")]
    private static MaterializedEntry Materialize(ImageCollectionEntry entry, ImageConfiguration configuration, ImageEncoder encoder, CancellationToken cancellationToken)
    {
        if (entry.OwnedOrNull is { } owned)
        {
            if (PixelFormats.IsFloatingPoint(owned.PixelFormat) && !encoder.StoresFloatingPointSamples)
                return new MaterializedEntry(owned.CloneAs<Rgba64>(encoder.FloatingPointConversion), ownsImage: true);

            var widened = PixelFormatWidening.GetEncodedFormat(encoder, owned.PixelFormat);
            if (widened != owned.PixelFormat)
                return new MaterializedEntry(owned.CloneAs(widened, options: null), ownsImage: true);

            return new MaterializedEntry(owned, ownsImage: false);
        }

        var image = entry.Decode(new ImageDecodeOptions { Configuration = configuration }, cancellationToken);
        var encodedFormat = PixelFormatWidening.GetEncodedFormat(encoder, image.PixelFormat);
        if (encodedFormat != image.PixelFormat)
        {
            using (image)
            {
                return new MaterializedEntry(image.CloneAs(encodedFormat, options: null), ownsImage: true);
            }
        }

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
