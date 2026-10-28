using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>A color given by its hue, saturation and lightness (HSL), a cylindrical form of the encoded RGB values of a device.</summary>
/// <remarks>
/// <para>
/// HSL describes RGB values, not colors: it has no color space of its own, and its conversions read and write the stored
/// RGB values as they are, whatever their color space. The equations are those of CSS Color 4 (<c>hslToRgb</c> and
/// <c>rgbToHsl</c>). The conversions from RGB give the hue in [0, 360); a gray has no hue: it is 0.
/// </para>
/// <para>The values are not validated: the conversion to RGB clips each component to [0, 1].</para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct Hsl : IEquatable<Hsl>
{
    /// <summary>Initializes a new instance of the <see cref="Hsl"/> struct.</summary>
    /// <param name="h">The hue angle, in degrees: 0 is red, 120 green, 240 blue.</param>
    /// <param name="s">The saturation, from 0 (gray) to 1.</param>
    /// <param name="l">The lightness, from 0 (black) to 1 (white); 0.5 is the pure hue.</param>
    public Hsl(double h, double s, double l)
    {
        H = h;
        S = s;
        L = l;
    }

    /// <summary>Gets the hue angle, in degrees: 0 is red, 120 green, 240 blue.</summary>
    public double H { get; }

    /// <summary>Gets the saturation, from 0 (gray) to 1.</summary>
    public double S { get; }

    /// <summary>Gets the lightness, from 0 (black) to 1 (white); 0.5 is the pure hue.</summary>
    public double L { get; }

    /// <summary>Converts an 8-bit RGB color to HSL. Alpha is ignored.</summary>
    /// <param name="color">The color.</param>
    /// <returns>The color.</returns>
    public static Hsl FromRgb(Rgba32 color) => FromRgb(color.R / 255.0, color.G / 255.0, color.B / 255.0);

    /// <summary>Converts a 16-bit RGB color to HSL. Alpha is ignored.</summary>
    /// <param name="color">The color.</param>
    /// <returns>The color.</returns>
    public static Hsl FromRgb(Rgba64 color) => FromRgb(color.R / 65535.0, color.G / 65535.0, color.B / 65535.0);

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

    /// <summary>Parses a color written with the CSS Color 4 <c>hsl()</c> function, such as <c>hsl(120 50% 25%)</c>.</summary>
    /// <param name="value">The text.</param>
    /// <param name="alpha">The alpha of the color, in [0, 1]: 1 when the text has none.</param>
    /// <returns>The color.</returns>
    /// <remarks>
    /// The modern syntax (<c>hsl(120 50% 25%)</c>, with an optional <c>/ alpha</c>) and the legacy syntax (<c>hsl(120, 50%, 25%)</c> and <c>hsla()</c>) are accepted. The hue is a number of degrees or an angle (<c>deg</c>, <c>grad</c>, <c>rad</c>, <c>turn</c>), brought to [0, 360); the saturation and the lightness are percentages, or numbers of the same value in the modern syntax, and a negative saturation is clamped to 0. Each component may be <c>none</c>, read as 0. Function names, units and keywords are ASCII
    /// case-insensitive, and white space may surround the text; <c>calc()</c> and relative colors are not supported.
    /// </remarks>
    /// <exception cref="FormatException"><paramref name="value"/> is not a <c>hsl()</c> color.</exception>
    public static Hsl Parse(ReadOnlySpan<char> value, out double alpha)
    {
        if (!TryParse(value, out var result, out alpha))
            throw new FormatException("The value is not a CSS hsl() color.");

        return result;
    }

    /// <summary>Tries to parse a color written with the CSS Color 4 <c>hsl()</c> function, as <see cref="Parse(ReadOnlySpan{char}, out double)"/> does.</summary>
    /// <param name="value">The text.</param>
    /// <param name="result">The color, or the default value when the text is not a <c>hsl()</c> color.</param>
    /// <param name="alpha">The alpha of the color, in [0, 1]: 1 when the text has none.</param>
    /// <returns><see langword="true"/> if the text is a <c>hsl()</c> color.</returns>
    public static bool TryParse(ReadOnlySpan<char> value, out Hsl result, out double alpha)
    {
        if (CssColorSyntax.TryParse(value, out var function, out var first, out var second, out var third, out alpha) && function == CssColorSyntax.ColorFunction.Hsl)
        {
            result = new Hsl(first, second, third);
            return true;
        }

        result = default;
        alpha = 0;
        return false;
    }

    /// <summary>Formats this color with the CSS Color 4 <c>hsl()</c> function, the components after the hue as percentages: <c>hsl(120 50% 25%)</c>.</summary>
    /// <param name="alpha">The alpha, in [0, 1]. It is written after a <c>/</c> when it is not 1.</param>
    /// <returns>The text; <see cref="Parse(ReadOnlySpan{char}, out double)"/> reads it back.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="alpha"/> is not in [0, 1], or a component is not a finite number.</exception>
    public string ToCssString(double alpha = 1) => CssColorSyntax.Format("hsl", H, S, L, alpha, percentages: true);

    private static Hsl FromRgb(double red, double green, double blue)
    {
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var lightness = (min + max) / 2;
        var delta = max - min;
        var saturation = delta == 0 || lightness is 0 or 1 ? 0 : (max - lightness) / Math.Min(lightness, 1 - lightness);
        return new Hsl(ColorValueMath.GetHue(red, green, blue, max, min), saturation, lightness);
    }

    internal (double Red, double Green, double Blue) ToRgb() => ColorValueMath.HslToRgb(H, S, L);

    /// <inheritdoc />
    public bool Equals(Hsl other) => H.Equals(other.H) && S.Equals(other.S) && L.Equals(other.L);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is Hsl other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(H, S, L);

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"Hsl({H}, {S}, {L})");

    /// <summary>Compares two colors for equality, component by component.</summary>
    /// <param name="left">The first color.</param>
    /// <param name="right">The second color.</param>
    /// <returns><see langword="true"/> if all the components are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Hsl left, Hsl right) => left.Equals(right);

    /// <summary>Compares two colors for inequality, component by component.</summary>
    /// <param name="left">The first color.</param>
    /// <param name="right">The second color.</param>
    /// <returns><see langword="true"/> if a component differs; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Hsl left, Hsl right) => !left.Equals(right);
}
