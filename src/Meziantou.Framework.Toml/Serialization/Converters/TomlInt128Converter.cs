using System;
using System.Collections.Generic;
using System.Globalization;
using Tomlyn.Model;
using Tomlyn.Syntax;
using Tomlyn.Text;

namespace Tomlyn.Serialization.Converters;

internal sealed class TomlInt128Converter : TomlConverter<Int128>
{
    public static TomlInt128Converter Instance { get; } = new();

    public override Int128 Read(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.Integer)
        {
            throw reader.CreateException($"Expected {TomlTokenType.Integer} token but was {reader.TokenType}.");
        }

        var raw = reader.GetInt64();
        reader.Read();
        return raw;
    }

    public override void Write(TomlWriter writer, Int128 value)
    {
        if (value < long.MinValue || value > long.MaxValue)
        {
            throw new TomlException($"TOML integers are limited to signed 64-bit. Value {value} cannot be written.");
        }

        writer.WriteIntegerValue((long)value);
    }
}
