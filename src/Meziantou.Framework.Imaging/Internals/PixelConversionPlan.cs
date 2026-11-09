using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A validated conversion between two pixel formats under a <see cref="PixelConversionOptions"/> policy, computed once
/// per operation (typed load, <see cref="Image.CloneAs{TPixel}(PixelConversionOptions?)"/>, encoders) before any pixel is
/// converted or any destination is allocated:
/// <list type="bullet">
/// <item>unsupported pixel types throw <see cref="NotSupportedException"/> (generic factory) before any side effect;</item>
/// <item>the retained ICC profile is checked against the destination format (<see cref="ColorProfileCompatibility"/>);</item>
/// <item>alpha removal uses <see cref="PixelConversionOptions.BackgroundColor"/> to flatten, or rejects non-opaque pixels;</item>
/// <item>floating-point samples that the destination cannot store follow <see cref="Floats"/>;</item>
/// <item>
/// a conversion between CMYK and another kind of pixels converts colors (<see cref="CmykColorConversion"/>), only with
/// <see cref="PixelConversionOptions.CmykConversion"/>: the source side (alpha, floating-point samples) is validated by the
/// layout conversion to or from its RGB rows.
/// </item>
/// </list>
/// Callers that must not partially apply a conversion check <see cref="RequiresValidation"/> and pre-scan the source
/// with <see cref="ValidateRow"/> before allocating the destination, after giving the tone mapping its peak when
/// <see cref="RequiresPeak"/> (<see cref="SetPeak"/>).
/// </summary>
internal sealed class PixelConversionPlan
{
    private readonly PixelConversionOptions _options;

    // The color conversion between CMYK and another kind of pixels; null for the other conversions
    private readonly CmykColorConversion? _cmyk;

