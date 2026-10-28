using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// A color in Oklab, the perceptual color space of Björn Ottosson, in which equal distances are close to equal perceived
/// differences: the space CSS Color 4 recommends to mix colors in.
/// </summary>
/// <remarks>
/// <para>
/// The matrices are those of CSS Color 4, recomputed with 64-bit precision for the D65 white of CSS: the white has no
/// chroma, exactly. Oklab is defined from CIEXYZ relative to D65; <see cref="FromXyz"/> and <see cref="ToXyz"/> take
/// and give CIEXYZ relative to D50, as every conversion of the library, adapted with the linear Bradford transform. The
/// lightness is in [0, 1]: CSS writes it as a percentage.
/// </para>
/// <para>The values are not validated: colors outside the gamut of every display and values that are not numbers are kept as they are.</para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct Oklab : IEquatable<Oklab>
{
    /// <summary>Initializes a new instance of the <see cref="Oklab"/> struct.</summary>
    /// <param name="l">The perceived lightness, from 0 (black) to 1 (the white).</param>
    /// <param name="a">The green (negative) to red (positive) axis.</param>
    /// <param name="b">The blue (negative) to yellow (positive) axis.</param>
    public Oklab(double l, double a, double b)
    {
        L = l;
        A = a;
        B = b;
    }

    /// <summary>Gets the perceived lightness, from 0 (black) to 1 (the white).</summary>
    public double L { get; }

    /// <summary>Gets the green (negative) to red (positive) axis.</summary>
    public double A { get; }

    /// <summary>Gets the blue (negative) to yellow (positive) axis.</summary>
    public double B { get; }

    /// <summary>Converts a color from CIEXYZ relative to <see cref="CieXyz.D50"/>.</summary>
    /// <param name="xyz">The color.</param>
    /// <returns>The color.</returns>
    public static Oklab FromXyz(CieXyz xyz)
    {
        var (x, y, z) = ColorValueMath.D50ToD65.Transform(xyz.X, xyz.Y, xyz.Z);
        var (l, a, b) = ColorValueMath.XyzD65ToOklab(x, y, z);
        return new Oklab(l, a, b);
    }

    /// <summary>Converts this color to CIEXYZ relative to <see cref="CieXyz.D50"/>.</summary>
    /// <returns>The color.</returns>
    public CieXyz ToXyz()
    {
        var (x, y, z) = ColorValueMath.OklabToXyzD65(L, A, B);
        var (dx, dy, dz) = ColorValueMath.D65ToD50.Transform(x, y, z);
        return new CieXyz(dx, dy, dz);
    }

    /// <summary>Converts this color to its cylindrical form, Oklch.</summary>
    /// <returns>The color.</returns>
    public Oklch ToOklch() => Oklch.FromOklab(this);

    /// <summary>
    /// Interpolates linearly between two colors, component by component: the mixture CSS Color 4 gives for
    /// <c>in oklab</c> (without alpha).
    /// </summary>
    /// <param name="from">The color at 0.</param>
    /// <param name="to">The color at 1.</param>
    /// <param name="amount">The position between the two colors; values outside [0, 1] extrapolate.</param>
    /// <returns>The color.</returns>
    public static Oklab Lerp(Oklab from, Oklab to, double amount)
        => new(from.L + (amount * (to.L - from.L)), from.A + (amount * (to.A - from.A)), from.B + (amount * (to.B - from.B)));

    /// <summary>Converts an 8-bit RGB color to Oklab. Alpha is ignored.</summary>
    /// <param name="color">The color.</param>
    /// <param name="colorSpace">The meaning of the red, green and blue values, or <see langword="null"/> for <see cref="RgbColorSpace.Srgb"/>.</param>
    /// <returns>The color.</returns>
    public static Oklab FromRgb(Rgba32 color, RgbColorSpace? colorSpace = null) => FromRgb(colorSpace ?? RgbColorSpace.Srgb, color.R / 255.0, color.G / 255.0, color.B / 255.0);

    /// <summary>Converts a 16-bit RGB color to Oklab. Alpha is ignored.</summary>
    /// <param name="color">The color.</param>
    /// <param name="colorSpace">The meaning of the red, green and blue values, or <see langword="null"/> for <see cref="RgbColorSpace.Srgb"/>.</param>
    /// <returns>The color.</returns>
    public static Oklab FromRgb(Rgba64 color, RgbColorSpace? colorSpace = null) => FromRgb(colorSpace ?? RgbColorSpace.Srgb, color.R / 65535.0, color.G / 65535.0, color.B / 65535.0);

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

    /// <summary>Parses a color written with the CSS Color 4 <c>oklab()</c> function, such as <c>oklab(0.62796 0.22486 0.12585)</c>.</summary>
    /// <param name="value">The text.</param>
    /// <param name="alpha">The alpha of the color, in [0, 1]: 1 when the text has none.</param>
    /// <returns>The color.</returns>
    /// <remarks>
    /// The lightness is a number or a percentage of 1, clamped to [0, 1]; a and b are numbers, or percentages of 0.4. Each component may be <c>none</c>, read as 0. Function names, units and keywords are ASCII
    /// case-insensitive, and white space may surround the text; <c>calc()</c> and relative colors are not supported.
    /// </remarks>
    /// <exception cref="FormatException"><paramref name="value"/> is not a <c>oklab()</c> color.</exception>
    public static Oklab Parse(ReadOnlySpan<char> value, out double alpha)
    {
        if (!TryParse(value, out var result, out alpha))
            throw new FormatException("The value is not a CSS oklab() color.");

        return result;
    }

    /// <summary>Tries to parse a color written with the CSS Color 4 <c>oklab()</c> function, as <see cref="Parse(ReadOnlySpan{char}, out double)"/> does.</summary>
    /// <param name="value">The text.</param>
    /// <param name="result">The color, or the default value when the text is not a <c>oklab()</c> color.</param>
    /// <param name="alpha">The alpha of the color, in [0, 1]: 1 when the text has none.</param>
    /// <returns><see langword="true"/> if the text is a <c>oklab()</c> color.</returns>
    public static bool TryParse(ReadOnlySpan<char> value, out Oklab result, out double alpha)
    {
        if (CssColorSyntax.TryParse(value, out var function, out var first, out var second, out var third, out alpha) && function == CssColorSyntax.ColorFunction.Oklab)
        {
            result = new Oklab(first, second, third);
            return true;
        }

        result = default;
        alpha = 0;
        return false;
    }

    /// <summary>Formats this color with the CSS Color 4 <c>oklab()</c> function, every component as a number: <c>oklab(0.62796 0.22486 0.12585)</c>.</summary>
    /// <param name="alpha">The alpha, in [0, 1]. It is written after a <c>/</c> when it is not 1.</param>
    /// <returns>The text; <see cref="Parse(ReadOnlySpan{char}, out double)"/> reads it back.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="alpha"/> is not in [0, 1], or a component is not a finite number.</exception>
    public string ToCssString(double alpha = 1) => CssColorSyntax.Format("oklab", L, A, B, alpha, percentages: false);

    internal static Oklab FromRgb(RgbColorSpace colorSpace, double red, double green, double blue)
    {
        var (x, y, z) = colorSpace.ToXyzD65(red, green, blue);
        var (l, a, b) = ColorValueMath.XyzD65ToOklab(x, y, z);
        return new Oklab(l, a, b);
    }

    internal (double Red, double Green, double Blue) ToRgb(RgbColorSpace colorSpace)
    {
        var (x, y, z) = ColorValueMath.OklabToXyzD65(L, A, B);
        return colorSpace.FromXyzD65(x, y, z);
    }

    /// <inheritdoc />
    public bool Equals(Oklab other) => L.Equals(other.L) && A.Equals(other.A) && B.Equals(other.B);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is Oklab other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(L, A, B);

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"Oklab({L}, {A}, {B})");

    /// <summary>Compares two colors for equality, component by component.</summary>
    /// <param name="left">The first color.</param>
    /// <param name="right">The second color.</param>
    /// <returns><see langword="true"/> if all the components are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Oklab left, Oklab right) => left.Equals(right);

    /// <summary>Compares two colors for inequality, component by component.</summary>
    /// <param name="left">The first color.</param>
    /// <param name="right">The second color.</param>
    /// <returns><see langword="true"/> if a component differs; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Oklab left, Oklab right) => !left.Equals(right);
}
