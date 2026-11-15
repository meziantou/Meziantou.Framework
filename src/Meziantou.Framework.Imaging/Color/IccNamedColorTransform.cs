using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// The conversion of the colors of an ICC named color profile to the device values of another profile, through a
/// sequence of profiles. Instances are immutable and thread-safe: create a transform once and reuse it.
/// </summary>
/// <remarks>
/// <para>
/// The first profile of the sequence is the named color profile; its colors enter the sequence in the profile connection
/// space, from their media-relative colorimetry (<see cref="IccNamedColor.ConnectionSpaceColor"/>). The other profiles
/// follow the rules of <see cref="IccColorTransform.Create(IReadOnlyList{IccProfile}, IccColorTransformOptions?)"/>:
/// abstract profiles in the connection space, then a device profile that converts the colors to its device values,
/// optionally followed by device link profiles or by other pairs of device profiles.
/// </para>
/// <para>
/// The rendering intent selects the conversion of the destination profiles; with
/// <see cref="IccRenderingIntent.AbsoluteColorimetric"/>, the colors are first rescaled from the media white point of the
/// named color profile. Named colors are measured colors, not the range of a device:
/// <see cref="IccColorTransformOptions.BlackPointCompensation"/> does not apply to them, as in LittleCMS.
/// </para>
/// <para>
/// To print a named color on the device the named color profile was made for, use its
/// <see cref="IccNamedColor.DeviceValues"/>, when the profile gives them, instead of converting its colorimetry.
/// </para>
/// </remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../samples/Meziantou.Framework.Imaging.Samples/ColorExamples.cs" ref:region="Imaging_NamedColors" ref:dedent="true">
/// public static (byte[] Rgb, IReadOnlyList&lt;double&gt; Inks) SpotColor(string swatchBookPath, string name)
/// {
///     var swatchBook = new IccProfile(new MetadataBlob(File.ReadAllBytes(swatchBookPath)));
///     var colors = IccNamedColorCollection.Create(swatchBook);
///     if (!colors.TryGetValue(name, out var color))
///         throw new ArgumentException($"The swatch book has no color named '{name}'.", nameof(name));
///
///     // The colorimetry of the color, converted to the device values of the display (relative colorimetric intent)
///     var transform = IccNamedColorTransform.Create(swatchBook, IccProfile.Srgb);
///     var rgb = new byte[transform.DestinationChannelCount];
///     transform.Convert(color, rgb);
///
///     // On the press the profile was made for, its device values (here CMYK inks) reproduce the color exactly
///     return (rgb, color.DeviceValues);
/// }
/// </code>
/// </example>
public sealed class IccNamedColorTransform
{
    private readonly IccPipeline _pipeline;
    private readonly bool _isUnbounded;

    private IccNamedColorTransform(IccNamedColorCollection colors, IccPipeline pipeline, bool isUnbounded)
    {
        Colors = colors;
        _pipeline = pipeline;
        _isUnbounded = isUnbounded;
    }

    /// <summary>Gets the colors of the named color profile.</summary>
    public IccNamedColorCollection Colors { get; }

    /// <summary>Gets the number of samples per converted color: 1 (grayscale), 3 (RGB) or 4 (CMYK).</summary>
    public int DestinationChannelCount => _pipeline.DestinationChannelCount;

    /// <summary>Creates the conversion of the colors of a named color profile to the device values of a profile.</summary>
    /// <param name="namedColorProfile">The named color profile.</param>
    /// <param name="destination">The profile describing the converted samples.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="IccColorTransformOptions.Default"/>.</param>
    /// <returns>The transform.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="namedColorProfile"/> or <paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="namedColorProfile"/> is not a named color profile.</exception>
    /// <exception cref="InvalidImageContentException">A profile is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A profile is valid but not supported, or cannot be used as a destination.</exception>
    public static IccNamedColorTransform Create(IccProfile namedColorProfile, IccProfile destination, IccColorTransformOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(namedColorProfile);
        ArgumentNullException.ThrowIfNull(destination);
        return Create([namedColorProfile, destination], options);
    }

