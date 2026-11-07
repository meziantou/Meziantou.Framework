using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>A color in the CIE 1976 L*a*b* color space (CIELAB), relative to a white, <see cref="CieXyz.D50"/> by default.</summary>
/// <remarks>
/// <para>
/// The equations are those of CIE 15:2004 with the exact constants (6/29)^3 and (29/3)^3, as CSS Color 4 writes them. The
/// conversions from and to RGB go through CIEXYZ relative to D50, as the <c>lab()</c> colors of CSS and the CIELAB
/// colors of ICC profiles do.
/// </para>
/// <para>The values are not validated: colors outside the gamut of every display and values that are not numbers are kept as they are.</para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct CieLab : IEquatable<CieLab>
{
    /// <summary>Initializes a new instance of the <see cref="CieLab"/> struct.</summary>
    /// <param name="l">The lightness, from 0 (black) to 100 (the white).</param>
    /// <param name="a">The green (negative) to red (positive) axis.</param>
    /// <param name="b">The blue (negative) to yellow (positive) axis.</param>
    public CieLab(double l, double a, double b)
    {
        L = l;
        A = a;
        B = b;
    }

    /// <summary>Gets the lightness, from 0 (black) to 100 (the white).</summary>
    public double L { get; }

    /// <summary>Gets the green (negative) to red (positive) axis.</summary>
    public double A { get; }

    /// <summary>Gets the blue (negative) to yellow (positive) axis.</summary>
    public double B { get; }

    /// <summary>Converts a color from CIEXYZ relative to <see cref="CieXyz.D50"/>.</summary>
    /// <param name="xyz">The color.</param>
    /// <returns>The color.</returns>
    public static CieLab FromXyz(CieXyz xyz) => FromXyz(xyz, CieXyz.D50);

    /// <summary>Converts a color from CIEXYZ relative to a white.</summary>
    /// <param name="xyz">The color.</param>
    /// <param name="whitePoint">The white the color is relative to: it has a lightness of 100 and no chroma.</param>
    /// <returns>The color.</returns>
    public static CieLab FromXyz(CieXyz xyz, CieXyz whitePoint)
    {
        var (l, a, b) = ColorValueMath.XyzToLab(xyz.X, xyz.Y, xyz.Z, whitePoint.X, whitePoint.Y, whitePoint.Z);
        return new CieLab(l, a, b);
    }

    /// <summary>Converts this color to CIEXYZ relative to <see cref="CieXyz.D50"/>.</summary>
    /// <returns>The color.</returns>
    public CieXyz ToXyz() => ToXyz(CieXyz.D50);

    /// <summary>Converts this color to CIEXYZ relative to a white.</summary>
    /// <param name="whitePoint">The white this color is relative to.</param>
    /// <returns>The color.</returns>
    public CieXyz ToXyz(CieXyz whitePoint)
    {
        var (x, y, z) = ColorValueMath.LabToXyz(L, A, B, whitePoint.X, whitePoint.Y, whitePoint.Z);
        return new CieXyz(x, y, z);
    }

    /// <summary>Converts this color to its cylindrical form, CIELCh.</summary>
    /// <returns>The color.</returns>
    public CieLch ToLch() => CieLch.FromLab(this);

    /// <summary>Converts an 8-bit RGB color to CieLab. Alpha is ignored.</summary>
    /// <param name="color">The color.</param>
    /// <param name="colorSpace">The meaning of the red, green and blue values, or <see langword="null"/> for <see cref="RgbColorSpace.Srgb"/>.</param>
    /// <returns>The color.</returns>
    public static CieLab FromRgb(Rgba32 color, RgbColorSpace? colorSpace = null) => FromRgb(colorSpace ?? RgbColorSpace.Srgb, color.R / 255.0, color.G / 255.0, color.B / 255.0);

    /// <summary>Converts a 16-bit RGB color to CieLab. Alpha is ignored.</summary>
    /// <param name="color">The color.</param>
    /// <param name="colorSpace">The meaning of the red, green and blue values, or <see langword="null"/> for <see cref="RgbColorSpace.Srgb"/>.</param>
    /// <returns>The color.</returns>
    public static CieLab FromRgb(Rgba64 color, RgbColorSpace? colorSpace = null) => FromRgb(colorSpace ?? RgbColorSpace.Srgb, color.R / 65535.0, color.G / 65535.0, color.B / 65535.0);

    /// <summary>Converts this color to an opaque 8-bit RGB color, clipped to the gamut of the space component by component.</summary>
    /// <param name="colorSpace">The meaning of the red, green and blue values, or <see langword="null"/> for <see cref="RgbColorSpace.Srgb"/>.</param>
    /// <returns>The color.</returns>
    /// <exception cref="UnsupportedImageFeatureException">The space cannot convert colors to RGB values (<see cref="RgbColorSpace.CanConvertFromXyz"/>).</exception>
    public Rgba32 ToRgba32(RgbColorSpace? colorSpace = null)
    {
        var (r, g, b) = ToRgb(colorSpace ?? RgbColorSpace.Srgb);
        return new Rgba32(ColorValueMath.ToByte(r), ColorValueMath.ToByte(g), ColorValueMath.ToByte(b));
    }

    /// <summary>Converts this color to an opaque 16-bit RGB color, clipped to the gamut of the space component by component.</summary>
    /// <param name="colorSpace">The meaning of the red, green and blue values, or <see langword="null"/> for <see cref="RgbColorSpace.Srgb"/>.</param>
    /// <returns>The color.</returns>
    /// <exception cref="UnsupportedImageFeatureException">The space cannot convert colors to RGB values (<see cref="RgbColorSpace.CanConvertFromXyz"/>).</exception>
    public Rgba64 ToRgba64(RgbColorSpace? colorSpace = null)
    {
        var (r, g, b) = ToRgb(colorSpace ?? RgbColorSpace.Srgb);
        return new Rgba64(ColorValueMath.ToUInt16(r), ColorValueMath.ToUInt16(g), ColorValueMath.ToUInt16(b), ushort.MaxValue);
    }

    /// <summary>Parses a color written with the CSS Color 4 <c>lab()</c> function, such as <c>lab(52.2345 40.1645 59.9971)</c>.</summary>
    /// <param name="value">The text.</param>
    /// <param name="alpha">The alpha of the color, in [0, 1]: 1 when the text has none.</param>
    /// <returns>The color.</returns>
    /// <remarks>
    /// The lightness is a number or a percentage of 100, clamped to [0, 100]; a and b are numbers, or percentages of 125. Each component may be <c>none</c>, read as 0. Function names, units and keywords are ASCII
    /// case-insensitive, and white space may surround the text; <c>calc()</c> and relative colors are not supported.
    /// </remarks>
    /// <exception cref="FormatException"><paramref name="value"/> is not a <c>lab()</c> color.</exception>
    public static CieLab Parse(ReadOnlySpan<char> value, out double alpha)
    {
        if (!TryParse(value, out var result, out alpha))
            throw new FormatException("The value is not a CSS lab() color.");

        return result;
    }

    /// <summary>Tries to parse a color written with the CSS Color 4 <c>lab()</c> function, as <see cref="Parse(ReadOnlySpan{char}, out double)"/> does.</summary>
    /// <param name="value">The text.</param>
    /// <param name="result">The color, or the default value when the text is not a <c>lab()</c> color.</param>
    /// <param name="alpha">The alpha of the color, in [0, 1]: 1 when the text has none.</param>
    /// <returns><see langword="true"/> if the text is a <c>lab()</c> color.</returns>
    public static bool TryParse(ReadOnlySpan<char> value, out CieLab result, out double alpha)
    {
        if (CssColorSyntax.TryParse(value, out var function, out var first, out var second, out var third, out alpha) && function == CssColorSyntax.ColorFunction.Lab)
        {
            result = new CieLab(first, second, third);
            return true;
        }

        result = default;
        alpha = 0;
        return false;
    }

    /// <summary>Formats this color with the CSS Color 4 <c>lab()</c> function, every component as a number: <c>lab(52.2345 40.1645 59.9971)</c>.</summary>
    /// <param name="alpha">The alpha, in [0, 1]. It is written after a <c>/</c> when it is not 1.</param>
    /// <returns>The text; <see cref="Parse(ReadOnlySpan{char}, out double)"/> reads it back.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="alpha"/> is not in [0, 1], or a component is not a finite number.</exception>
    public string ToCssString(double alpha = 1) => CssColorSyntax.Format("lab", L, A, B, alpha, percentages: false);

    internal static CieLab FromRgb(RgbColorSpace colorSpace, double red, double green, double blue)
    {
        var (x, y, z) = colorSpace.ToXyzD50(red, green, blue);
        return FromXyz(new CieXyz(x, y, z));
    }

    internal (double Red, double Green, double Blue) ToRgb(RgbColorSpace colorSpace)
    {
        var xyz = ToXyz();
        return colorSpace.FromXyzD50(xyz.X, xyz.Y, xyz.Z);
    }

    /// <inheritdoc />
    public bool Equals(CieLab other) => L.Equals(other.L) && A.Equals(other.A) && B.Equals(other.B);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is CieLab other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(L, A, B);

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"CieLab({L}, {A}, {B})");

    /// <summary>Compares two colors for equality, component by component.</summary>
    /// <param name="left">The first color.</param>
    /// <param name="right">The second color.</param>
    /// <returns><see langword="true"/> if all the components are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(CieLab left, CieLab right) => left.Equals(right);

    /// <summary>Compares two colors for inequality, component by component.</summary>
    /// <param name="left">The first color.</param>
    /// <param name="right">The second color.</param>
    /// <returns><see langword="true"/> if a component differs; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(CieLab left, CieLab right) => !left.Equals(right);
}
