using System;
using System.Collections.Generic;
using System.Globalization;
using Tomlyn.Model;
using Tomlyn.Syntax;
using Tomlyn.Text;

namespace Tomlyn.Serialization.Converters;

internal sealed class TomlDecimalConverter : TomlConverter<decimal>
{
    public static TomlDecimalConverter Instance { get; } = new();

    public override decimal Read(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.Float && reader.TokenType != TomlTokenType.Integer)
        {
            throw reader.CreateException($"Expected {TomlTokenType.Float} token but was {reader.TokenType}.");
        }

        if (reader.TokenType == TomlTokenType.Integer)
        {
            var integer = reader.GetInt64();
            reader.Read();
            return integer;
        }

        var raw = reader.GetDouble();
        if (double.IsNaN(raw) || double.IsInfinity(raw))
        {
            throw reader.CreateException($"TOML float literal `{reader.GetRawText()}` cannot be converted to decimal.");
        }

        try
        {
            var value = (decimal)raw;
            reader.Read();
            return value;
        }
        catch (OverflowException)
        {
            throw reader.CreateException($"TOML numeric value {raw} is out of range.");
        }
    }

    public override void Write(TomlWriter writer, decimal value)
    {
        writer.WriteFloatValue((double)value);
    }
}
