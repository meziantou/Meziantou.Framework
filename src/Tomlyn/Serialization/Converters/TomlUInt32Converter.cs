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

internal sealed class TomlUInt32Converter : TomlConverter<uint>
{
    public static TomlUInt32Converter Instance { get; } = new();

    public override uint Read(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.Integer)
        {
            throw reader.CreateException($"Expected {TomlTokenType.Integer} token but was {reader.TokenType}.");
        }

        var raw = reader.GetInt64();
        if (raw < 0 || raw > uint.MaxValue)
        {
            throw reader.CreateException($"TOML integer value {raw} is out of range.");
        }

        reader.Read();
        return (uint)raw;
    }

    public override void Write(TomlWriter writer, uint value) => writer.WriteIntegerValue(unchecked((long)value));
}
