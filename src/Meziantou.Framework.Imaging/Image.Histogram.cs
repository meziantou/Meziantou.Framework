using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

// Internal entry points of the histogram operations, called by ImageProcessingExtensions after argument validation
public abstract partial class Image
{
    /// <summary>Counts the levels of a channel of frames of this image (every displayed frame and the poster when <paramref name="frames"/> is <see langword="null"/>).</summary>
    /// <exception cref="UnsupportedImageFeatureException">The pixel format is a floating-point one.</exception>
    internal ImageHistogram ComputeHistogramCore(ImageFrame[]? frames, HistogramChannel channel, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ToneSamples.EnsureIntegerSamples(PixelFormat, "compute its histogram");
        cancellationToken.ThrowIfCancellationRequested();
        frames ??= GetAllFrames();
        var format = PixelFormat;
        var levels = ToneSamples.GetLevelCount(format);
        var max = (uint)(levels - 1);
        var bytesPerPixel = PixelFormats.GetBytesPerPixel(format);
        var counts = new long[levels];
        using var buffer = ToneSamples.RentRow(Owner.Scope, _size.Width);
        var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(buffer.Span))[..(4 * _size.Width)];
        foreach (var frame in frames)
        {
            using var lease = frame.GetStorage().AcquireLease();
            for (var y = 0; y < lease.Height; y++)
            {
                if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                ToneSamples.Read(format, lease.GetRowBytes(y)[..(_size.Width * bytesPerPixel)], samples);
                for (var i = 0; i < samples.Length; i += 4)
                {
                    var level = channel switch
                    {
                        HistogramChannel.Luminance => PixelConverter.Luma(samples[i], samples[i + 1], samples[i + 2], max),
                        HistogramChannel.Red => samples[i],
                        HistogramChannel.Green => samples[i + 1],
                        HistogramChannel.Blue => samples[i + 2],
                        _ => samples[i + 3],
                    };
                    counts[level]++;
                }
            }
        }

        return new ImageHistogram(channel, counts, (long)frames.Length * _size.Width * _size.Height);
    }

    /// <summary>
    /// Equalizes the histograms of frames of this image in place (every displayed frame and the poster when
    /// <paramref name="frames"/> is <see langword="null"/>). The working memory is charged to the scope of the image and rented
    /// before any pixel changes; afterward, on failure, some frames or rows may already be remapped.
    /// </summary>
    /// <exception cref="UnsupportedImageFeatureException">The pixel format is a floating-point one.</exception>
    internal void EqualizeFrames(ImageFrame[]? frames, HistogramEqualizationOptions options, CancellationToken cancellationToken)
    {
        Owner.EnsureCanModify("equalize the image");
        ToneSamples.EnsureIntegerSamples(PixelFormat, "equalize it");
        cancellationToken.ThrowIfCancellationRequested();
        var shared = frames is null && options.ShareHistogramAcrossFrames;
        frames ??= GetAllFrames();
        using var mapper = new ToneCurveMapper(Owner.Scope, PixelFormat, _size, options.ColorMode, options.Mode == HistogramEqualizationMode.Adaptive ? options : null);
        _metadata.RemoveStaleThumbnail();
        foreach (var group in GetToneGroups(frames, shared))
        {
            mapper.Equalize(group, cancellationToken);
        }
    }

    /// <summary>
    /// Stretches the levels of frames of this image in place (every displayed frame and the poster when
    /// <paramref name="frames"/> is <see langword="null"/>), as <see cref="EqualizeFrames"/> does.
    /// </summary>
    /// <exception cref="UnsupportedImageFeatureException">The pixel format is a floating-point one.</exception>
    internal void AutoLevelsFrames(ImageFrame[]? frames, AutoLevelsOptions options, CancellationToken cancellationToken)
    {
        Owner.EnsureCanModify("stretch the levels of the image");
        ToneSamples.EnsureIntegerSamples(PixelFormat, "stretch its levels");
        cancellationToken.ThrowIfCancellationRequested();
        var shared = frames is null && options.ShareHistogramAcrossFrames;
        frames ??= GetAllFrames();
        using var mapper = new ToneCurveMapper(Owner.Scope, PixelFormat, _size, options.ColorMode, adaptive: null);
        _metadata.RemoveStaleThumbnail();
        foreach (var group in GetToneGroups(frames, shared))
        {
            mapper.AutoLevels(group, options.LowPercentile, options.HighPercentile, cancellationToken);
        }
    }

    /// <summary>Gets the groups of frames that share their histograms: all of them together, or each frame alone.</summary>
    private static IEnumerable<ImageFrame[]> GetToneGroups(ImageFrame[] frames, bool shared)
    {
        if (shared)
        {
            yield return frames;
            yield break;
        }

        foreach (var frame in frames)
        {
            yield return [frame];
        }
    }
}