    private PixelConversionPlan(PixelFormat sourceFormat, PixelFormat destinationFormat, PixelConversionOptions options, IccProfile? profile, bool profileDiscarded, ImageFormat format, CmykColorConversion? cmyk = null)
    {
        _cmyk = cmyk;
        var background = options.BackgroundColor;
        _options = options;
        SourceFormat = sourceFormat;
        DestinationFormat = destinationFormat;
        RemovesAlpha = PixelFormats.HasAlpha(sourceFormat) && !PixelFormats.HasAlpha(destinationFormat);
        Background = RemovesAlpha ? background : null;
        ColorProfile = profile;
        ColorProfileDiscarded = profileDiscarded;
        Format = format;
        Floats = FloatSampleConversion.Create(options);
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

    // The layout conversion of the source to RGB rows validates the source of a conversion to CMYK
    private PixelConversionPlan? SourceLayout => _cmyk is { FromCmyk: false } cmyk ? cmyk.Layout : null;

    /// <summary>Gets a value indicating whether floating-point samples are converted to a format that cannot store all of them (an integer format, or <see cref="PixelFormat.RgbaFloat"/> to <see cref="PixelFormat.RgbaHalf"/>).</summary>
    public bool ChecksFloatSamples => SourceLayout is { } layout ? layout.ChecksFloatSamples : PixelFormats.IsFloatingPoint(SourceFormat) && (!PixelFormats.IsFloatingPoint(DestinationFormat) || (SourceFormat == PixelFormat.RgbaFloat && DestinationFormat == PixelFormat.RgbaHalf));

    /// <summary>Gets a value indicating whether a source row can be rejected: <see cref="RequiresOpaqueSource"/> or <see cref="ChecksFloatSamples"/>.</summary>
    public bool RequiresValidation => RequiresOpaqueSource || ChecksFloatSamples;

    /// <summary>Gets a value indicating whether the tone mapping needs the peak of the source pixels (<see cref="SetPeak"/>) before any row is converted or validated.</summary>
    public bool RequiresPeak => SourceLayout is { } layout ? layout.RequiresPeak : ChecksFloatSamples && !PixelFormats.IsFloatingPoint(DestinationFormat) && FloatSampleConversion.NeedsPeak(_options);

    /// <summary>Gets the policy of the floating-point samples that the destination cannot store.</summary>
    public FloatSampleConversion Floats { get; private set; }

    /// <summary>Gives the tone mapping the peak of the source pixels, when <see cref="RequiresPeak"/>.</summary>
    /// <param name="peak">The largest finite color component of the converted pixels (<see cref="PixelConverter.GetLargestFiniteColor"/>).</param>
    public void SetPeak(double peak)
    {
        SourceLayout?.SetPeak(peak);
        Floats = FloatSampleConversion.Create(_options, peak);
    }

    /// <summary>Gets a value indicating whether the conversion changes the component precision (8 to 16 bits or 16 to 8 bits).</summary>
    public bool ChangesPrecision => PixelFormats.GetBitsPerComponent(SourceFormat) != PixelFormats.GetBitsPerComponent(DestinationFormat);

    /// <summary>Gets a value indicating whether the conversion reduces the component precision (16 to 8 bits, nearest rounding).</summary>
    public bool ReducesPrecision => PixelFormats.GetBitsPerComponent(SourceFormat) > PixelFormats.GetBitsPerComponent(DestinationFormat);

    /// <summary>Gets a value indicating whether the conversion computes Rec. 709 luma (color to gray).</summary>
    public bool ConvertsToGrayscale => !PixelFormats.IsGrayscale(SourceFormat) && PixelFormats.IsGrayscale(DestinationFormat);

    /// <summary>Gets a value indicating whether the conversion converts colors between CMYK and another kind of pixels.</summary>
    public bool ConvertsCmykColors => _cmyk is not null;

    /// <summary>Gets a value indicating whether the source and destination formats are identical (rows are copied).</summary>
    public bool IsIdentity => SourceFormat == DestinationFormat;

    /// <summary>Gets the ICC profile to attach to the converted pixels, or <see langword="null"/>.</summary>
    public IccProfile? ColorProfile { get; private set; }

    /// <summary>Gets a value indicating whether an incompatible source profile was removed (<see cref="PixelConversionOptions.DiscardIncompatibleColorProfile"/>).</summary>
    public bool ColorProfileDiscarded { get; private set; }

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
        if (PixelFormats.IsCmyk(sourceFormat) != PixelFormats.IsCmyk(destinationFormat))
        {
            if (options.CmykConversion is null)
                throw PixelConverter.CreateCmykConversionException(sourceFormat, destinationFormat, format);

            var cmyk = CmykColorConversion.Create(sourceFormat, destinationFormat, options, sourceProfile, format);
            return new PixelConversionPlan(sourceFormat, destinationFormat, options, cmyk.ColorProfile, profileDiscarded: sourceProfile is not null && cmyk.ColorProfile is null, format, cmyk);
        }

        var profile = ColorProfileCompatibility.Resolve(sourceProfile, destinationFormat, options.DiscardIncompatibleColorProfile, format);
        return new PixelConversionPlan(sourceFormat, destinationFormat, options, profile, profileDiscarded: sourceProfile is not null && profile is null, format);
    }

    /// <summary>
    /// Replaces the ICC profile of the source with one found once the plan was made (a BMP profile stored after the pixel
    /// data), with the rules of <see cref="Create(PixelFormat, PixelFormat, PixelConversionOptions?, IccProfile?, ImageFormat)"/>.
    /// The conversion of the samples does not depend on the profile.
    /// </summary>
    /// <param name="sourceProfile">The ICC profile of the source.</param>
    /// <exception cref="UnsupportedImageFeatureException">The profile is incompatible with the destination and may not be discarded.</exception>
    public void AdoptSourceColorProfile(IccProfile sourceProfile)
    {
        ColorProfile = ColorProfileCompatibility.Resolve(sourceProfile, DestinationFormat, _options.DiscardIncompatibleColorProfile, Format);
        ColorProfileDiscarded = ColorProfile is null;
    }

    /// <summary>Throws if a source row would lose alpha under this plan (alpha removed, no background, non-opaque pixel).</summary>
    /// <typeparam name="TSource">The source pixel type; must match <see cref="SourceFormat"/>.</typeparam>
    /// <param name="row">The source row.</param>
    /// <exception cref="UnsupportedImageFeatureException">A pixel is not fully opaque.</exception>
    public void EnsureConvertible<TSource>(ReadOnlySpan<TSource> row)
        where TSource : unmanaged
    {
        EnsureFormat<TSource>(SourceFormat);
        if (_cmyk is not null)
        {
            SourceLayout?.EnsureConvertible(row);
            return;
        }

        if (ChecksFloatSamples)
        {
            PixelConverter.ValidateFloatRow(row, SourceFormat, DestinationFormat, Background, Format, Floats);
        }
        else if (RequiresOpaqueSource && PixelConverter.IndexOfNonOpaque(row) is var index and >= 0)
        {
            throw PixelConverter.CreateNonOpaqueException(SourceFormat, DestinationFormat, index, Format);
        }
    }

