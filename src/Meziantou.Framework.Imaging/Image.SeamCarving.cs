using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

// Internal entry point of the seam carving of ImageProcessingExtensions, called after argument validation
public abstract partial class Image
{
    /// <summary>
    /// Resizes every displayed frame and the poster by seam carving, transactionally: the features of every frame are read,
    /// the seams planned (<see cref="SeamCarver"/>), then applied to each frame through a processor in
    /// <see cref="RgbaFloat"/>. A size that does not change leaves the image unchanged.
    /// </summary>
    internal void SeamCarveCore(Size size, SeamCarvingOptions options, CancellationToken cancellationToken)
    {
        const string Operation = "seam carve the image";
        Owner.EnsureCanModify(Operation);
        cancellationToken.ThrowIfCancellationRequested();
        if (size == _size)
            return;

        // The plan holds the largest intermediate canvas: check the limits before reading anything
        _configuration.Limits.EnsureCanvasWithinLimits(size.Width, _size.Height);
        _configuration.Limits.EnsureCanvasWithinLimits(size.Width, size.Height);
        var frames = GetAllFrames();
        var channels = 2 * frames.Length;
        using var carver = CreateSeamCarver(frames, channels, size, options, cancellationToken);
        ApplyProcessorFrames<RgbaFloat, SeamCarvingProcessor>(new SeamCarvingProcessor(carver), Operation, cancellationToken);
    }

    private SeamCarver CreateSeamCarver(ImageFrame[] frames, int channels, Size size, SeamCarvingOptions options, CancellationToken cancellationToken)
    {
        var width = _size.Width;
        var height = _size.Height;
        var format = PixelFormat;
        var max = (long)ToneSamples.GetLevelCount(format) - 1;
        var bytesPerPixel = PixelFormats.GetBytesPerPixel(format);
        using var buffer = ResampleWeights.Rent<int>(Owner.Scope, (long)width * height * channels);
        using var row = ToneSamples.RentRow(Owner.Scope, width);
        var features = unsafe(MemoryMarshal.Cast<byte, int>(buffer.Span))[..(width * height * channels)];
        var samples = ToneSamples.GetSamples(row, width);
        var luma = ToneSamples.GetLuma(row, width);
        for (var f = 0; f < frames.Length; f++)
        {
            using var lease = frames[f].GetStorage().AcquireLease();
            for (var y = 0; y < height; y++)
            {
                if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                ToneSamples.Read(format, lease.GetRowBytes(y)[..(width * bytesPerPixel)], samples);
                ToneSamples.ComputeLuma(samples, luma);
                for (var x = 0; x < width; x++)
                {
                    var alpha = (long)samples[(4 * x) + 3];
                    var index = ((((y * width) + x) * frames.Length) + f) * 2;
                    features[index] = (int)(((luma[x] * alpha) + (max / 2)) / max);
                    features[index + 1] = (int)alpha;
                }
            }
        }

        return SeamCarver.Create(Owner.Scope, _size, size, features, channels, options.ProtectedRegion, options.RemovedRegion, cancellationToken);
    }

    /// <summary>Applies a seam carving plan to each frame.</summary>
    private sealed class SeamCarvingProcessor(SeamCarver carver) : IImageProcessor<RgbaFloat>, INativePixelCopier
    {
        // The pixels that are only moved keep their stored bytes
        public void CopyNativePixels(scoped in PixelLease source, scoped in PixelLease destination, int bytesPerPixel)
            => carver.CopyMovedPixels(source, destination, bytesPerPixel);

        public Size GetOutputSize(Size size) => carver.TargetSize;

        public void Process(int frameIndex, PixelRowSource<RgbaFloat> source, PixelAccessor<RgbaFloat> destination, CancellationToken cancellationToken)
            => carver.Apply(source, destination, cancellationToken);

        public bool TryMapPoint(Point source, Size sourceSize, Size destinationSize, out Point destination)
            => carver.TryMapPoint(source, out destination);
    }
}
