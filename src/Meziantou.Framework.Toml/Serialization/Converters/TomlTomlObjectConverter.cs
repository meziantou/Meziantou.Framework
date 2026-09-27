using System;
using System.Collections.Generic;
using System.Globalization;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Syntax;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Serialization.Converters;

internal sealed class TomlTomlObjectConverter : TomlConverter<TomlObject>
{
    public static TomlTomlObjectConverter Instance { get; } = new();

    public override TomlObject? Read(TomlReader reader)
    {
        return reader.TokenType switch
        {
            TomlTokenType.StartTable => TomlUntypedObjectConverter.ReadTable(reader),
            TomlTokenType.StartArray => (TomlObject)TomlUntypedObjectConverter.ReadArrayValue(reader),
            _ => throw reader.CreateException($"Expected a TOML table or array when reading {nameof(TomlObject)} but was {reader.TokenType}."),
        };
    }

    public override void Write(TomlWriter writer, TomlObject value)
    {
        TomlUntypedObjectConverter.Instance.Write(writer, value);
    }
}
