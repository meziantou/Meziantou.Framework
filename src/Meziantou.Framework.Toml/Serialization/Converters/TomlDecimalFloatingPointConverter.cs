#if NET11_0_OR_GREATER
using System.Globalization;
using System.Numerics;

namespace Meziantou.Framework.Toml.Serialization.Converters;

// Decimal32, Decimal64 and Decimal128 are read from the literal and written as their exact text, so every digit (and the quantum) round-trips
internal sealed class TomlDecimalFloatingPointConverter<T> : TomlConverter<T>
    where T : struct, IDecimalFloatingPointIeee754<T>
{
    public override T Read(TomlReader reader)
    {
        if (reader.TokenType == TomlTokenType.Integer)
        {
            var integer = reader.GetInt64();
            reader.Read();
            return T.CreateChecked(integer);
        }

        if (reader.TokenType != TomlTokenType.Float)
        {
            throw reader.CreateException($"Expected {TomlTokenType.Float} token but was {reader.TokenType}.");
        }

        var text = reader.GetRawText();
        var value = ParseFloat(reader, text);
        reader.Read();
        return value;
    }

    private static T ParseFloat(TomlReader reader, string text)
    {
        switch (text)
        {
            case "inf" or "+inf":
                return T.PositiveInfinity;
            case "-inf":
                return T.NegativeInfinity;
            case "nan" or "+nan" or "-nan":
                return T.NaN;
        }

        if (!T.TryParse(text.Replace("_", "", StringComparison.Ordinal), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            throw reader.CreateException($"TOML float literal `{text}` cannot be converted to {typeof(T).Name}.");
        }

        // The literal fits in a double, but its value rounded to the precision of T may not (such as 1.7976931348623157e308 in a
        // Decimal64), and then it could not be written back
        if (T.IsInfinity(value) || OverflowsDouble(value.ToString(format: null, CultureInfo.InvariantCulture)))
        {
            throw reader.CreateException($"TOML numeric value {text} is out of range.");
        }

        return value;
    }

    private static bool OverflowsDouble(string text) => double.IsInfinity(double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture));

    public override void Write(TomlWriter writer, T value)
    {
        if (T.IsNaN(value))
        {
            writer.WriteFloatValue(double.NaN);
        }
        else if (T.IsPositiveInfinity(value))
        {
            writer.WriteFloatValue(double.PositiveInfinity);
        }
        else if (T.IsNegativeInfinity(value))
        {
            writer.WriteFloatValue(double.NegativeInfinity);
        }
        else
        {
            // TOML parsers read floats as 64-bit floating-point numbers, so a literal that overflows a double cannot be read back.
            // The digits beyond the precision of a double, and values too small for a double, are kept.
            var text = value.ToString(format: null, CultureInfo.InvariantCulture);
            if (OverflowsDouble(text))
            {
                throw new TomlException($"TOML floats are limited to the range of a 64-bit floating-point number. Value {text} cannot be written.");
            }

            writer.WriteFloatLiteral(text);
        }
    }
}

internal static class TomlDecimalFloatingPointConverter
{
    public static TomlDecimalFloatingPointConverter<Decimal32> Decimal32 { get; } = new();

    public static TomlDecimalFloatingPointConverter<Decimal64> Decimal64 { get; } = new();

    public static TomlDecimalFloatingPointConverter<Decimal128> Decimal128 { get; } = new();
}
#endif
