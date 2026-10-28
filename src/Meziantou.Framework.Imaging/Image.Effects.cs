using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

// Internal entry points of the effects of ImageProcessingExtensions that are pixel kernels (EffectKernels), called after
// argument validation
public abstract partial class Image
{
    /// <summary>
    /// Replaces the blocks of frames of this image by their means, in place (every displayed frame and the poster when
    /// <paramref name="frames"/> is <see langword="null"/>). The scratch is charged to the scope of the image and rented
    /// before any pixel changes; afterward, on failure, some frames or rows may already be changed.
    /// </summary>
    internal void PixelateFrames(ImageFrame[]? frames, int size, CancellationToken cancellationToken)
    {
        Owner.EnsureCanModify("pixelate the image");
        EffectKernels.EnsureSupported(PixelFormat);
        cancellationToken.ThrowIfCancellationRequested();
        frames ??= GetAllFrames();
        using var scratch = ResampleWeights.Rent<double>(Owner.Scope, EffectKernels.GetPixelateScratchLength(PixelFormat, _size.Width, size));
        _metadata.RemoveStaleThumbnail();
        foreach (var frame in frames)
        {
            using var lease = frame.GetStorage().AcquireLease();
            frame.PixelatePixels(lease, size, unsafe(MemoryMarshal.Cast<byte, double>(scratch.Span)), cancellationToken);
        }
    }

    /// <summary>
    /// Applies the oil painting effect to frames of this image, in place (every displayed frame and the poster when
    /// <paramref name="frames"/> is <see langword="null"/>). The scratch is charged to the scope of the image and rented
    /// before any pixel changes; afterward, on failure, some frames or rows may already be changed.
    /// </summary>
    internal void OilPaintFrames(ImageFrame[]? frames, int levels, int radius, CancellationToken cancellationToken)
    {
        Owner.EnsureCanModify("apply the oil painting effect");
        EffectKernels.EnsureSupported(PixelFormat);
        cancellationToken.ThrowIfCancellationRequested();
        frames ??= GetAllFrames();
        var scope = Owner.Scope;
        using var scratch = ResampleWeights.Rent<double>(scope, EffectKernels.GetOilPaintScratchLength(PixelFormat, _size.Width, radius, levels));
        using var levelScratch = ResampleWeights.Rent<int>(scope, EffectKernels.GetOilPaintLevelsLength(_size.Width, radius, levels));
        _metadata.RemoveStaleThumbnail();
        foreach (var frame in frames)
        {
            using var lease = frame.GetStorage().AcquireLease();
            frame.OilPaintPixels(lease, levels, radius, unsafe(MemoryMarshal.Cast<byte, double>(scratch.Span)), unsafe(MemoryMarshal.Cast<byte, int>(levelScratch.Span)), cancellationToken);
        }
    }

    /// <summary>
    /// Composites frames of this image over an opaque background, in place (every displayed frame and the poster when
    /// <paramref name="frames"/> is <see langword="null"/>). A pixel format without alpha is left as it is. On failure,
    /// some frames or rows may already be changed.
    /// </summary>
    internal void FlattenFrames(ImageFrame[]? frames, Rgba64 background, CancellationToken cancellationToken)
    {
        Owner.EnsureCanModify("flatten the image");
        EffectKernels.EnsureSupported(PixelFormat);
        cancellationToken.ThrowIfCancellationRequested();
        if (!PixelFormats.HasAlpha(PixelFormat))
            return;

        _metadata.RemoveStaleThumbnail();
        foreach (var frame in frames ?? GetAllFrames())
        {
            using var lease = frame.GetStorage().AcquireLease();
            frame.FlattenPixels(lease, background, cancellationToken);
        }
    }
}
