using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

// Internal entry point of the content-aware crop suggestion of ImageProcessingExtensions, called after argument validation
public abstract partial class Image
{
    /// <summary>
    /// Suggests the crop of an aspect ratio that keeps the most energy of frames of this image (every displayed frame and the
    /// poster when <paramref name="frames"/> is <see langword="null"/>). Nothing is written to the pixels.
    /// </summary>
    /// <exception cref="UnsupportedImageFeatureException">A floating-point sample is not finite.</exception>
    internal Rectangle SuggestCropFrames(ImageFrame[]? frames, int aspectWidth, int aspectHeight, CropSuggestionOptions options, CancellationToken cancellationToken)
    {
        Owner.ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        frames ??= GetAllFrames();
        var crop = CropSuggester.GetCropSize(_size, aspectWidth, aspectHeight, options.Scale);
        using var table = CropSuggester.CreateTable(Owner, PixelFormat, _size);
        foreach (var frame in frames)
        {
            using var lease = frame.GetStorage().AcquireLease();
            table.Add(lease.Storage, options.Energy, cancellationToken);
        }

        return table.FindBest(crop, cancellationToken);
    }
}
