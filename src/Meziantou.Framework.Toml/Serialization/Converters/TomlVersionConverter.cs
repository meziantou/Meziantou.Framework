using System;
using System.Collections.Generic;
using System.Globalization;
using Tomlyn.Model;
using Tomlyn.Syntax;
using Tomlyn.Text;

namespace Tomlyn.Serialization.Converters;

internal sealed class TomlVersionConverter : TomlConverter<Version>
{
    public static TomlVersionConverter Instance { get; } = new();

    public override Version? Read(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.String)
        {
            throw reader.CreateException($"Expected {TomlTokenType.String} token but was {reader.TokenType}.");
        }

        var raw = reader.GetString();

        if (!Version.TryParse(raw, out var version))
        {
            throw reader.CreateException($"Invalid version literal `{raw}`.");
        }

        reader.Read();
        return version;
    }

    public override void Write(TomlWriter writer, Version value)
    {
        writer.WriteStringValue(value.ToString());
    }
}
