using System.Globalization;
using System.Runtime.InteropServices;

namespace Meziantou.Framework;

[StructLayout(LayoutKind.Sequential)]
public readonly partial struct Color : IEquatable<Color>
{
    public static Color Transparent { get; } = new(0, 0, 0, 0);
    public static Color Black { get; } = new(255, 0, 0, 0);
    public static Color White { get; } = new(255, 255, 255, 255);

    private Color(byte red, byte green, byte blue)
        : this(255, red, green, blue)
    {
    }

    private Color(byte alpha, byte red, byte green, byte blue)
    {
        Alpha = alpha;
        Red = red;
        Green = green;
        Blue = blue;
    }

    public byte Alpha { get; }
    public byte Red { get; }
    public byte Green { get; }
    public byte Blue { get; }

    public static Color FromRgb(byte red, byte green, byte blue)
    {
        return new Color(red, green, blue);
    }

    public static Color FromRgb(int red, int green, int blue)
    {
        return new Color(ToByte(red, nameof(red)), ToByte(green, nameof(green)), ToByte(blue, nameof(blue)));
    }

    public static Color FromArgb(byte alpha, byte red, byte green, byte blue)
    {
        return new Color(alpha, red, green, blue);
    }

    public static Color FromArgb(int alpha, int red, int green, int blue)
    {
        return new Color(ToByte(alpha, nameof(alpha)), ToByte(red, nameof(red)), ToByte(green, nameof(green)), ToByte(blue, nameof(blue)));
    }

    public static Color FromArgb(uint argb)
    {
        return new Color((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
    }

    public uint ToArgb()
    {
        return ((uint)Alpha << 24) | ((uint)Red << 16) | ((uint)Green << 8) | Blue;
    }

    public Color WithAlpha(byte alpha)
    {
        return new Color(alpha, Red, Green, Blue);
    }

    public string ToCssString()
    {
        if (Alpha is byte.MaxValue)
        {
            return $"#{Red:x2}{Green:x2}{Blue:x2}";
        }

        var alpha = (Alpha / 255d).ToString("0.###", CultureInfo.InvariantCulture);
        return $"rgba({Red},{Green},{Blue},{alpha})";
    }

    public static Color Parse(string value)
    {
        if (TryParse(value, out var color))
        {
            return color;
        }

        throw new ArgumentException("Color format is invalid. Supported formats are #RGB, #ARGB, #RRGGBB, #AARRGGBB, rgb(r,g,b), and rgba(r,g,b,a).", nameof(value));
    }

    public static bool TryParse(string? value, out Color color)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            color = default;
            return false;
        }

        var trimmed = value.AsSpan().Trim();
        return TryParseHex(trimmed, out color)
            || TryParseRgb(trimmed, out color)
            || TryParseRgba(trimmed, out color);
    }

    public bool Equals(Color other)
    {
        return Alpha == other.Alpha && Red == other.Red && Green == other.Green && Blue == other.Blue;
    }

    public override bool Equals(object? obj)
    {
        return obj is Color other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Alpha, Red, Green, Blue);
    }

    public override string ToString()
    {
        return $"#{Alpha:x2}{Red:x2}{Green:x2}{Blue:x2}";
    }

    public static bool operator ==(Color left, Color right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Color left, Color right)
    {
        return !left.Equals(right);
    }

    private static bool TryParseHex(ReadOnlySpan<char> value, out Color color)
    {
        if (value.Length > 0 && value[0] == '#')
        {
            if (value.Length is 4
                && TryParseHexNibble(value[1], out var redNibble)
                && TryParseHexNibble(value[2], out var greenNibble)
                && TryParseHexNibble(value[3], out var blueNibble))
            {
                color = FromRgb((redNibble << 4) | redNibble, (greenNibble << 4) | greenNibble, (blueNibble << 4) | blueNibble);
                return true;
            }

            if (value.Length is 5
                && TryParseHexNibble(value[1], out var alphaNibble)
                && TryParseHexNibble(value[2], out redNibble)
                && TryParseHexNibble(value[3], out greenNibble)
                && TryParseHexNibble(value[4], out blueNibble))
            {
                color = FromArgb((alphaNibble << 4) | alphaNibble, (redNibble << 4) | redNibble, (greenNibble << 4) | greenNibble, (blueNibble << 4) | blueNibble);
                return true;
            }

            if (value.Length is 7
                && byte.TryParse(value[1..3], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red)
                && byte.TryParse(value[3..5], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green)
                && byte.TryParse(value[5..7], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
            {
                color = FromRgb(red, green, blue);
                return true;
            }

            if (value.Length is 9
                && byte.TryParse(value[1..3], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var alpha)
                && byte.TryParse(value[3..5], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out red)
                && byte.TryParse(value[5..7], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out green)
                && byte.TryParse(value[7..9], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out blue))
            {
                color = FromArgb(alpha, red, green, blue);
                return true;
            }
        }

        color = default;
        return false;
    }

    private static bool TryParseRgb(ReadOnlySpan<char> value, out Color color)
    {
        if (!TryParseFunction(value, "rgb(", out var components) || components.Length is not 3)
        {
            color = default;
            return false;
        }

        if (TryParseByte(components[0], out var red)
            && TryParseByte(components[1], out var green)
            && TryParseByte(components[2], out var blue))
        {
            color = FromRgb(red, green, blue);
            return true;
        }

        color = default;
        return false;
    }

    private static bool TryParseRgba(ReadOnlySpan<char> value, out Color color)
    {
        if (!TryParseFunction(value, "rgba(", out var components) || components.Length is not 4)
        {
            color = default;
            return false;
        }

        if (TryParseByte(components[0], out var red)
            && TryParseByte(components[1], out var green)
            && TryParseByte(components[2], out var blue)
            && TryParseAlpha(components[3], out var alpha))
        {
            color = FromArgb(alpha, red, green, blue);
            return true;
        }

        color = default;
        return false;
    }

    private static bool TryParseFunction(ReadOnlySpan<char> value, string functionName, out string[] components)
    {
        if (!value.StartsWith(functionName, StringComparison.OrdinalIgnoreCase) || value.Length <= functionName.Length + 1 || value[^1] != ')')
        {
            components = [];
            return false;
        }

        var content = value.Slice(functionName.Length, value.Length - functionName.Length - 1);
        components = content.ToString().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return components.Length > 0;
    }

    private static bool TryParseHexNibble(char value, out int result)
    {
        if (value >= '0' && value <= '9')
        {
            result = value - '0';
            return true;
        }

        if (value >= 'A' && value <= 'F')
        {
            result = value - 'A' + 10;
            return true;
        }

        if (value >= 'a' && value <= 'f')
        {
            result = value - 'a' + 10;
            return true;
        }

        result = 0;
        return false;
    }

    private static bool TryParseByte(string value, out byte result)
    {
        return byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryParseAlpha(string value, out byte alpha)
    {
        if (TryParseByte(value, out alpha))
        {
            return true;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue)
            && doubleValue >= 0
            && doubleValue <= 1)
        {
            alpha = (byte)Math.Round(doubleValue * 255, MidpointRounding.AwayFromZero);
            return true;
        }

        alpha = 0;
        return false;
    }

    private static byte ToByte(int value, string parameterName)
    {
        if (value < 0 || value > 255)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The color channel value must be between 0 and 255.");
        }

        return (byte)value;
    }
}
