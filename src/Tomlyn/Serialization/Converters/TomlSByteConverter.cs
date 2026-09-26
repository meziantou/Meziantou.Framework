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

internal sealed class TomlSByteConverter : TomlConverter<sbyte>
{
    public static TomlSByteConverter Instance { get; } = new();

    public override sbyte Read(TomlReader reader)
    {
        var value = ReadCheckedIntegral(reader, sbyte.MinValue, sbyte.MaxValue);
        return (sbyte)value;
    }

    public override void Write(TomlWriter writer, sbyte value) => writer.WriteIntegerValue(value);

    private static long ReadCheckedIntegral(TomlReader reader, long minValue, long maxValue)
    {
        if (reader.TokenType != TomlTokenType.Integer)
        {
            throw reader.CreateException($"Expected {TomlTokenType.Integer} token but was {reader.TokenType}.");
        }

        var raw = reader.GetInt64();
        if (raw < minValue || raw > maxValue)
        {
            throw reader.CreateException($"TOML integer value {raw} is out of range.");
        }

        reader.Read();
        return raw;
    }
}
