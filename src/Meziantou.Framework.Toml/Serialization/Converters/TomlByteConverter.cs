using System;
using System.Collections.Generic;
using System.Globalization;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Syntax;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Serialization.Converters;

internal sealed class TomlByteConverter : TomlConverter<byte>
{
    public static TomlByteConverter Instance { get; } = new();

    public override byte Read(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.Integer)
        {
            throw reader.CreateException($"Expected {TomlTokenType.Integer} token but was {reader.TokenType}.");
        }

        var raw = reader.GetInt64();
        if (raw < byte.MinValue || raw > byte.MaxValue)
        {
            throw reader.CreateException($"TOML integer value {raw} is out of range.");
        }

        reader.Read();
        return (byte)raw;
    }

    public override void Write(TomlWriter writer, byte value) => writer.WriteIntegerValue(value);
}
