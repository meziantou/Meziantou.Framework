using System;
using System.Collections.Generic;
using System.Globalization;
using Tomlyn.Model;
using Tomlyn.Syntax;
using Tomlyn.Text;

namespace Tomlyn.Serialization.Converters;

internal sealed class TomlCharConverter : TomlConverter<char>
{
    public static TomlCharConverter Instance { get; } = new();

    public override char Read(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.String)
        {
            throw reader.CreateException($"Expected {TomlTokenType.String} token but was {reader.TokenType}.");
        }

        var value = reader.GetString();
        if (value.Length != 1)
        {
            throw reader.CreateException("Expected a single character string.");
        }

        reader.Read();
        return value[0];
    }

    public override void Write(TomlWriter writer, char value)
    {
        writer.WriteStringValue(value.ToString());
    }
}
