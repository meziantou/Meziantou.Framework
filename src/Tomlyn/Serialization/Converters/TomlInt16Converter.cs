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

internal sealed class TomlInt16Converter : TomlConverter<short>
{
    public static TomlInt16Converter Instance { get; } = new();

    public override short Read(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.Integer)
        {
            throw reader.CreateException($"Expected {TomlTokenType.Integer} token but was {reader.TokenType}.");
        }

        var raw = reader.GetInt64();
        if (raw < short.MinValue || raw > short.MaxValue)
        {
            throw reader.CreateException($"TOML integer value {raw} is out of range.");
        }

        reader.Read();
        return (short)raw;
    }

    public override void Write(TomlWriter writer, short value) => writer.WriteIntegerValue(value);
}
