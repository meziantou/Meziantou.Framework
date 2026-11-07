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
/// describe the conversion from the profile connection space, which input profiles often do not. Device link and abstract
/// profiles are supported in a sequence of profiles (<see cref="Create(IReadOnlyList{IccProfile}, IccColorTransformOptions?)"/>).
/// </para>
/// <para>
/// Samples are interleaved device values without alpha: one sample per color for grayscale, three (red, green, blue) for
/// RGB, four (cyan, magenta, yellow, black) for CMYK, where the smallest sample value is no light or no ink and the largest
/// value is full light or full ink. Integer samples cover the whole range of their type; floating-point samples are in
/// [0, 1]. Colors that the destination cannot represent are clipped per channel, unless
/// <see cref="IccColorTransformOptions.GamutMapping"/> maps them into its gamut.
/// </para>
/// <para>
/// The rendering intent (<see cref="IccColorTransformOptions.Intent"/>) selects the conversion of each profile, from its
/// device values to the profile connection space and back: <see cref="IccRenderingIntent.Perceptual"/> and
/// <see cref="IccRenderingIntent.Saturation"/> use the tables a profile has for them, which map its gamut as its author
/// chose; <see cref="IccRenderingIntent.RelativeColorimetric"/> keeps the colors relative to the media white;
/// <see cref="IccRenderingIntent.AbsoluteColorimetric"/> keeps them relative to the illuminant. A sequence of profiles
/// (<see cref="Create(IReadOnlyList{IccProfile}, IccColorTransformOptions?)"/>) adds device link and abstract profiles.
/// </para>
/// <para>
/// When the two profiles have identical bytes, the samples are copied unchanged.
/// </para>
/// </remarks>
public sealed class IccColorTransform
{
    private readonly IccPipeline _pipeline;
    private readonly bool _isUnbounded;

    private IccColorTransform(IccPipeline pipeline, bool isUnbounded = false)
    {
        _pipeline = pipeline;
        _isUnbounded = isUnbounded;
    }

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
    /// <exception cref="UnsupportedImageFeatureException">
    /// A profile is valid but not supported, or cannot be used as a destination; or
    /// <see cref="IccColorTransformOptions.ClipFloatingPointSamples"/> is <see langword="false"/> and the conversion goes
    /// through a lookup table or a gamut mapping.
    /// </exception>
    public static IccColorTransform Create(IccProfile source, IccProfile destination, IccColorTransformOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        options ??= IccColorTransformOptions.Default;
        return Create(IccPipeline.Create(source, destination, options.Intent, options.BlackPointCompensation, options.GamutMapping), options);
    }

    /// <summary>
    /// Creates the conversion through a sequence of profiles: device profiles (input, display, output and color space
    /// profiles), device link profiles and abstract profiles, in the order the colors go through them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The samples are device values of the first profile and the converted samples device values of the last one. A
    /// device profile entered with device values converts them to the profile connection space, and a device profile
    /// entered from the connection space converts the colors to its device values. To go through the device values of a
    /// profile in the middle, the profile appears twice: display, printer, printer, display previews a print on the
    /// display (a soft proof), with the colors the printer can reproduce. A device link profile converts the device values of its input color space to
    /// those of its output color space with its <c>AToB0</c> table, whatever the rendering intent (it was chosen when the
    /// link was made). An abstract profile converts colors of the connection space with its <c>AToB0</c> table, between
    /// two device profiles.
    /// </para>
    /// <para>
    /// The options apply where the colors cross the connection space. There, the abstract profiles convert colors
    /// relative to the media white of the device profile before them, after its black point compensation, or ICC-absolute
    /// colors (relative to the illuminant of the connection space) with
    /// <see cref="IccRenderingIntent.AbsoluteColorimetric"/>. A sequence of two device profiles is the same conversion as
    /// <see cref="Create(IccProfile, IccProfile, IccColorTransformOptions?)"/>, which copies the samples unchanged when the
    /// two profiles have identical bytes; a longer sequence never does.
    /// </para>
    /// </remarks>
    /// <param name="profiles">The profiles, at least one; the first one describes the source samples and the last one the converted samples.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="IccColorTransformOptions.Default"/>.</param>
    /// <returns>The transform.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="profiles"/> or one of its profiles is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="profiles"/> is empty.</exception>
    /// <exception cref="InvalidImageContentException">A profile is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">
    /// A profile is valid but not supported, or the sequence does not connect: an abstract profile entered with device values,
    /// a device link profile entered from the connection space or with device values of another color space, or a
    /// sequence that ends in the connection space; or <see cref="IccColorTransformOptions.ClipFloatingPointSamples"/> is
    /// <see langword="false"/> and the conversion goes through a lookup table or a gamut mapping.
    /// </exception>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../samples/Meziantou.Framework.Imaging.Samples/ColorExamples.cs" ref:region="Imaging_SoftProof" ref:dedent="true">
    /// public static ushort[] SoftProof(IccProfile display, IccProfile printer, ReadOnlySpan&lt;ushort&gt; rgb)
    /// {
    ///     // The printer profile appears twice: the colors enter it from the profile connection space to become inks, then
    ///     // leave it as the colors of these inks. The colors the printer cannot reproduce lose chroma at constant
    ///     // lightness and hue, instead of being clipped channel by channel.
    ///     var proof = IccColorTransform.Create([display, printer, printer, display], new IccColorTransformOptions
    ///     {
    ///         Intent = IccRenderingIntent.RelativeColorimetric,
    ///         GamutMapping = GamutMapping.ReduceChroma,
    ///     });
    ///
    ///     // A transform is immutable and thread-safe: create it once and reuse it
    ///     var preview = new ushort[rgb.Length];
    ///     proof.Convert(rgb, preview);
    ///     return preview;
    /// }
    /// </code>
    /// </example>
    public static IccColorTransform Create(IReadOnlyList<IccProfile> profiles, IccColorTransformOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        for (var i = 0; i < profiles.Count; i++)
        {
            if (profiles[i] is null)
                throw new ArgumentNullException(nameof(profiles), string.Create(CultureInfo.InvariantCulture, $"The profile at index {i} is null."));
        }

        options ??= IccColorTransformOptions.Default;
        return Create(IccPipeline.Create(profiles, options.Intent, options.BlackPointCompensation, options.GamutMapping), options);
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

        // Many colors read the first stage from tables, rented for the call from a scope of the default limits
        var colors = source.Length / SourceChannelCount;
        if (colors < IccUInt16InputTables.Threshold || !_pipeline.HasInputCurves || _pipeline.IsIdentity)
        {
            ColorConversionKernels.Convert<ushort, IccUInt16Sample>(_pipeline, source, destination, SourceChannelCount, DestinationChannelCount);
            return;
        }

        using var tables = IccUInt16InputTables.Create(_pipeline, colors, AllocationScope.Create(configuration: null, nameof(IccColorTransform)));
        ColorConversionKernels.Convert<ushort, IccUInt16Sample>(_pipeline, source, destination, SourceChannelCount, DestinationChannelCount, uint16Tables: tables);
    }

