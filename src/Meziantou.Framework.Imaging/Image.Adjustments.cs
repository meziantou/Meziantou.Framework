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

    /// <summary>
    /// Applies a morphological operation to frames of this image, in place (every displayed frame and the poster when
    /// <paramref name="frames"/> is <see langword="null"/>). The scratch is charged to the scope of the image and rented
    /// before any pixel changes; afterward, on failure, some frames or rows may already be filtered.
    /// </summary>
    /// <exception cref="NotSupportedException">A top-hat transform of an alpha mask, which has no color.</exception>
    internal void MorphologyFrames(ImageFrame[]? frames, StructuringElement element, ConvolutionEdgeMode edgeMode, MorphologyOperation operation, CancellationToken cancellationToken)
    {
        Owner.EnsureCanModify("filter the image");
        if (operation is MorphologyOperation.TopHat or MorphologyOperation.BlackHat && !PixelFormats.HasColor(PixelFormat))
            throw new NotSupportedException($"The pixel format '{PixelFormat}' has no color: a top-hat transform keeps the alpha and changes the colors only.");

        using var plan = new MorphologyPlan(element, edgeMode, operation, KernelPixelFormat, _size);
        cancellationToken.ThrowIfCancellationRequested();
        frames ??= GetAllFrames();
        plan.Prepare(Owner);
        _metadata.RemoveStaleThumbnail();
        foreach (var frame in frames)
        {
            using var lease = frame.GetStorage().AcquireLease();
            if (KernelPixelFormat == PixelFormat)
            {
                MorphologyFilter.Apply(lease, plan, cancellationToken);
            }
            else
            {
                ProcessingBridge.RunInPlace(Owner, PixelFormat, lease, (scoped in PixelLease working) => MorphologyFilter.Apply(working, plan, cancellationToken));
            }
        }
    }
}
