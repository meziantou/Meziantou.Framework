using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>A color in the CIE 1931 XYZ color space: the tristimulus values X, Y (the luminance) and Z.</summary>
/// <remarks>
/// <para>
/// The values are relative: the white of a color space has a luminance <see cref="Y"/> of 1. The conversions of the
/// library give and take CIEXYZ relative to <see cref="D50"/>, the white of CIELAB in CSS and of the ICC profile
/// connection space: colors given relative to another white are first brought to D50 with <see cref="Adapt"/>.
/// </para>
/// <para>The values are not validated: negative values (imaginary colors) and values that are not numbers are kept as they are.</para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct CieXyz : IEquatable<CieXyz>
{
    /// <summary>Initializes a new instance of the <see cref="CieXyz"/> struct.</summary>
    /// <param name="x">The X tristimulus value.</param>
    /// <param name="y">The Y tristimulus value, the luminance.</param>
    /// <param name="z">The Z tristimulus value.</param>
    public CieXyz(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>Gets the X tristimulus value.</summary>
    public double X { get; }

    /// <summary>Gets the Y tristimulus value, the luminance.</summary>
    public double Y { get; }

    /// <summary>Gets the Z tristimulus value.</summary>
    public double Z { get; }

    /// <summary>Gets the CIE illuminant D50, from its chromaticity (0.3457, 0.3585), with a luminance of 1, as CSS Color 4 defines it.</summary>
    public static CieXyz D50 => new(ColorValueMath.D50X, 1, ColorValueMath.D50Z);

    /// <summary>Gets the CIE illuminant D65, the white of sRGB, from its chromaticity (0.3127, 0.3290), with a luminance of 1, as CSS Color 4 defines it.</summary>
    public static CieXyz D65 => new(ColorValueMath.D65X, 1, ColorValueMath.D65Z);

    /// <summary>Converts an 8-bit RGB color to CIEXYZ relative to <see cref="D50"/>. Alpha is ignored.</summary>
    /// <param name="color">The color.</param>
    /// <param name="colorSpace">The meaning of the red, green and blue values, or <see langword="null"/> for <see cref="RgbColorSpace.Srgb"/>.</param>
    /// <returns>The color.</returns>
    public static CieXyz FromRgb(Rgba32 color, RgbColorSpace? colorSpace = null)
    {
        var (x, y, z) = (colorSpace ?? RgbColorSpace.Srgb).ToXyzD50(color.R / 255.0, color.G / 255.0, color.B / 255.0);
        return new CieXyz(x, y, z);
    }

    /// <summary>Converts a 16-bit RGB color to CIEXYZ relative to <see cref="D50"/>. Alpha is ignored.</summary>
    /// <param name="color">The color.</param>
    /// <param name="colorSpace">The meaning of the red, green and blue values, or <see langword="null"/> for <see cref="RgbColorSpace.Srgb"/>.</param>
    /// <returns>The color.</returns>
    public static CieXyz FromRgb(Rgba64 color, RgbColorSpace? colorSpace = null)
    {
        var (x, y, z) = (colorSpace ?? RgbColorSpace.Srgb).ToXyzD50(color.R / 65535.0, color.G / 65535.0, color.B / 65535.0);
        return new CieXyz(x, y, z);
    }

    /// <summary>Converts this color, relative to <see cref="D50"/>, to an opaque 8-bit RGB color, clipped to the gamut of the space component by component.</summary>
    /// <param name="colorSpace">The meaning of the red, green and blue values, or <see langword="null"/> for <see cref="RgbColorSpace.Srgb"/>.</param>
    /// <returns>The color.</returns>
    /// <exception cref="UnsupportedImageFeatureException">The space cannot convert colors to RGB values (<see cref="RgbColorSpace.CanConvertFromXyz"/>).</exception>
    public Rgba32 ToRgba32(RgbColorSpace? colorSpace = null)
    {
        var (r, g, b) = (colorSpace ?? RgbColorSpace.Srgb).FromXyzD50(X, Y, Z);
        return new Rgba32(ColorValueMath.ToByte(r), ColorValueMath.ToByte(g), ColorValueMath.ToByte(b));
    }

    /// <summary>Converts this color, relative to <see cref="D50"/>, to an opaque 16-bit RGB color, clipped to the gamut of the space component by component.</summary>
    /// <param name="colorSpace">The meaning of the red, green and blue values, or <see langword="null"/> for <see cref="RgbColorSpace.Srgb"/>.</param>
    /// <returns>The color.</returns>
    /// <exception cref="UnsupportedImageFeatureException">The space cannot convert colors to RGB values (<see cref="RgbColorSpace.CanConvertFromXyz"/>).</exception>
    public Rgba64 ToRgba64(RgbColorSpace? colorSpace = null)
    {
        var (r, g, b) = (colorSpace ?? RgbColorSpace.Srgb).FromXyzD50(X, Y, Z);
        return new Rgba64(ColorValueMath.ToUInt16(r), ColorValueMath.ToUInt16(g), ColorValueMath.ToUInt16(b), ushort.MaxValue);
    }

    /// <summary>
    /// Adapts this color from a white to another with the linear Bradford transform: the color that looks, under the
    /// destination white, as this color looks under the source white.
    /// </summary>
    /// <param name="sourceWhite">The white this color is relative to, such as <see cref="D65"/>.</param>
    /// <param name="destinationWhite">The white of the result, such as <see cref="D50"/>.</param>
    /// <returns>The adapted color; the source white becomes the destination white.</returns>
    public CieXyz Adapt(CieXyz sourceWhite, CieXyz destinationWhite)
    {
        var matrix = ColorValueMath.GetBradfordAdaptation(sourceWhite.X, sourceWhite.Y, sourceWhite.Z, destinationWhite.X, destinationWhite.Y, destinationWhite.Z);
        var (x, y, z) = matrix.Transform(X, Y, Z);
        return new CieXyz(x, y, z);
    }

    /// <inheritdoc />
    public bool Equals(CieXyz other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is CieXyz other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"CieXyz({X}, {Y}, {Z})");

    /// <summary>Compares two colors for equality, component by component.</summary>
    /// <param name="left">The first color.</param>
    /// <param name="right">The second color.</param>
    /// <returns><see langword="true"/> if all the components are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(CieXyz left, CieXyz right) => left.Equals(right);

    /// <summary>Compares two colors for inequality, component by component.</summary>
    /// <param name="left">The first color.</param>
    /// <param name="right">The second color.</param>
    /// <returns><see langword="true"/> if a component differs; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(CieXyz left, CieXyz right) => !left.Equals(right);
}
