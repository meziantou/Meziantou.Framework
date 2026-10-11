using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Conversions between <see cref="ImageResolution"/> (dots per inch) and the encoded resolution fields of PNG
/// (<c>pHYs</c>, pixels per meter) and JPEG (JFIF density). One inch is exactly 0.0254 meter.
/// </summary>
/// <remarks>
/// Decoding computes <c>dpi = pixelsPerMeter * 0.0254</c> (or <c>dotsPerCentimeter * 2.54</c>) in IEEE double precision,
/// so the same encoded value always yields the same <see cref="ImageResolution"/>. Encoding rounds to the nearest integer
/// unit (ties away from zero), which reproduces the original encoded value for every decoded resolution.
/// Aspect-ratio-only fields (PNG unit 0, JFIF units 0) are not physical resolutions and decode to <see langword="null"/>.
/// </remarks>
internal static class ResolutionConversion
{
    public const double MetersPerInch = 0.0254;
    public const double CentimetersPerInch = 2.54;

    /// <summary>The largest value of a PNG 4-byte unsigned integer (2^31 - 1).</summary>
    public const uint MaxPngInteger = int.MaxValue;

    /// <summary>Converts a PNG <c>pHYs</c> chunk to a resolution.</summary>
    /// <param name="x">Pixels per unit, X axis.</param>
    /// <param name="y">Pixels per unit, Y axis.</param>
    /// <param name="unit">The unit specifier: 1 is the meter, 0 is unknown (aspect ratio only).</param>
    /// <returns>The resolution, or <see langword="null"/> for aspect-ratio-only, zero, or unknown-unit values.</returns>
    public static ImageResolution? FromPngPhys(uint x, uint y, byte unit)
    {
        if (unit != 1 || x == 0 || y == 0)
            return null;

        return new ImageResolution(x * MetersPerInch, y * MetersPerInch);
    }

    /// <summary>Converts a resolution to PNG <c>pHYs</c> pixels per meter (unit 1).</summary>
    /// <param name="resolution">The resolution.</param>
    /// <returns>The pixels per meter on each axis.</returns>
    /// <exception cref="UnsupportedImageFeatureException">A rounded value is zero or exceeds 2^31 - 1.</exception>
    public static (uint X, uint Y) ToPngPhys(ImageResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        return (ToInteger(resolution.HorizontalDpi / MetersPerInch, MaxPngInteger, ImageFormat.Png), ToInteger(resolution.VerticalDpi / MetersPerInch, MaxPngInteger, ImageFormat.Png));
    }

    /// <summary>Converts the BMP <c>biXPelsPerMeter</c>/<c>biYPelsPerMeter</c> fields to a resolution.</summary>
    /// <param name="x">Pixels per meter, X axis.</param>
    /// <param name="y">Pixels per meter, Y axis.</param>
    /// <returns>The resolution, or <see langword="null"/> when either field is zero or negative (no resolution is declared).</returns>
    public static ImageResolution? FromBmpPixelsPerMeter(int x, int y)
    {
        if (x <= 0 || y <= 0)
            return null;

        return new ImageResolution(x * MetersPerInch, y * MetersPerInch);
    }

    /// <summary>Converts a resolution to BMP pixels per meter.</summary>
    /// <param name="resolution">The resolution.</param>
    /// <returns>The pixels per meter on each axis.</returns>
    /// <exception cref="UnsupportedImageFeatureException">A rounded value is zero or exceeds 2^31 - 1.</exception>
    public static (int X, int Y) ToBmpPixelsPerMeter(ImageResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        return ((int)ToInteger(resolution.HorizontalDpi / MetersPerInch, int.MaxValue, ImageFormat.Bmp), (int)ToInteger(resolution.VerticalDpi / MetersPerInch, int.MaxValue, ImageFormat.Bmp));
    }

    /// <summary>The denominator of the RATIONAL values the TIFF encoder writes (four decimals of a dot per inch).</summary>
    public const uint TiffResolutionDenominator = 10_000;

    /// <summary>Converts a resolution to the TIFF <c>XResolution</c>/<c>YResolution</c> RATIONAL values, in dots per inch.</summary>
    /// <param name="resolution">The resolution.</param>
    /// <returns>The numerator of each axis; the denominator is <see cref="TiffResolutionDenominator"/>.</returns>
    /// <exception cref="UnsupportedImageFeatureException">A rounded value is zero or does not fit the unsigned 32-bit numerator.</exception>
    public static (uint X, uint Y) ToTiffRational(ImageResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        return (ToInteger(resolution.HorizontalDpi * TiffResolutionDenominator, uint.MaxValue, ImageFormat.Tiff), ToInteger(resolution.VerticalDpi * TiffResolutionDenominator, uint.MaxValue, ImageFormat.Tiff));
    }

    /// <summary>Converts JFIF density fields to a resolution.</summary>
    /// <param name="units">0: aspect ratio only, 1: dots per inch, 2: dots per centimeter.</param>
    /// <param name="x">The horizontal density.</param>
    /// <param name="y">The vertical density.</param>
    /// <returns>The resolution, or <see langword="null"/> for aspect-ratio-only, zero, or unknown-unit values.</returns>
    public static ImageResolution? FromJfifDensity(byte units, ushort x, ushort y)
    {
        if (x == 0 || y == 0)
            return null;

        return units switch
        {
            1 => new ImageResolution(x, y),
            2 => new ImageResolution(x * CentimetersPerInch, y * CentimetersPerInch),
            _ => null,
        };
    }

    /// <summary>
    /// Converts a resolution to JFIF density fields. Dots per inch (units 1) are used when both values are whole numbers of
    /// dots per inch or when dots per centimeter would not be exact either; dots per centimeter (units 2) are used when
    /// they represent the resolution exactly (for example a resolution decoded from units 2).
    /// </summary>
    /// <param name="resolution">The resolution.</param>
    /// <returns>The units and the densities.</returns>
    /// <exception cref="UnsupportedImageFeatureException">A rounded value is zero or exceeds 65,535.</exception>
    public static (byte Units, ushort X, ushort Y) ToJfifDensity(ImageResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        var h = resolution.HorizontalDpi;
        var v = resolution.VerticalDpi;
        if (!(IsWhole(h) && IsWhole(v)))
        {
            var hcm = Math.Round(h / CentimetersPerInch, MidpointRounding.AwayFromZero);
            var vcm = Math.Round(v / CentimetersPerInch, MidpointRounding.AwayFromZero);
            if (hcm is >= 1 and <= ushort.MaxValue && vcm is >= 1 and <= ushort.MaxValue && hcm * CentimetersPerInch == h && vcm * CentimetersPerInch == v)
                return (2, (ushort)hcm, (ushort)vcm);
        }

        return (1, (ushort)ToInteger(h, ushort.MaxValue, ImageFormat.Jpeg), (ushort)ToInteger(v, ushort.MaxValue, ImageFormat.Jpeg));

        static bool IsWhole(double value) => Math.Floor(value) == value;
    }

    private static uint ToInteger(double value, uint max, ImageFormat format)
    {
        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        if (rounded < 1 || rounded > max)
            throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"The resolution cannot be represented in a {format} file (encoded value {rounded} is outside 1..{max})."), format, "Resolution range");

        return (uint)rounded;
    }
}
