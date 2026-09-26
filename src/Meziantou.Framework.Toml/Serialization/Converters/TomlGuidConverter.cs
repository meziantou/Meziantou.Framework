using System;
using System.Collections.Generic;
using System.Globalization;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Syntax;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Serialization.Converters;

internal sealed class TomlGuidConverter : TomlConverter<Guid>
{
    public static TomlGuidConverter Instance { get; } = new();

    public override Guid Read(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.String)
        {
            throw reader.CreateException($"Expected {TomlTokenType.String} token but was {reader.TokenType}.");
        }

        var raw = reader.GetString();
        if (!Guid.TryParse(raw, out var value))
        {
            throw reader.CreateException($"Invalid GUID literal `{raw}`.");
        }

        reader.Read();
        return value;
    }

    public override void Write(TomlWriter writer, Guid value)
    {
        writer.WriteStringValue(value.ToString("D"));
    }
}
