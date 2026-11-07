using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>A color in the cylindrical form of CIELAB (CIELCh): its lightness, chroma and hue angle.</summary>
/// <remarks>
/// <para>
/// The hue is in degrees, counterclockwise from the positive a axis; the conversions from CIELAB give it in [0, 360). The
/// hue of a gray has no meaning: it is the angle of its tiny a and b, or 0 when they are exactly zero.
/// </para>
/// <para>The values are not validated: colors outside the gamut of every display and values that are not numbers are kept as they are.</para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct CieLch : IEquatable<CieLch>
{
    /// <summary>Initializes a new instance of the <see cref="CieLch"/> struct.</summary>
    /// <param name="l">The lightness, from 0 (black) to 100 (the white).</param>
    /// <param name="c">The chroma, the distance from the gray of the same lightness.</param>
    /// <param name="h">The hue angle, in degrees.</param>
    public CieLch(double l, double c, double h)
    {
        L = l;
        C = c;
        H = h;
    }

    /// <summary>Gets the lightness, from 0 (black) to 100 (the white).</summary>
    public double L { get; }

    /// <summary>Gets the chroma, the distance from the gray of the same lightness.</summary>
    public double C { get; }

    /// <summary>Gets the hue angle, in degrees.</summary>
    public double H { get; }

    /// <summary>Converts a color from CIELAB.</summary>
    /// <param name="lab">The color.</param>
    /// <returns>The color, with the hue in [0, 360).</returns>
    public static CieLch FromLab(CieLab lab)
    {
        var (c, h) = ColorValueMath.ToPolar(lab.A, lab.B);
        return new CieLch(lab.L, c, h);
    }

    /// <summary>Converts this color to CIELAB.</summary>
    /// <returns>The color.</returns>
    public CieLab ToLab()
    {
        var (a, b) = ColorValueMath.FromPolar(C, H);
        return new CieLab(L, a, b);
    }

    /// <summary>Converts an 8-bit RGB color to CieLch. Alpha is ignored.</summary>
    /// <param name="color">The color.</param>
    /// <param name="colorSpace">The meaning of the red, green and blue values, or <see langword="null"/> for <see cref="RgbColorSpace.Srgb"/>.</param>
    /// <returns>The color.</returns>
    public static CieLch FromRgb(Rgba32 color, RgbColorSpace? colorSpace = null) => FromRgb(colorSpace ?? RgbColorSpace.Srgb, color.R / 255.0, color.G / 255.0, color.B / 255.0);

    /// <summary>Converts a 16-bit RGB color to CieLch. Alpha is ignored.</summary>
    /// <param name="color">The color.</param>
    /// <param name="colorSpace">The meaning of the red, green and blue values, or <see langword="null"/> for <see cref="RgbColorSpace.Srgb"/>.</param>
    /// <returns>The color.</returns>
    public static CieLch FromRgb(Rgba64 color, RgbColorSpace? colorSpace = null) => FromRgb(colorSpace ?? RgbColorSpace.Srgb, color.R / 65535.0, color.G / 65535.0, color.B / 65535.0);

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

    /// <summary>Parses a color written with the CSS Color 4 <c>lch()</c> function, such as <c>lch(52.2345 72.2 56.2)</c>.</summary>
    /// <param name="value">The text.</param>
    /// <param name="alpha">The alpha of the color, in [0, 1]: 1 when the text has none.</param>
    /// <returns>The color.</returns>
    /// <remarks>
    /// The lightness is a number or a percentage of 100, clamped to [0, 100]; the chroma is a number or a percentage of 150, clamped to 0 and above; the hue is a number of degrees or an angle (<c>deg</c>, <c>grad</c>, <c>rad</c>, <c>turn</c>), brought to [0, 360). Each component may be <c>none</c>, read as 0. Function names, units and keywords are ASCII
    /// case-insensitive, and white space may surround the text; <c>calc()</c> and relative colors are not supported.
    /// </remarks>
    /// <exception cref="FormatException"><paramref name="value"/> is not a <c>lch()</c> color.</exception>
    public static CieLch Parse(ReadOnlySpan<char> value, out double alpha)
    {
        if (!TryParse(value, out var result, out alpha))
            throw new FormatException("The value is not a CSS lch() color.");

        return result;
    }

    /// <summary>Tries to parse a color written with the CSS Color 4 <c>lch()</c> function, as <see cref="Parse(ReadOnlySpan{char}, out double)"/> does.</summary>
    /// <param name="value">The text.</param>
    /// <param name="result">The color, or the default value when the text is not a <c>lch()</c> color.</param>
    /// <param name="alpha">The alpha of the color, in [0, 1]: 1 when the text has none.</param>
    /// <returns><see langword="true"/> if the text is a <c>lch()</c> color.</returns>
    public static bool TryParse(ReadOnlySpan<char> value, out CieLch result, out double alpha)
    {
        if (CssColorSyntax.TryParse(value, out var function, out var first, out var second, out var third, out alpha) && function == CssColorSyntax.ColorFunction.Lch)
        {
            result = new CieLch(first, second, third);
            return true;
        }

        result = default;
        alpha = 0;
        return false;
    }

    /// <summary>Formats this color with the CSS Color 4 <c>lch()</c> function, every component as a number: <c>lch(52.2345 72.2 56.2)</c>.</summary>
    /// <param name="alpha">The alpha, in [0, 1]. It is written after a <c>/</c> when it is not 1.</param>
    /// <returns>The text; <see cref="Parse(ReadOnlySpan{char}, out double)"/> reads it back.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="alpha"/> is not in [0, 1], or a component is not a finite number.</exception>
    public string ToCssString(double alpha = 1) => CssColorSyntax.Format("lch", L, C, H, alpha, percentages: false);

    private static CieLch FromRgb(RgbColorSpace colorSpace, double red, double green, double blue) => FromLab(CieLab.FromRgb(colorSpace, red, green, blue));

    private (double Red, double Green, double Blue) ToRgb(RgbColorSpace colorSpace) => ToLab().ToRgb(colorSpace);

    /// <inheritdoc />
    public bool Equals(CieLch other) => L.Equals(other.L) && C.Equals(other.C) && H.Equals(other.H);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is CieLch other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(L, C, H);

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"CieLch({L}, {C}, {H})");

    /// <summary>Compares two colors for equality, component by component.</summary>
    /// <param name="left">The first color.</param>
    /// <param name="right">The second color.</param>
    /// <returns><see langword="true"/> if all the components are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(CieLch left, CieLch right) => left.Equals(right);

    /// <summary>Compares two colors for inequality, component by component.</summary>
    /// <param name="left">The first color.</param>
    /// <param name="right">The second color.</param>
    /// <returns><see langword="true"/> if a component differs; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(CieLch left, CieLch right) => !left.Equals(right);
}
