using System;
using System.Collections.Generic;
using System.Globalization;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Syntax;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Serialization.Converters;

internal sealed class TomlUriConverter : TomlConverter<Uri>
{
    public static TomlUriConverter Instance { get; } = new();

    public override Uri? Read(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.String)
        {
            throw reader.CreateException($"Expected {TomlTokenType.String} token but was {reader.TokenType}.");
        }

        var raw = reader.GetString();
        if (!Uri.TryCreate(raw, UriKind.RelativeOrAbsolute, out var uri))
        {
            throw reader.CreateException($"Invalid URI literal `{raw}`.");
        }

        reader.Read();
        return uri;
    }

    public override void Write(TomlWriter writer, Uri value)
    {
        writer.WriteStringValue(value.ToString());
    }
}
