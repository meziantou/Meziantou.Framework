using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A validated conversion between two pixel formats under a <see cref="PixelConversionOptions"/> policy, computed once
/// per operation (typed load, <see cref="Image.CloneAs{TPixel}(PixelConversionOptions?)"/>, encoders) before any pixel is
/// converted or any destination is allocated:
/// <list type="bullet">
/// <item>unsupported pixel types throw <see cref="NotSupportedException"/> (generic factory) before any side effect;</item>
/// <item>the retained ICC profile is checked against the destination format (<see cref="ColorProfileCompatibility"/>);</item>
/// <item>alpha removal uses <see cref="PixelConversionOptions.BackgroundColor"/> to flatten, or rejects non-opaque pixels.</item>
/// </list>
/// Callers that must not partially apply a conversion check <see cref="RequiresOpaqueSource"/> and pre-scan the source
/// with <see cref="PixelConverter.IsOpaque{TPixel}(ReadOnlySpan{TPixel})"/> before allocating the destination.
/// </summary>
internal sealed class PixelConversionPlan
{
    private PixelConversionPlan(PixelFormat sourceFormat, PixelFormat destinationFormat, Rgba64? background, IccProfile? profile, bool profileDiscarded, ImageFormat format)
    {
        SourceFormat = sourceFormat;
        DestinationFormat = destinationFormat;
        RemovesAlpha = PixelFormats.HasAlpha(sourceFormat) && !PixelFormats.HasAlpha(destinationFormat);
        Background = RemovesAlpha ? background : null;
        ColorProfile = profile;
        ColorProfileDiscarded = profileDiscarded;
        Format = format;
    }

    /// <summary>Gets the source pixel format.</summary>
    public PixelFormat SourceFormat { get; }

    /// <summary>Gets the destination pixel format.</summary>
    public PixelFormat DestinationFormat { get; }

    /// <summary>Gets a value indicating whether the source has alpha and the destination does not.</summary>
    public bool RemovesAlpha { get; }

    /// <summary>Gets the flattening background when alpha is removed, or <see langword="null"/> (non-opaque pixels are rejected, or alpha is not removed).</summary>
    public Rgba64? Background { get; }

    /// <summary>Gets a value indicating whether every source pixel must be fully opaque (alpha removed without a background).</summary>
    public bool RequiresOpaqueSource => RemovesAlpha && Background is null;

    /// <summary>Gets a value indicating whether the conversion changes the component precision (8 to 16 bits or 16 to 8 bits).</summary>
    public bool ChangesPrecision => PixelFormats.GetBitsPerComponent(SourceFormat) != PixelFormats.GetBitsPerComponent(DestinationFormat);

    /// <summary>Gets a value indicating whether the conversion reduces the component precision (16 to 8 bits, nearest rounding).</summary>
    public bool ReducesPrecision => PixelFormats.GetBitsPerComponent(SourceFormat) > PixelFormats.GetBitsPerComponent(DestinationFormat);

    /// <summary>Gets a value indicating whether the conversion computes Rec. 709 luma (color to gray).</summary>
    public bool ConvertsToGrayscale => !PixelFormats.IsGrayscale(SourceFormat) && PixelFormats.IsGrayscale(DestinationFormat);

    /// <summary>Gets a value indicating whether the source and destination formats are identical (rows are copied).</summary>
    public bool IsIdentity => SourceFormat == DestinationFormat;

    /// <summary>Gets the ICC profile to attach to the converted pixels, or <see langword="null"/>.</summary>
    public IccProfile? ColorProfile { get; }

    /// <summary>Gets a value indicating whether an incompatible source profile was removed (<see cref="PixelConversionOptions.DiscardIncompatibleColorProfile"/>).</summary>
    public bool ColorProfileDiscarded { get; }

    /// <summary>Gets the image format reported in exceptions, or <see cref="ImageFormat.Unknown"/>.</summary>
    public ImageFormat Format { get; }

