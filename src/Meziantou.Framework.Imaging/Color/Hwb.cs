using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>A color given by its hue, whiteness and blackness (HWB), a cylindrical form of the encoded RGB values of a device.</summary>
/// <remarks>
/// <para>
/// HWB describes RGB values, not colors: it has no color space of its own, and its conversions read and write the stored
/// RGB values as they are, whatever their color space. The equations are those of CSS Color 4 (<c>hwbToRgb</c> and
/// <c>rgbToHwb</c>): when the whiteness and the blackness add up to 1 or more, the color is the gray of their ratio. The
/// conversions from RGB give the hue in [0, 360); a gray has no hue: it is 0.
/// </para>
/// <para>The values are not validated: the conversion to RGB clips each component to [0, 1].</para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct Hwb : IEquatable<Hwb>
{
    /// <summary>Initializes a new instance of the <see cref="Hwb"/> struct.</summary>
    /// <param name="h">The hue angle, in degrees: 0 is red, 120 green, 240 blue.</param>
    /// <param name="w">The whiteness, the amount of white mixed in, from 0 to 1.</param>
    /// <param name="b">The blackness, the amount of black mixed in, from 0 to 1.</param>
    public Hwb(double h, double w, double b)
    {
        H = h;
        W = w;
        B = b;
    }

    /// <summary>Gets the hue angle, in degrees: 0 is red, 120 green, 240 blue.</summary>
    public double H { get; }

    /// <summary>Gets the whiteness, the amount of white mixed in, from 0 to 1.</summary>
    public double W { get; }

    /// <summary>Gets the blackness, the amount of black mixed in, from 0 to 1.</summary>
    public double B { get; }

    /// <summary>Converts an 8-bit RGB color to HWB. Alpha is ignored.</summary>
    /// <param name="color">The color.</param>
    /// <returns>The color.</returns>
    public static Hwb FromRgb(Rgba32 color) => FromRgb(color.R / 255.0, color.G / 255.0, color.B / 255.0);

    /// <summary>Converts a 16-bit RGB color to HWB. Alpha is ignored.</summary>
    /// <param name="color">The color.</param>
    /// <returns>The color.</returns>
    public static Hwb FromRgb(Rgba64 color) => FromRgb(color.R / 65535.0, color.G / 65535.0, color.B / 65535.0);

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

    /// <summary>Parses a color written with the CSS Color 4 <c>hwb()</c> function, such as <c>hwb(120 20% 30%)</c>.</summary>
    /// <param name="value">The text.</param>
    /// <param name="alpha">The alpha of the color, in [0, 1]: 1 when the text has none.</param>
    /// <returns>The color.</returns>
    /// <remarks>
    /// The hue is a number of degrees or an angle (<c>deg</c>, <c>grad</c>, <c>rad</c>, <c>turn</c>), brought to [0, 360); the whiteness and the blackness are percentages, or numbers of the same value. Each component may be <c>none</c>, read as 0. Function names, units and keywords are ASCII
    /// case-insensitive, and white space may surround the text; <c>calc()</c> and relative colors are not supported.
    /// </remarks>
    /// <exception cref="FormatException"><paramref name="value"/> is not a <c>hwb()</c> color.</exception>
    public static Hwb Parse(ReadOnlySpan<char> value, out double alpha)
    {
        if (!TryParse(value, out var result, out alpha))
            throw new FormatException("The value is not a CSS hwb() color.");

        return result;
    }

    /// <summary>Tries to parse a color written with the CSS Color 4 <c>hwb()</c> function, as <see cref="Parse(ReadOnlySpan{char}, out double)"/> does.</summary>
    /// <param name="value">The text.</param>
    /// <param name="result">The color, or the default value when the text is not a <c>hwb()</c> color.</param>
    /// <param name="alpha">The alpha of the color, in [0, 1]: 1 when the text has none.</param>
    /// <returns><see langword="true"/> if the text is a <c>hwb()</c> color.</returns>
    public static bool TryParse(ReadOnlySpan<char> value, out Hwb result, out double alpha)
    {
        if (CssColorSyntax.TryParse(value, out var function, out var first, out var second, out var third, out alpha) && function == CssColorSyntax.ColorFunction.Hwb)
        {
            result = new Hwb(first, second, third);
            return true;
        }

        result = default;
        alpha = 0;
        return false;
    }

    /// <summary>Formats this color with the CSS Color 4 <c>hwb()</c> function, the components after the hue as percentages: <c>hwb(120 20% 30%)</c>.</summary>
    /// <param name="alpha">The alpha, in [0, 1]. It is written after a <c>/</c> when it is not 1.</param>
    /// <returns>The text; <see cref="Parse(ReadOnlySpan{char}, out double)"/> reads it back.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="alpha"/> is not in [0, 1], or a component is not a finite number.</exception>
    public string ToCssString(double alpha = 1) => CssColorSyntax.Format("hwb", H, W, B, alpha, percentages: true);

    private static Hwb FromRgb(double red, double green, double blue)
    {
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        return new Hwb(ColorValueMath.GetHue(red, green, blue, max, min), min, 1 - max);
    }

    internal (double Red, double Green, double Blue) ToRgb()
    {
        if (W + B >= 1)
        {
            var gray = W / (W + B);
            return (gray, gray, gray);
        }

        var (r, g, b) = ColorValueMath.HslToRgb(H, 1, 0.5);
        var scale = 1 - W - B;
        return ((r * scale) + W, (g * scale) + W, (b * scale) + W);
    }

    /// <inheritdoc />
    public bool Equals(Hwb other) => H.Equals(other.H) && W.Equals(other.W) && B.Equals(other.B);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is Hwb other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(H, W, B);

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"Hwb({H}, {W}, {B})");

    /// <summary>Compares two colors for equality, component by component.</summary>
    /// <param name="left">The first color.</param>
    /// <param name="right">The second color.</param>
    /// <returns><see langword="true"/> if all the components are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Hwb left, Hwb right) => left.Equals(right);

    /// <summary>Compares two colors for inequality, component by component.</summary>
    /// <param name="left">The first color.</param>
    /// <param name="right">The second color.</param>
    /// <returns><see langword="true"/> if a component differs; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Hwb left, Hwb right) => !left.Equals(right);
}
