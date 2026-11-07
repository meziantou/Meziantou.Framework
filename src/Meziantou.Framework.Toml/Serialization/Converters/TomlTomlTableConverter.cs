using System;
using System.Collections.Generic;
using System.Globalization;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Syntax;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Serialization.Converters;

internal sealed class TomlTomlTableConverter : TomlConverter<TomlTable>
{
    public static TomlTomlTableConverter Instance { get; } = new();

    public override TomlTable? Read(TomlReader reader)
    {
        return TomlUntypedObjectConverter.ReadTable(reader);
    }

    public override void Write(TomlWriter writer, TomlTable value)
    {
        TomlUntypedObjectConverter.Instance.Write(writer, value);
    }
}
