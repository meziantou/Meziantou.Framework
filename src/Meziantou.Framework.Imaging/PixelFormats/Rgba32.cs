using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>A pixel with 8-bit red, green, blue and straight (non-premultiplied) alpha components, stored in RGBA byte order.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Pixel structs expose their exact storage layout as fields.")]
public struct Rgba32 : IEquatable<Rgba32>
{
    /// <summary>The red component.</summary>
    public byte R;

    /// <summary>The green component.</summary>
    public byte G;

    /// <summary>The blue component.</summary>
    public byte B;

    /// <summary>The straight alpha component (0 is fully transparent, 255 is fully opaque).</summary>
    public byte A;

    /// <summary>Initializes a new instance of the <see cref="Rgba32"/> struct.</summary>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    /// <param name="a">The straight alpha component. Defaults to fully opaque.</param>
    public Rgba32(byte r, byte g, byte b, byte a = byte.MaxValue)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    /// <summary>Parses a color written in hexadecimal, as in CSS: <c>#RGB</c>, <c>#RGBA</c>, <c>#RRGGBB</c> or <c>#RRGGBBAA</c>.</summary>
    /// <remarks>
    /// The <c>#</c> is optional and the digits are not case-sensitive. A short form doubles each digit (<c>#F80</c> is
    /// <c>#FF8800</c>); a form without alpha is opaque. Nothing else is accepted: no white space, no color name.
    /// </remarks>
    /// <param name="value">The text.</param>
    /// <returns>The color.</returns>
    /// <exception cref="FormatException"><paramref name="value"/> is not a hexadecimal color.</exception>
    public static Rgba32 ParseHex(ReadOnlySpan<char> value)
    {
        if (!TryParseHex(value, out var result))
            throw new FormatException("The value is not a hexadecimal color (#RGB, #RGBA, #RRGGBB or #RRGGBBAA).");

        return result;
    }

    /// <summary>Tries to parse a color written in hexadecimal, as <see cref="ParseHex(ReadOnlySpan{char})"/> does.</summary>
    /// <param name="value">The text.</param>
    /// <param name="result">The color, or the default value when the text is not a hexadecimal color.</param>
    /// <returns><see langword="true"/> if the text is a hexadecimal color.</returns>
    public static bool TryParseHex(ReadOnlySpan<char> value, out Rgba32 result)
    {
        result = default;
        if (!value.IsEmpty && value[0] == '#')
        {
            value = value[1..];
        }

        if (value.Length is not (3 or 4 or 6 or 8))
            return false;

        Span<byte> digits = stackalloc byte[8];
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (!char.IsAsciiHexDigit(c))
                return false;

            digits[i] = (byte)(c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10);
        }

        if (value.Length <= 4)
        {
            result = new Rgba32(
                (byte)(digits[0] * 17),
                (byte)(digits[1] * 17),
                (byte)(digits[2] * 17),
                value.Length == 4 ? (byte)(digits[3] * 17) : byte.MaxValue);
        }
        else
        {
            result = new Rgba32(
                (byte)((digits[0] << 4) | digits[1]),
                (byte)((digits[2] << 4) | digits[3]),
                (byte)((digits[4] << 4) | digits[5]),
                value.Length == 8 ? (byte)((digits[6] << 4) | digits[7]) : byte.MaxValue);
        }