    /// <summary>Converts floating-point samples in [0, 1], or of any value when the transform does not clip them.</summary>
    /// <param name="source">
    /// The source samples: a whole number of colors of <see cref="SourceChannelCount"/> samples. Values outside [0, 1] are
    /// clipped, unless <see cref="IccColorTransformOptions.ClipFloatingPointSamples"/> was <see langword="false"/>, and
    /// values that are not a number are read as 0.
    /// </param>
    /// <param name="destination">
    /// The converted samples, in [0, 1] unless <see cref="IccColorTransformOptions.ClipFloatingPointSamples"/> was
    /// <see langword="false"/>: exactly <see cref="DestinationChannelCount"/> samples per source color. It may be the same
    /// memory as <paramref name="source"/> when both profiles have the same number of channels.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The length of <paramref name="source"/> is not a multiple of <see cref="SourceChannelCount"/>, the length of
    /// <paramref name="destination"/> does not match, or the spans overlap without being the same memory.
    /// </exception>
    public void Convert(ReadOnlySpan<float> source, Span<float> destination)
    {
        Validate(source, destination);
        if (_isUnbounded)
        {
            ConvertUnbounded(source, destination);
            return;
        }

        ColorConversionKernels.Convert<float, IccSingleSample>(_pipeline, source, destination, SourceChannelCount, DestinationChannelCount);
    }

    private static IccColorTransform Create(IccPipeline pipeline, IccColorTransformOptions options)
    {
        if (!options.ClipFloatingPointSamples && !pipeline.SupportsUnbounded)
            throw new UnsupportedImageFeatureException("The conversion goes through a lookup table or a gamut mapping, which cannot convert floating-point samples outside [0, 1]: only matrix-based profiles without gamut mapping can keep them.", ImageFormat.Unknown, "Unbounded conversion through a lookup table");

        return new IccColorTransform(pipeline, isUnbounded: !options.ClipFloatingPointSamples);
    }

    // Color by color: the source samples are read before the destination ones are written, so the two may be the same memory
    private void ConvertUnbounded(ReadOnlySpan<float> source, Span<float> destination)
    {
        var sourceCount = SourceChannelCount;
        var destinationCount = DestinationChannelCount;
        Span<double> values = stackalloc double[IccPipeline.MaxChannels];
        for (int input = 0, output = 0; input < source.Length; input += sourceCount, output += destinationCount)
        {
            values.Clear();
            for (var channel = 0; channel < sourceCount; channel++)
            {
                values[channel] = source[input + channel];
            }

            _pipeline.ApplyUnbounded(values);

            for (var channel = 0; channel < destinationCount; channel++)
            {
                destination[output + channel] = (float)values[channel];
            }
        }
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
