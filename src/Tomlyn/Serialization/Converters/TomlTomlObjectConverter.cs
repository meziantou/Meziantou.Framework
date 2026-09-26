// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using Tomlyn.Model;
using Tomlyn.Syntax;
using Tomlyn.Text;

namespace Tomlyn.Serialization.Converters;

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
