#if NET11_0_OR_GREATER
using System.Numerics;

namespace Meziantou.Framework.Toml.Serialization.Converters;

internal sealed class TomlBFloat16Converter : TomlConverter<BFloat16>
{
    public static TomlBFloat16Converter Instance { get; } = new();

    public override BFloat16 Read(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.Float && reader.TokenType != TomlTokenType.Integer)
        {
            throw reader.CreateException($"Expected {TomlTokenType.Float} token but was {reader.TokenType}.");
        }

        var raw = reader.GetDouble();

        // Values slightly above BFloat16.MaxValue round to it; only values that overflow are out of range.
        var value = (BFloat16)raw;
        if (BFloat16.IsInfinity(value) && !double.IsInfinity(raw))
        {
            throw reader.CreateException($"TOML numeric value {raw} is out of range.");
        }

        reader.Read();
        return value;
    }

    // Every BFloat16 value is exactly representable as a float, whose round-trip text reads back the same value
    public override void Write(TomlWriter writer, BFloat16 value) => writer.WriteFloatValue((float)value);
}
#endif
