using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>A color given by its hue, saturation and value (HSV, also called HSB), a cylindrical form of the encoded RGB values of a device.</summary>
/// <remarks>
/// <para>
/// HSV describes RGB values, not colors: it has no color space of its own, and its conversions read and write the stored
/// RGB values as they are, whatever their color space. The value is the largest RGB value and the saturation is the
/// range of the RGB values relative to it. The conversions from RGB give the hue in [0, 360); a gray has no hue: it is 0.
/// </para>
/// <para>The values are not validated: the conversion to RGB clips each component to [0, 1].</para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct Hsv : IEquatable<Hsv>
{
    /// <summary>Initializes a new instance of the <see cref="Hsv"/> struct.</summary>
    /// <param name="h">The hue angle, in degrees: 0 is red, 120 green, 240 blue.</param>
    /// <param name="s">The saturation, from 0 (gray) to 1 (the pure hue).</param>
    /// <param name="v">The value, the largest of the red, green and blue values, from 0 (black) to 1.</param>
    public Hsv(double h, double s, double v)
    {
        H = h;
        S = s;
        V = v;
    }

    /// <summary>Gets the hue angle, in degrees: 0 is red, 120 green, 240 blue.</summary>
    public double H { get; }

    /// <summary>Gets the saturation, from 0 (gray) to 1 (the pure hue).</summary>
    public double S { get; }

    /// <summary>Gets the value, the largest of the red, green and blue values, from 0 (black) to 1.</summary>
    public double V { get; }

    /// <summary>Converts an 8-bit RGB color to HSV. Alpha is ignored.</summary>
    /// <param name="color">The color.</param>
    /// <returns>The color.</returns>
    public static Hsv FromRgb(Rgba32 color) => FromRgb(color.R / 255.0, color.G / 255.0, color.B / 255.0);

    /// <summary>Converts a 16-bit RGB color to HSV. Alpha is ignored.</summary>
    /// <param name="color">The color.</param>
    /// <returns>The color.</returns>
    public static Hsv FromRgb(Rgba64 color) => FromRgb(color.R / 65535.0, color.G / 65535.0, color.B / 65535.0);

    /// <summary>Converts this color to an opaque 8-bit RGB color, each component clipped to [0, 1].</summary>
    /// <returns>The color.</returns>
    public Rgba32 ToRgba32()
    {
        var (r, g, b) = ToRgb();
        return new Rgba32(ColorValueMath.ToByte(r), ColorValueMath.ToByte(g), ColorValueMath.ToByte(b));
    }

    /// <summary>Converts this color to an opaque 16-bit RGB color, each component clipped to [0, 1].</summary>
    /// <returns>The color.</returns>
    public Rgba64 ToRgba64()
    {
        var (r, g, b) = ToRgb();
        return new Rgba64(ColorValueMath.ToUInt16(r), ColorValueMath.ToUInt16(g), ColorValueMath.ToUInt16(b), ushort.MaxValue);
    }

    private static Hsv FromRgb(double red, double green, double blue)
    {
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var saturation = max == 0 ? 0 : (max - min) / max;
        return new Hsv(ColorValueMath.GetHue(red, green, blue, max, min), saturation, max);
    }

    // Each component is the value minus the part of the chroma its distance from the hue removes
    internal (double Red, double Green, double Blue) ToRgb()
    {
        var hue = ColorValueMath.NormalizeHue(H) / 60;
        return (Channel(5, hue, S, V), Channel(3, hue, S, V), Channel(1, hue, S, V));

        static double Channel(double n, double hue, double saturation, double value)
        {
            var k = (n + hue) % 6;
            return value - (value * saturation * Math.Max(0, Math.Min(Math.Min(k, 4 - k), 1)));
        }
    }

    /// <inheritdoc />
    public bool Equals(Hsv other) => H.Equals(other.H) && S.Equals(other.S) && V.Equals(other.V);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is Hsv other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(H, S, V);

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"Hsv({H}, {S}, {V})");

    /// <summary>Compares two colors for equality, component by component.</summary>
    /// <param name="left">The first color.</param>
    /// <param name="right">The second color.</param>
    /// <returns><see langword="true"/> if all the components are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Hsv left, Hsv right) => left.Equals(right);

    /// <summary>Compares two colors for inequality, component by component.</summary>
    /// <param name="left">The first color.</param>
    /// <param name="right">The second color.</param>
    /// <returns><see langword="true"/> if a component differs; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Hsv left, Hsv right) => !left.Equals(right);
}
