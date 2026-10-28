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
        var bytesPerPixel = PixelFormats.GetBytesPerPixel(format);
        var counts = new long[levels];
        using var buffer = ToneSamples.RentRow(Owner.Scope, _size.Width);
        var samples = ToneSamples.GetSamples(buffer, _size.Width);
        var luma = ToneSamples.GetLuma(buffer, _size.Width);

        // The values counted in a row: the luma, or one of the four samples of each pixel
        var stride = channel == HistogramChannel.Luminance ? 1 : 4;
        ReadOnlySpan<ushort> values = channel switch
        {
            HistogramChannel.Luminance => luma,
            HistogramChannel.Red => samples,
            HistogramChannel.Green => samples[1..],
            HistogramChannel.Blue => samples[2..],
            _ => samples[3..],
        };

        // 8-bit levels are counted in partial counters, added up before they can overflow
        Span<uint> partial = levels == 256 ? stackalloc uint[ToneSamples.PartialCountLength] : default;
        partial.Clear();
        var pending = 0L;
        foreach (var frame in frames)
        {
            using var lease = frame.GetStorage().AcquireLease();
            for (var y = 0; y < lease.Height; y++)
            {
                if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var pixels = lease.GetRowBytes(y)[..(_size.Width * bytesPerPixel)];
                if (channel == HistogramChannel.Luminance)
                {
                    ToneSamples.ReadLuma(format, pixels, samples, luma);
                }
                else
                {
                    ToneSamples.Read(format, pixels, samples);
                }

                if (levels == 256)
                {
                    ToneSamples.CountBytes(values, stride, partial);
                    pending += _size.Width;
                    if (pending > uint.MaxValue - (long)_size.Width)
                    {
                        ToneSamples.FlushPartialCounts(partial, counts);
                        pending = 0;
                    }
                }
                else
                {
                    for (var i = 0; i < values.Length; i += stride)
                    {
                        counts[values[i]]++;
                    }
                }
            }
        }

        if (levels == 256)
        {
            ToneSamples.FlushPartialCounts(partial, counts);
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

    /// <summary>
    /// Binarizes frames of this image in place (every displayed frame and the poster when <paramref name="frames"/> is
    /// <see langword="null"/>). The working memory is charged to the scope of the image and rented before any pixel changes;
    /// afterward, on failure, some frames or rows may already be binarized.
    /// </summary>
    /// <exception cref="UnsupportedImageFeatureException">The pixel format is a floating-point one.</exception>
    /// <exception cref="ArgumentException">The pixel format cannot store a color of <paramref name="options"/> exactly.</exception>
    internal void ThresholdFrames(ImageFrame[]? frames, ThresholdOptions options, CancellationToken cancellationToken)
    {
        Owner.EnsureCanModify("threshold the image");
        Thresholder.Validate(PixelFormat, options);
        cancellationToken.ThrowIfCancellationRequested();
        var shared = frames is null && options.ShareHistogramAcrossFrames;
        frames ??= GetAllFrames();
        var max = ToneSamples.GetLevelCount(PixelFormat) - 1;
        using var thresholder = new Thresholder(Owner.Scope, PixelFormat, _size, options);
        double? sharedLevel = options.Method == ThresholdMethod.Otsu && shared ? ComputeHistogramCore(frames, HistogramChannel.Luminance, cancellationToken).GetOtsuThreshold() : null;
        _metadata.RemoveStaleThumbnail();
        foreach (var frame in frames)
        {
            var level = options.Method switch
            {
                ThresholdMethod.Otsu => sharedLevel ?? ComputeHistogramCore([frame], HistogramChannel.Luminance, cancellationToken).GetOtsuThreshold(),
                ThresholdMethod.Fixed => options.Level * max,
                _ => 0,
            };

            using var lease = frame.GetStorage().AcquireLease();
            thresholder.Apply(lease, level, cancellationToken);
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