    /// <summary>Throws if a raw source row cannot be converted under this plan (see <see cref="EnsureConvertible{TSource}(ReadOnlySpan{TSource})"/>).</summary>
    /// <param name="row">The source bytes (a whole number of <see cref="SourceFormat"/> pixels).</param>
    /// <exception cref="UnsupportedImageFeatureException">A pixel cannot be converted.</exception>
    public void ValidateRow(ReadOnlySpan<byte> row)
    {
        if (_cmyk is not null)
        {
            SourceLayout?.ValidateRow(row);
            return;
        }

        PixelConverter.ValidateRow(SourceFormat, row, DestinationFormat, Background, Format, Floats);
    }

    /// <summary>Converts a row of typed pixels.</summary>
    /// <typeparam name="TSource">The source pixel type; must match <see cref="SourceFormat"/>.</typeparam>
    /// <typeparam name="TDestination">The destination pixel type; must match <see cref="DestinationFormat"/>.</typeparam>
    /// <param name="source">The source pixels.</param>
    /// <param name="destination">The destination pixels.</param>
    /// <exception cref="UnsupportedImageFeatureException">A pixel is not fully opaque and alpha would be discarded, or a floating-point sample cannot be stored (nothing is written).</exception>
    public void ConvertRow<TSource, TDestination>(ReadOnlySpan<TSource> source, Span<TDestination> destination)
        where TSource : unmanaged
        where TDestination : unmanaged
    {
        EnsureFormat<TSource>(SourceFormat);
        EnsureFormat<TDestination>(DestinationFormat);
        if (_cmyk is not null)
        {
            if (destination.Length < source.Length)
                throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The destination has {destination.Length} pixels but {source.Length} are required."), nameof(destination));

            _cmyk.ConvertRow(unsafe(MemoryMarshal.AsBytes(source)), unsafe(MemoryMarshal.AsBytes(destination)), SourceFormat, DestinationFormat);
            return;
        }

        PixelConverter.ConvertRow(source, destination, Background, Format, Floats);
    }

    /// <summary>Converts a row of raw native-endian pixels.</summary>
    /// <param name="source">The source bytes (a whole number of <see cref="SourceFormat"/> pixels).</param>
    /// <param name="destination">The destination bytes.</param>
    /// <returns>The number of pixels converted.</returns>
    /// <exception cref="UnsupportedImageFeatureException">A pixel is not fully opaque and alpha would be discarded (nothing is written).</exception>
    public int ConvertRow(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (_cmyk is null)
            return PixelConverter.ConvertRow(SourceFormat, source, DestinationFormat, destination, Background, Format, Floats);

        var sourceBytesPerPixel = PixelFormats.GetBytesPerPixel(SourceFormat);
        var destinationBytesPerPixel = PixelFormats.GetBytesPerPixel(DestinationFormat);
        if (source.Length % sourceBytesPerPixel != 0)
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The source length ({source.Length} bytes) is not a multiple of the {SourceFormat} pixel size ({sourceBytesPerPixel} bytes)."), nameof(source));

        var width = source.Length / sourceBytesPerPixel;
        if (destination.Length / destinationBytesPerPixel < width)
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The destination has {destination.Length} bytes but {(long)width * destinationBytesPerPixel} are required."), nameof(destination));

        _cmyk.ConvertRow(source, destination, SourceFormat, DestinationFormat);
        return width;
    }

    private static void EnsureFormat<TPixel>(PixelFormat expected)
        where TPixel : unmanaged
    {
        var actual = PixelFormats.GetPixelFormat<TPixel>();
        if (actual != expected)
            throw new InvalidOperationException($"The pixel type {actual} does not match the planned format {expected}.");
    }
}
