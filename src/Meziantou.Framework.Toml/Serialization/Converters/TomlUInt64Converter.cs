using System;
using System.Collections.Generic;
using System.Globalization;
using Tomlyn.Model;
using Tomlyn.Syntax;
using Tomlyn.Text;

namespace Tomlyn.Serialization.Converters;

internal sealed class TomlUInt64Converter : TomlConverter<ulong>
{
    public static TomlUInt64Converter Instance { get; } = new();

    public override ulong Read(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.Integer)
        {
            throw reader.CreateException($"Expected {TomlTokenType.Integer} token but was {reader.TokenType}.");
        }

        var raw = reader.GetInt64();
        if (raw < 0)
        {
            throw reader.CreateException($"TOML integer value {raw} is out of range.");
        }

        reader.Read();
        return unchecked((ulong)raw);
    }

    public override void Write(TomlWriter writer, ulong value)
    {
        if (value > long.MaxValue)
        {
            throw new TomlException($"TOML integers are limited to signed 64-bit. Value {value} cannot be written.");
        }

        writer.WriteIntegerValue(unchecked((long)value));
    }
}
