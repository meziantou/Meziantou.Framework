using System;
using System.Collections.Generic;
using System.Globalization;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Syntax;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Serialization.Converters;

internal sealed class TomlTomlArrayConverter : TomlConverter<TomlArray>
{
    public static TomlTomlArrayConverter Instance { get; } = new();

    public override TomlArray? Read(TomlReader reader)
    {
        return TomlUntypedObjectConverter.ReadArray(reader);
    }

    public override void Write(TomlWriter writer, TomlArray value)
    {
        TomlUntypedObjectConverter.Instance.Write(writer, value);
    }
}
