using System;
using System.Collections.Generic;
using System.Globalization;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Syntax;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Serialization.Converters;

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
