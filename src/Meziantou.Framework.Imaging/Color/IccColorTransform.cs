using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// An ICC color conversion from the device color space of a source profile to the device color space of a destination
/// profile. Instances are immutable and thread-safe: create a transform once and reuse it.
/// </summary>
/// <remarks>
/// <para>
/// Supported profiles are ICC version 2 and version 4 input, display, output and color space profiles whose data color
/// space is grayscale, RGB or CMYK, based on a matrix and tone curves or on lookup tables. A destination profile must
/// describe the conversion from the profile connection space, which input profiles often do not.
/// </para>
/// <para>
/// Samples are interleaved device values without alpha: one sample per color for grayscale, three (red, green, blue) for
/// RGB, four (cyan, magenta, yellow, black) for CMYK, where the smallest sample value is no light or no ink and the largest
/// value is full light or full ink. Integer samples cover the whole range of their type; floating-point samples are in
/// [0, 1]. Colors that the destination cannot represent are clipped per channel.
/// </para>
/// <para>
/// When the two profiles have identical bytes, the samples are copied unchanged.
/// </para>
/// </remarks>
public sealed class IccColorTransform
{
    private readonly IccPipeline _pipeline;

    private IccColorTransform(IccPipeline pipeline) => _pipeline = pipeline;

    /// <summary>Gets the number of samples per source color: 1 (grayscale), 3 (RGB) or 4 (CMYK).</summary>
    public int SourceChannelCount => _pipeline.SourceChannelCount;

    /// <summary>Gets the number of samples per destination color: 1 (grayscale), 3 (RGB) or 4 (CMYK).</summary>
    public int DestinationChannelCount => _pipeline.DestinationChannelCount;

    /// <summary>Gets the conversion evaluated by the transform.</summary>
    internal IccPipeline Pipeline => _pipeline;

    /// <summary>Creates the conversion from a source profile to a destination profile.</summary>
    /// <param name="source">The profile describing the source samples.</param>
    /// <param name="destination">The profile describing the converted samples.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="IccColorTransformOptions.Default"/>.</param>
    /// <returns>The transform.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidImageContentException">A profile is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A profile is valid but not supported, or cannot be used as a destination.</exception>
    public static IccColorTransform Create(IccProfile source, IccProfile destination, IccColorTransformOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        options ??= IccColorTransformOptions.Default;
        return new IccColorTransform(IccPipeline.Create(source, destination, options.Intent, options.BlackPointCompensation));
    }

    /// <summary>Converts 8-bit samples.</summary>
    /// <param name="source">The source samples: a whole number of colors of <see cref="SourceChannelCount"/> samples.</param>
    /// <param name="destination">
    /// The converted samples: exactly <see cref="DestinationChannelCount"/> samples per source color. It may be the same
    /// memory as <paramref name="source"/> when both profiles have the same number of channels.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The length of <paramref name="source"/> is not a multiple of <see cref="SourceChannelCount"/>, the length of
    /// <paramref name="destination"/> does not match, or the spans overlap without being the same memory.
    /// </exception>
    public void Convert(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        Validate(source, destination);
        ColorConversionKernels.Convert<byte, IccByteSample>(_pipeline, source, destination, SourceChannelCount, DestinationChannelCount);
    }

    /// <summary>Converts 16-bit samples.</summary>
    /// <param name="source">The source samples: a whole number of colors of <see cref="SourceChannelCount"/> samples.</param>
    /// <param name="destination">
    /// The converted samples: exactly <see cref="DestinationChannelCount"/> samples per source color. It may be the same
    /// memory as <paramref name="source"/> when both profiles have the same number of channels.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The length of <paramref name="source"/> is not a multiple of <see cref="SourceChannelCount"/>, the length of
    /// <paramref name="destination"/> does not match, or the spans overlap without being the same memory.
    /// </exception>
    public void Convert(ReadOnlySpan<ushort> source, Span<ushort> destination)
    {
        Validate(source, destination);
        ColorConversionKernels.Convert<ushort, IccUInt16Sample>(_pipeline, source, destination, SourceChannelCount, DestinationChannelCount);
    }

    /// <summary>Converts floating-point samples in [0, 1].</summary>
    /// <param name="source">
    /// The source samples: a whole number of colors of <see cref="SourceChannelCount"/> samples. Values outside [0, 1] are
    /// clipped and values that are not a number are read as 0.
    /// </param>
    /// <param name="destination">
    /// The converted samples in [0, 1]: exactly <see cref="DestinationChannelCount"/> samples per source color. It may be
    /// the same memory as <paramref name="source"/> when both profiles have the same number of channels.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The length of <paramref name="source"/> is not a multiple of <see cref="SourceChannelCount"/>, the length of
    /// <paramref name="destination"/> does not match, or the spans overlap without being the same memory.
    /// </exception>
    public void Convert(ReadOnlySpan<float> source, Span<float> destination)
    {
        Validate(source, destination);
        ColorConversionKernels.Convert<float, IccSingleSample>(_pipeline, source, destination, SourceChannelCount, DestinationChannelCount);
    }

    private void Validate<T>(ReadOnlySpan<T> source, Span<T> destination)
    {
        var (count, remainder) = Math.DivRem(source.Length, SourceChannelCount);
        if (remainder != 0)
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The source must hold a whole number of colors of {SourceChannelCount} samples, but it has {source.Length} samples."), nameof(source));

        if (destination.Length != (long)count * DestinationChannelCount)
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The destination must hold exactly {(long)count * DestinationChannelCount} samples ({DestinationChannelCount} for each of the {count} source colors), but it has {destination.Length} samples."), nameof(destination));

        if (source.Overlaps(destination, out var offset) && (offset != 0 || SourceChannelCount != DestinationChannelCount))
            throw new ArgumentException("The source and the destination must not overlap, unless they are the same memory and both profiles have the same number of channels.", nameof(destination));
    }
}
