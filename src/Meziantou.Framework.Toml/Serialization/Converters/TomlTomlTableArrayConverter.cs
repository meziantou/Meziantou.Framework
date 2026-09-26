using System;
using System.Collections.Generic;
using System.Globalization;
using Tomlyn.Model;
using Tomlyn.Syntax;
using Tomlyn.Text;

namespace Tomlyn.Serialization.Converters;

internal sealed class TomlTomlTableArrayConverter : TomlConverter<TomlTableArray>
{
    public static TomlTomlTableArrayConverter Instance { get; } = new();

    public override TomlTableArray? Read(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.StartArray)
        {
            throw reader.CreateException($"Expected {TomlTokenType.StartArray} token but was {reader.TokenType}.");
        }

        var array = new TomlTableArray();
        reader.Read();
        while (reader.TokenType != TomlTokenType.EndArray)
        {
            if (reader.TokenType != TomlTokenType.StartTable)
            {
                throw reader.CreateException($"Expected {TomlTokenType.StartTable} token but was {reader.TokenType}.");
            }

            array.Add(TomlUntypedObjectConverter.ReadTable(reader));
        }

        reader.Read();
        return array;
    }

    public override void Write(TomlWriter writer, TomlTableArray value)
    {
        TomlUntypedObjectConverter.Instance.Write(writer, value);
    }
}