    /// <summary>
    /// Creates the conversion of the colors of a named color profile through a sequence of profiles: the named color
    /// profile first, then the profiles in the order the colors go through them.
    /// </summary>
    /// <param name="profiles">The named color profile, then at least one profile; the last one describes the converted samples.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="IccColorTransformOptions.Default"/>.</param>
    /// <returns>The transform.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="profiles"/> or one of its profiles is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="profiles"/> has fewer than two profiles, or its first profile is not a named color profile.
    /// </exception>
    /// <exception cref="InvalidImageContentException">A profile is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">
    /// A profile is valid but not supported, or the sequence does not connect; or
    /// <see cref="IccColorTransformOptions.ClipFloatingPointSamples"/> is <see langword="false"/> and the conversion goes
    /// through a lookup table or a gamut mapping.
    /// </exception>
    public static IccNamedColorTransform Create(IReadOnlyList<IccProfile> profiles, IccColorTransformOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        for (var i = 0; i < profiles.Count; i++)
        {
            if (profiles[i] is null)
                throw new ArgumentNullException(nameof(profiles), string.Create(CultureInfo.InvariantCulture, $"The profile at index {i} is null."));
        }

        if (profiles.Count < 2)
            throw new ArgumentException("The sequence must have the named color profile and at least one profile after it.", nameof(profiles));

        if (profiles[0].ProfileClass != IccProfileClass.NamedColor)
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The first profile is not a named color profile: its class is {profiles[0].ProfileClass}."), nameof(profiles));

        options ??= IccColorTransformOptions.Default;
        var colors = IccNamedColorCollection.Create(profiles[0]);
        var pipeline = IccPipeline.CreateFromNamedColors(profiles, options.Intent, options.GamutMapping);
        if (!options.ClipFloatingPointSamples && !pipeline.SupportsUnbounded)
            throw new UnsupportedImageFeatureException("The conversion goes through a lookup table or a gamut mapping, which cannot convert floating-point samples outside [0, 1]: only matrix-based profiles without gamut mapping can keep them.", ImageFormat.Unknown, "Unbounded conversion through a lookup table");

        return new IccNamedColorTransform(colors, pipeline, isUnbounded: !options.ClipFloatingPointSamples);
    }

    /// <summary>Converts a named color to 8-bit samples.</summary>
    /// <param name="color">The color, usually one of <see cref="Colors"/>.</param>
    /// <param name="destination">The converted samples: exactly <see cref="DestinationChannelCount"/> samples.</param>
    /// <exception cref="ArgumentNullException"><paramref name="color"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The length of <paramref name="destination"/> is not <see cref="DestinationChannelCount"/>.</exception>
    public void Convert(IccNamedColor color, Span<byte> destination) => Convert<byte, IccByteSample>(color, destination);

    /// <summary>Converts a named color to 16-bit samples.</summary>
    /// <param name="color">The color, usually one of <see cref="Colors"/>.</param>
    /// <param name="destination">The converted samples: exactly <see cref="DestinationChannelCount"/> samples.</param>
    /// <exception cref="ArgumentNullException"><paramref name="color"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The length of <paramref name="destination"/> is not <see cref="DestinationChannelCount"/>.</exception>
    public void Convert(IccNamedColor color, Span<ushort> destination) => Convert<ushort, IccUInt16Sample>(color, destination);

    /// <summary>Converts a named color to floating-point samples, in [0, 1] unless the transform does not clip them.</summary>
    /// <param name="color">The color, usually one of <see cref="Colors"/>.</param>
    /// <param name="destination">The converted samples: exactly <see cref="DestinationChannelCount"/> samples.</param>
    /// <exception cref="ArgumentNullException"><paramref name="color"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The length of <paramref name="destination"/> is not <see cref="DestinationChannelCount"/>.</exception>
    public void Convert(IccNamedColor color, Span<float> destination)
    {
        if (!_isUnbounded)
        {
            Convert<float, IccSingleSample>(color, destination);
            return;
        }

        Span<double> values = stackalloc double[IccPipeline.MaxChannels];
        Load(color, destination.Length, nameof(destination), values);
        _pipeline.ApplyUnbounded(values);
        for (var channel = 0; channel < destination.Length; channel++)
        {
            destination[channel] = (float)values[channel];
        }
    }

    private void Convert<T, TSample>(IccNamedColor color, Span<T> destination)
        where T : unmanaged
        where TSample : IIccSample<T>
    {
        Span<double> values = stackalloc double[IccPipeline.MaxChannels];
        Load(color, destination.Length, nameof(destination), values);
        _pipeline.Apply(values);
        for (var channel = 0; channel < destination.Length; channel++)
        {
            destination[channel] = TSample.Store(values[channel]);
        }
    }

    private void Load(IccNamedColor color, int destinationLength, string parameterName, Span<double> values)
    {
        ArgumentNullException.ThrowIfNull(color);
        if (destinationLength != DestinationChannelCount)
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The destination must hold exactly {DestinationChannelCount} samples, but it has {destinationLength} samples."), parameterName);

        values.Clear();
        var xyz = color.ConnectionSpaceColor;
        values[0] = xyz.X;
        values[1] = xyz.Y;
        values[2] = xyz.Z;
    }
}
