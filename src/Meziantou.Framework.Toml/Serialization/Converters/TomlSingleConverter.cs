using System;
using System.Collections.Generic;
using System.Globalization;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Syntax;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Serialization.Converters;

internal sealed class TomlSingleConverter : TomlConverter<float>
{
    public static TomlSingleConverter Instance { get; } = new();

    public override float Read(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.Float && reader.TokenType != TomlTokenType.Integer)
        {
            throw reader.CreateException($"Expected {TomlTokenType.Float} token but was {reader.TokenType}.");
        }

        var raw = reader.GetDouble();
        if (!double.IsNaN(raw) && !double.IsInfinity(raw) && Math.Abs(raw) > float.MaxValue)
        {
            throw reader.CreateException($"TOML numeric value {raw} is out of range.");
        }

        reader.Read();
        return (float)raw;
    }

    public override void Write(TomlWriter writer, float value) => writer.WriteFloatValue(value);
}