        return true;
    }

    /// <summary>
    /// Parses a color written in CSS Color 4: hexadecimal (<c>#RRGGBB</c>, ...), or one of the functions <c>rgb()</c>,
    /// <c>rgba()</c>, <c>hsl()</c>, <c>hsla()</c>, <c>hwb()</c>, <c>lab()</c>, <c>lch()</c>, <c>oklab()</c> and
    /// <c>oklch()</c>, converted to sRGB.
    /// </summary>
    /// <remarks>
    /// The hexadecimal form needs its <c>#</c> here. The functions accept the modern syntax (components separated by white
    /// space, an optional <c>/ alpha</c>, <c>none</c>) and, for <c>rgb()</c> and <c>hsl()</c>, the legacy syntax with
    /// commas; see <see cref="CieLab.Parse(ReadOnlySpan{char}, out double)"/> and the other color value types for the
    /// components. A color outside sRGB is clipped component by component (not gamut-mapped as a browser does), and each
    /// component is rounded to the nearest 8-bit value. Named colors, <c>calc()</c>, relative colors and the
    /// <c>color()</c> function are not supported.
    /// </remarks>
    /// <param name="value">The text.</param>
    /// <returns>The color.</returns>
    /// <exception cref="FormatException"><paramref name="value"/> is not a supported CSS color.</exception>
    public static Rgba32 ParseCss(ReadOnlySpan<char> value)
    {
        if (!TryParseCss(value, out var result))
            throw new FormatException("The value is not a supported CSS color (hexadecimal, rgb(), hsl(), hwb(), lab(), lch(), oklab() or oklch()).");

        return result;
    }

    /// <summary>Tries to parse a color written in CSS Color 4, as <see cref="ParseCss(ReadOnlySpan{char})"/> does.</summary>
    /// <param name="value">The text.</param>
    /// <param name="result">The color, or the default value when the text is not a supported CSS color.</param>
    /// <returns><see langword="true"/> if the text is a supported CSS color.</returns>
    public static bool TryParseCss(ReadOnlySpan<char> value, out Rgba32 result)
    {
        var trimmed = value.Trim(" \t\n\r\f");
        if (!trimmed.IsEmpty && trimmed[0] == '#')
            return TryParseHex(trimmed, out result);

        if (!CssColorSyntax.TryParse(trimmed, out var function, out var first, out var second, out var third, out var alpha))
        {
            result = default;
            return false;
        }

        result = function switch
        {
            CssColorSyntax.ColorFunction.Rgb => new Rgba32(ColorValueMath.ToByte(first), ColorValueMath.ToByte(second), ColorValueMath.ToByte(third)),
            CssColorSyntax.ColorFunction.Hsl => new Hsl(first, second, third).ToRgba32(),
            CssColorSyntax.ColorFunction.Hwb => new Hwb(first, second, third).ToRgba32(),
            CssColorSyntax.ColorFunction.Lab => new CieLab(first, second, third).ToRgba32(),
            CssColorSyntax.ColorFunction.Lch => new CieLch(first, second, third).ToRgba32(),
            CssColorSyntax.ColorFunction.Oklab => new Oklab(first, second, third).ToRgba32(),
            _ => new Oklch(first, second, third).ToRgba32(),
        };
        result.A = ColorValueMath.ToByte(alpha);
        return true;
    }

    /// <summary>Formats the color in hexadecimal, as in CSS: <c>#RRGGBB</c> when it is opaque, <c>#RRGGBBAA</c> otherwise.</summary>
    /// <returns>The text, with uppercase digits; <see cref="ParseHex(ReadOnlySpan{char})"/> reads it back.</returns>
    public readonly string ToHexString()
        => A == byte.MaxValue
            ? string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}")
            : string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}{A:X2}");

    /// <summary>Converts this pixel to <see cref="Rgba64"/> using exact full-range expansion (<c>v * 257</c>).</summary>
    /// <returns>The widened pixel.</returns>
    public readonly Rgba64 ToRgba64() => new((ushort)(R * 257), (ushort)(G * 257), (ushort)(B * 257), (ushort)(A * 257));

    /// <inheritdoc />
    public readonly bool Equals(Rgba32 other) => R == other.R && G == other.G && B == other.B && A == other.A;

    /// <inheritdoc />
    public override readonly bool Equals([NotNullWhen(true)] object? obj) => obj is Rgba32 other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(R, G, B, A);

    /// <inheritdoc />
    public override readonly string ToString() => string.Create(CultureInfo.InvariantCulture, $"Rgba32({R}, {G}, {B}, {A})");

    /// <summary>Converts a pixel to <see cref="Rgba64"/> using exact full-range expansion. The conversion is lossless.</summary>
    /// <param name="value">The pixel to convert.</param>
    public static implicit operator Rgba64(Rgba32 value) => value.ToRgba64();

    /// <summary>Compares two pixels for equality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if all components are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Rgba32 left, Rgba32 right) => left.Equals(right);

    /// <summary>Compares two pixels for inequality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if any component differs; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Rgba32 left, Rgba32 right) => !left.Equals(right);
}
