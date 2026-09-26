using System;
using System.Collections.Generic;
using System.Globalization;
using Tomlyn.Model;
using Tomlyn.Syntax;
using Tomlyn.Text;

namespace Tomlyn.Serialization.Converters;

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
