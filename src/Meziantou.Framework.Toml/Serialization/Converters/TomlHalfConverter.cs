using System;
using System.Collections.Generic;
using System.Globalization;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Syntax;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Serialization.Converters;

internal sealed class TomlHalfConverter : TomlConverter<Half>
{
    public static TomlHalfConverter Instance { get; } = new();

    public override Half Read(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.Float && reader.TokenType != TomlTokenType.Integer)
        {
            throw reader.CreateException($"Expected {TomlTokenType.Float} token but was {reader.TokenType}.");
        }

        var raw = reader.GetDouble();

        // Values slightly above Half.MaxValue round to it; only values that overflow are out of range.
        var value = (Half)raw;
        if (Half.IsInfinity(value) && !double.IsInfinity(raw))
        {
            throw reader.CreateException($"TOML numeric value {raw} is out of range.");
        }

        reader.Read();
        return value;
    }

    public override void Write(TomlWriter writer, Half value) => writer.WriteFloatValue((float)value);
}