    /// <summary>Creates a plan for typed pixels. Unsupported pixel types are rejected first.</summary>
    /// <typeparam name="TSource">The source pixel type.</typeparam>
    /// <typeparam name="TDestination">The destination pixel type.</typeparam>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PixelConversionOptions.Default"/>.</param>
    /// <param name="sourceProfile">The ICC profile retained on the source, or <see langword="null"/>.</param>
    /// <param name="format">The image format reported in exceptions.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="NotSupportedException">A pixel type is not a built-in pixel struct.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The profile is incompatible with the destination and may not be discarded.</exception>
    public static PixelConversionPlan Create<TSource, TDestination>(PixelConversionOptions? options = null, IccProfile? sourceProfile = null, ImageFormat format = ImageFormat.Unknown)
        where TSource : unmanaged
        where TDestination : unmanaged
        => Create(PixelFormats.GetPixelFormat<TSource>(), PixelFormats.GetPixelFormat<TDestination>(), options, sourceProfile, format);

    /// <summary>Creates a plan.</summary>
    /// <param name="sourceFormat">The source pixel format.</param>
    /// <param name="destinationFormat">The destination pixel format.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PixelConversionOptions.Default"/>.</param>
    /// <param name="sourceProfile">The ICC profile retained on the source, or <see langword="null"/>.</param>
    /// <param name="format">The image format reported in exceptions.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A format is not a supported pixel format.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The profile is incompatible with the destination and may not be discarded.</exception>
    public static PixelConversionPlan Create(PixelFormat sourceFormat, PixelFormat destinationFormat, PixelConversionOptions? options = null, IccProfile? sourceProfile = null, ImageFormat format = ImageFormat.Unknown)
    {
        _ = PixelFormats.GetBytesPerPixel(sourceFormat);
        _ = PixelFormats.GetBytesPerPixel(destinationFormat);
        options ??= PixelConversionOptions.Default;
        var profile = ColorProfileCompatibility.Resolve(sourceProfile, destinationFormat, options.DiscardIncompatibleColorProfile, format);
        return new PixelConversionPlan(sourceFormat, destinationFormat, options.BackgroundColor, profile, profileDiscarded: sourceProfile is not null && profile is null, format);
    }

    /// <summary>Throws if a source row would lose alpha under this plan (alpha removed, no background, non-opaque pixel).</summary>
    /// <typeparam name="TSource">The source pixel type; must match <see cref="SourceFormat"/>.</typeparam>
    /// <param name="row">The source row.</param>
    /// <exception cref="UnsupportedImageFeatureException">A pixel is not fully opaque.</exception>
    public void EnsureConvertible<TSource>(ReadOnlySpan<TSource> row)
        where TSource : unmanaged
    {
        EnsureFormat<TSource>(SourceFormat);
        if (RequiresOpaqueSource && PixelConverter.IndexOfNonOpaque(row) is var index and >= 0)
            throw PixelConverter.CreateNonOpaqueException(SourceFormat, DestinationFormat, index, Format);
    }

    /// <summary>Converts a row of typed pixels.</summary>
    /// <typeparam name="TSource">The source pixel type; must match <see cref="SourceFormat"/>.</typeparam>
    /// <typeparam name="TDestination">The destination pixel type; must match <see cref="DestinationFormat"/>.</typeparam>
    /// <param name="source">The source pixels.</param>
    /// <param name="destination">The destination pixels.</param>
    /// <exception cref="UnsupportedImageFeatureException">A pixel is not fully opaque and alpha would be discarded (nothing is written).</exception>
    public void ConvertRow<TSource, TDestination>(ReadOnlySpan<TSource> source, Span<TDestination> destination)
        where TSource : unmanaged
        where TDestination : unmanaged
    {
        EnsureFormat<TSource>(SourceFormat);
        EnsureFormat<TDestination>(DestinationFormat);
        PixelConverter.ConvertRow(source, destination, Background, Format);
    }

    /// <summary>Converts a row of raw native-endian pixels.</summary>
    /// <param name="source">The source bytes (a whole number of <see cref="SourceFormat"/> pixels).</param>
    /// <param name="destination">The destination bytes.</param>
    /// <returns>The number of pixels converted.</returns>
    /// <exception cref="UnsupportedImageFeatureException">A pixel is not fully opaque and alpha would be discarded (nothing is written).</exception>
    public int ConvertRow(ReadOnlySpan<byte> source, Span<byte> destination)
        => PixelConverter.ConvertRow(SourceFormat, source, DestinationFormat, destination, Background, Format);

    private static void EnsureFormat<TPixel>(PixelFormat expected)
        where TPixel : unmanaged
    {
        var actual = PixelFormats.GetPixelFormat<TPixel>();
        if (actual != expected)
            throw new InvalidOperationException($"The pixel type {actual} does not match the planned format {expected}.");
    }
}
