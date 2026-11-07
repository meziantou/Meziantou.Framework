using System;
using System.Collections.Generic;
using System.Globalization;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Syntax;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Serialization.Converters;

internal sealed class TomlInt64Converter : TomlConverter<long>
{
    public static TomlInt64Converter Instance { get; } = new();

    public override long Read(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.Integer)
        {
            throw reader.CreateException($"Expected {TomlTokenType.Integer} token but was {reader.TokenType}.");
        }

        var value = reader.GetInt64();
        reader.Read();
        return value;
    }

    public override void Write(TomlWriter writer, long value)
    {
        writer.WriteIntegerValue(value);
    }
}
