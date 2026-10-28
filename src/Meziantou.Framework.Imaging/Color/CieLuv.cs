using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>A color in the CIE 1976 L*u*v* color space (CIELUV), relative to a white, <see cref="CieXyz.D50"/> by default.</summary>
/// <remarks>
/// <para>
/// The equations are those of CIE 15:2004 with the exact constants (6/29)^3 and (29/3)^3: the lightness is that of
/// CIELAB, and u* and v* are the distances of the chromaticity (u', v') of the color from the one of the white. Black has
/// no chromaticity: its u* and v* are 0.
/// </para>
/// <para>The values are not validated: colors outside the gamut of every display and values that are not numbers are kept as they are.</para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct CieLuv : IEquatable<CieLuv>
{
    /// <summary>Initializes a new instance of the <see cref="CieLuv"/> struct.</summary>
    /// <param name="l">The lightness, from 0 (black) to 100 (the white).</param>
    /// <param name="u">The u* axis, green (negative) to red (positive).</param>
    /// <param name="v">The v* axis, blue (negative) to yellow (positive).</param>
    public CieLuv(double l, double u, double v)
    {
        L = l;
        U = u;
        V = v;
    }

    /// <summary>Gets the lightness, from 0 (black) to 100 (the white).</summary>
    public double L { get; }

    /// <summary>Gets the u* axis, green (negative) to red (positive).</summary>
    public double U { get; }

    /// <summary>Gets the v* axis, blue (negative) to yellow (positive).</summary>
    public double V { get; }

    /// <summary>Converts a color from CIEXYZ relative to <see cref="CieXyz.D50"/>.</summary>
    /// <param name="xyz">The color.</param>
    /// <returns>The color.</returns>
    public static CieLuv FromXyz(CieXyz xyz) => FromXyz(xyz, CieXyz.D50);

    /// <summary>Converts a color from CIEXYZ relative to a white.</summary>
    /// <param name="xyz">The color.</param>
    /// <param name="whitePoint">The white the color is relative to: it has a lightness of 100 and no chroma.</param>
    /// <returns>The color.</returns>
    public static CieLuv FromXyz(CieXyz xyz, CieXyz whitePoint)
    {
        var relativeY = xyz.Y / whitePoint.Y;
        var l = relativeY > ColorValueMath.Epsilon ? (116 * Math.Cbrt(relativeY)) - 16 : ColorValueMath.Kappa * relativeY;
        var (whiteU, whiteV) = GetChromaticity(whitePoint.X, whitePoint.Y, whitePoint.Z);
        var denominator = xyz.X + (15 * xyz.Y) + (3 * xyz.Z);
        if (denominator == 0)
            return new CieLuv(l, 0, 0);

        var (u, v) = GetChromaticity(xyz.X, xyz.Y, xyz.Z);
        return new CieLuv(l, 13 * l * (u - whiteU), 13 * l * (v - whiteV));
    }

    /// <summary>Converts this color to CIEXYZ relative to <see cref="CieXyz.D50"/>.</summary>
    /// <returns>The color.</returns>
    public CieXyz ToXyz() => ToXyz(CieXyz.D50);

    /// <summary>Converts this color to CIEXYZ relative to a white.</summary>
    /// <param name="whitePoint">The white this color is relative to.</param>
    /// <returns>The color; black when the lightness is 0.</returns>
    public CieXyz ToXyz(CieXyz whitePoint)
    {
        if (L == 0)
            return default;

        var relativeY = L > ColorValueMath.Kappa * ColorValueMath.Epsilon ? Cube((L + 16) / 116) : L / ColorValueMath.Kappa;
        var y = relativeY * whitePoint.Y;
        var (whiteU, whiteV) = GetChromaticity(whitePoint.X, whitePoint.Y, whitePoint.Z);
        var u = (U / (13 * L)) + whiteU;
        var v = (V / (13 * L)) + whiteV;
        return new CieXyz(y * 9 * u / (4 * v), y, y * (12 - (3 * u) - (20 * v)) / (4 * v));

        static double Cube(double value) => value * value * value;
    }

    /// <summary>Converts an 8-bit RGB color to CieLuv. Alpha is ignored.</summary>
    /// <param name="color">The color.</param>
    /// <param name="colorSpace">The meaning of the red, green and blue values, or <see langword="null"/> for <see cref="RgbColorSpace.Srgb"/>.</param>
    /// <returns>The color.</returns>
    public static CieLuv FromRgb(Rgba32 color, RgbColorSpace? colorSpace = null) => FromRgb(colorSpace ?? RgbColorSpace.Srgb, color.R / 255.0, color.G / 255.0, color.B / 255.0);

    /// <summary>Converts a 16-bit RGB color to CieLuv. Alpha is ignored.</summary>
    /// <param name="color">The color.</param>
    /// <param name="colorSpace">The meaning of the red, green and blue values, or <see langword="null"/> for <see cref="RgbColorSpace.Srgb"/>.</param>
    /// <returns>The color.</returns>
    public static CieLuv FromRgb(Rgba64 color, RgbColorSpace? colorSpace = null) => FromRgb(colorSpace ?? RgbColorSpace.Srgb, color.R / 65535.0, color.G / 65535.0, color.B / 65535.0);

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

    // CIE 15:2004 equation 8.9: the uniform chromaticity scale (u', v')
    private static (double U, double V) GetChromaticity(double x, double y, double z)
    {
        var denominator = x + (15 * y) + (3 * z);
        return (4 * x / denominator, 9 * y / denominator);
    }

    private static CieLuv FromRgb(RgbColorSpace colorSpace, double red, double green, double blue)
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
    public bool Equals(CieLuv other) => L.Equals(other.L) && U.Equals(other.U) && V.Equals(other.V);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is CieLuv other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(L, U, V);

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"CieLuv({L}, {U}, {V})");

    /// <summary>Compares two colors for equality, component by component.</summary>
    /// <param name="left">The first color.</param>
    /// <param name="right">The second color.</param>
    /// <returns><see langword="true"/> if all the components are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(CieLuv left, CieLuv right) => left.Equals(right);

    /// <summary>Compares two colors for inequality, component by component.</summary>
    /// <param name="left">The first color.</param>
    /// <param name="right">The second color.</param>
    /// <returns><see langword="true"/> if a component differs; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(CieLuv left, CieLuv right) => !left.Equals(right);
}
