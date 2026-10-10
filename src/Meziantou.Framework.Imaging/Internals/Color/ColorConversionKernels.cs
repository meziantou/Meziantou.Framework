using System.Diagnostics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The scalar kernels of ICC color conversion: they evaluate the stages of an <see cref="IccPipeline"/> for each color of
/// a row, in <see cref="double"/>, and are the numerical reference of the conversion.
/// </summary>
internal static class ColorConversionKernels
{
    /// <summary>Converts interleaved colors.</summary>
    /// <typeparam name="T">The stored sample type.</typeparam>
    /// <typeparam name="TSample">The sample conversion.</typeparam>
    /// <param name="pipeline">The conversion.</param>
    /// <param name="source">The source samples: a whole number of colors of <paramref name="sourceStride"/> samples.</param>
    /// <param name="destination">The destination samples, for the same number of colors. It may be exactly <paramref name="source"/> when the strides are equal.</param>
    /// <param name="sourceStride">The samples per source color: the source channels, then the samples copied unchanged (alpha).</param>
    /// <param name="destinationStride">The samples per destination color: the destination channels, then the copied samples.</param>
    /// <param name="reverseColorOrder">Whether three-channel colors are stored blue first on both sides.</param>
    public static void Convert<T, TSample>(IccPipeline pipeline, ReadOnlySpan<T> source, Span<T> destination, int sourceStride, int destinationStride, bool reverseColorOrder = false)
        where T : unmanaged
        where TSample : struct, IIccSample<T>
    {
        var sourceChannels = pipeline.SourceChannelCount;
        var destinationChannels = pipeline.DestinationChannelCount;
        var copied = sourceStride - sourceChannels;
        Debug.Assert(copied is >= 0 and <= IccPipeline.MaxChannels && copied == destinationStride - destinationChannels);
        Debug.Assert(!reverseColorOrder || (sourceChannels == 3 && destinationChannels == 3));
        if (pipeline.IsIdentity)
        {
            // Identical profiles: the samples are kept exactly, whatever their type
            source.CopyTo(destination);
            return;
        }

        Span<double> values = stackalloc double[IccPipeline.MaxChannels];
        Span<T> kept = stackalloc T[IccPipeline.MaxChannels];
        kept = kept[..copied];
        var count = source.Length / sourceStride;
        for (var i = 0; i < count; i++)
        {
            // The whole source color is read before the destination is written: the spans may be the same
            var input = source.Slice(i * sourceStride, sourceStride);
            for (var channel = 0; channel < sourceChannels; channel++)
            {
                values[channel] = TSample.Load(input[reverseColorOrder ? 2 - channel : channel]);
            }

            input[sourceChannels..].CopyTo(kept);
            pipeline.Apply(values);

            var output = destination.Slice(i * destinationStride, destinationStride);
            for (var channel = 0; channel < destinationChannels; channel++)
            {
                output[reverseColorOrder ? 2 - channel : channel] = TSample.Store(values[channel]);
            }

            kept.CopyTo(output[destinationChannels..]);
        }
    }
}
