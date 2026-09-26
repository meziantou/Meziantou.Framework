using System;
using System.Collections.Generic;
using System.Globalization;
using Tomlyn.Model;
using Tomlyn.Syntax;
using Tomlyn.Text;

namespace Tomlyn.Serialization.Converters;

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
