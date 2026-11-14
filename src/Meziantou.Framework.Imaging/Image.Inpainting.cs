using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

// Internal entry point of the inpainting of ImageProcessingExtensions, called after argument validation
public abstract partial class Image
{
    /// <summary>Fills the pixels of a mask of every displayed frame and the poster, transactionally, through a processor in <see cref="RgbaFloat"/>.</summary>
    internal void InpaintCore(PixelMask mask, InpaintingOptions options, CancellationToken cancellationToken)
    {
        const string Operation = "inpaint the image";
        Owner.EnsureCanModify(Operation);
        var count = mask.Count;
        if (count == (long)_size.Width * _size.Height)
            throw new ArgumentException("The mask holds every pixel of the image: there is nothing to fill it from.", nameof(mask));

        cancellationToken.ThrowIfCancellationRequested();
        if (count == 0)
            return;

        using var inpainter = new Inpainter(Owner.Scope, _size, mask, options.Radius);
        ApplyProcessorFrames<RgbaFloat, InpaintingProcessor>(new InpaintingProcessor(inpainter), Operation, cancellationToken);
    }

    /// <summary>Fills the mask of each frame; the pixels do not move, so a cursor hotspot stays where it is.</summary>
    private sealed class InpaintingProcessor(Inpainter inpainter) : IImageProcessor<RgbaFloat>
    {
        public Size GetOutputSize(Size size) => size;

        public void Process(int frameIndex, PixelRowSource<RgbaFloat> source, PixelAccessor<RgbaFloat> destination, CancellationToken cancellationToken)
            => inpainter.Apply(source, destination, cancellationToken);
    }
}
