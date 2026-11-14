using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

// Internal entry point of the custom processors of ImageProcessingExtensions, called after argument validation, and of the
// built-in operations written as processors (seam carving, inpainting)
public abstract partial class Image
{
    /// <summary>
    /// Replaces every displayed frame and the poster with the frames a processor computes, transactionally (see
    /// <see cref="ReplaceGeometry{TFiller}"/>). When <typeparamref name="TPixel"/> is not the pixel format of the image, each
    /// frame is converted to a working copy, the result is computed in a second working storage, then stored in the pixel
    /// format of the image; the working storages are charged to the scope of the image with the replacements.
    /// </summary>
    /// <param name="processor">The processor.</param>
    /// <param name="operation">The operation, for exception messages.</param>
    /// <param name="cancellationToken">The token checked between frames and once more right before the commit.</param>
    /// <exception cref="NotSupportedException"><typeparamref name="TPixel"/> cannot hold the samples of the image.</exception>
    /// <exception cref="InvalidOperationException">The processor returned an empty size.</exception>
    internal void ApplyProcessorFrames<TPixel, TProcessor>(TProcessor processor, string operation, CancellationToken cancellationToken)
        where TPixel : unmanaged
        where TProcessor : IImageProcessor<TPixel>
    {
        var working = GetWorkingFormat<TPixel>();
        Owner.EnsureCanModify(operation);
        cancellationToken.ThrowIfCancellationRequested();
        var size = processor.GetOutputSize(_size);
        if (size.IsEmpty)
            throw new InvalidOperationException($"The processor {processor.GetType().Name} returned an empty size for a {_size.Width}x{_size.Height} canvas.");

        using var scratch = new ProcessorScratch(Owner, working, working != PixelFormat, _size, size);
        ReplaceGeometry(size, normalizeOrientation: false, operation, new ProcessorFiller<TPixel, TProcessor>(processor, PixelFormat, working, _size, size, scratch), cancellationToken);
    }

    /// <summary>
    /// Calls a processor for the replacement of each frame, directly on the storages or through the working storages of
    /// <see cref="ProcessorScratch"/>.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly struct ProcessorFiller<TPixel, TProcessor>(TProcessor processor, PixelFormat format, PixelFormat working, Size sourceSize, Size destinationSize, ProcessorScratch scratch) : IGeometryFiller
        where TPixel : unmanaged
        where TProcessor : IImageProcessor<TPixel>
    {
        public bool TryMapPoint(Point source, out Point destination)
            => processor.TryMapPoint(source, sourceSize, destinationSize, out destination)
            && destination.X >= 0 && destination.Y >= 0 && destination.X < destinationSize.Width && destination.Y < destinationSize.Height;

        public void Prepare(AllocationScope scope) => scratch.Allocate();

        public void Fill(ImageFrame frame, scoped in PixelLease source, scoped in PixelLease destination, CancellationToken cancellationToken)
        {
            var index = scratch.NextFrameIndex();
            if (working == format)
            {
                processor.Process(index, new PixelRowSource<TPixel>(source.Storage, source.Token), new PixelAccessor<TPixel>(destination.Storage, destination.Token), cancellationToken);
                return;
            }

            using var leases = PixelLeasePair.Acquire(scratch.Source!, scratch.Destination!);
            var sourceCopy = leases.First;
            var destinationCopy = leases.Second;
            for (var y = 0; y < sourceSize.Height; y++)
            {
                PixelConverter.ConvertRow(format, source.GetRowBytes(y), working, sourceCopy.GetRowBytes(y));
            }

            // The replacement is a cleared frame: its working copy starts from the same pixels
            for (var y = 0; y < destinationSize.Height; y++)
            {
                PixelConverter.ConvertRow(format, destination.GetRowBytes(y), working, destinationCopy.GetRowBytes(y));
            }

            processor.Process(index, new PixelRowSource<TPixel>(sourceCopy.Storage, sourceCopy.Token), new PixelAccessor<TPixel>(destinationCopy.Storage, destinationCopy.Token), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            for (var y = 0; y < destinationSize.Height; y++)
            {
                StoreWorkingRow(working, destinationCopy.GetRowBytes(y), format, destination.GetRowBytes(y));
            }

            if (processor is INativePixelCopier copier)
            {
                copier.CopyNativePixels(source, destination, PixelFormats.GetBytesPerPixel(format));
            }
        }
    }

    /// <summary>
    /// A built-in processor that knows which pixels of the result are pixels of the source moved or kept as they are: after
    /// the result is stored from a working format, their stored bytes are copied, so that the conversions never change
    /// them (the payload of a not-a-number half, for instance).
    /// </summary>
    internal interface INativePixelCopier
    {
        /// <summary>Copies the bytes of the pixels of the source that the result keeps as they are.</summary>
        void CopyNativePixels(scoped in PixelLease source, scoped in PixelLease destination, int bytesPerPixel);
    }

    /// <summary>The working storages of a processor whose pixel type is not the pixel format of the image, and the index of the frame being processed.</summary>
    private sealed class ProcessorScratch(StorageOwner owner, PixelFormat working, bool converted, Size sourceSize, Size destinationSize) : IDisposable
    {
        private int _frameIndex;

        public PixelStorage? Source { get; private set; }

        public PixelStorage? Destination { get; private set; }

        public int NextFrameIndex() => _frameIndex++;

        public void Allocate()
        {
            if (!converted)
                return;

            var bytesPerPixel = PixelFormats.GetBytesPerPixel(working);
            Source = owner.Allocate(sourceSize.Width, sourceSize.Height, bytesPerPixel, options: null, AllocationKind.Temporary);
            Destination = owner.Allocate(destinationSize.Width, destinationSize.Height, bytesPerPixel, options: null, AllocationKind.Temporary);
        }

        public void Dispose()
        {
            Source?.Dispose();
            Destination?.Dispose();
        }
    }
}
