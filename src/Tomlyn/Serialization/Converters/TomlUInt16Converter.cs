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

internal sealed class TomlUInt16Converter : TomlConverter<ushort>
{
    public static TomlUInt16Converter Instance { get; } = new();

    public override ushort Read(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.Integer)
        {
            throw reader.CreateException($"Expected {TomlTokenType.Integer} token but was {reader.TokenType}.");
        }

        var raw = reader.GetInt64();
        if (raw < ushort.MinValue || raw > ushort.MaxValue)
        {
            throw reader.CreateException($"TOML integer value {raw} is out of range.");
        }

        reader.Read();
        return (ushort)raw;
    }

    public override void Write(TomlWriter writer, ushort value) => writer.WriteIntegerValue(value);
}
