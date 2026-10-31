using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

// Internal entry points of the tone adjustments and the morphological and rank filters of ImageProcessingExtensions,
// called after argument validation
public abstract partial class Image
{
    /// <summary>
    /// Gives new colors to frames of this image from a lookup, in place (every displayed frame and the poster when
    /// <paramref name="frames"/> is <see langword="null"/>). The working memory is charged to the scope of the image and
    /// rented before any pixel changes; afterward, on failure, some frames or rows may already be changed.
    /// </summary>
    internal void ApplyColorLookupFrames(ImageFrame[]? frames, ColorLookup lookup, string operation, CancellationToken cancellationToken)
    {
        Owner.EnsureCanModify(operation);
        cancellationToken.ThrowIfCancellationRequested();
        if (!PixelFormats.HasColor(PixelFormat))
            return;

        frames ??= GetAllFrames();
        using var run = lookup.Prepare(Owner.Scope, PixelFormat, _size);
        _metadata.RemoveStaleThumbnail();
        run.Apply(frames, cancellationToken);
    }
}
