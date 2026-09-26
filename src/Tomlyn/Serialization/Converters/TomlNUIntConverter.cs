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

internal sealed class TomlNUIntConverter : TomlConverter<nuint>
{
    public static TomlNUIntConverter Instance { get; } = new();

    public override nuint Read(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.Integer)
        {
            throw reader.CreateException($"Expected {TomlTokenType.Integer} token but was {reader.TokenType}.");
        }

        var raw = reader.GetInt64();
        if (raw < 0)
        {
            throw reader.CreateException($"TOML integer value {raw} is out of range.");
        }

        if (IntPtr.Size == 4 && raw > uint.MaxValue)
        {
            throw reader.CreateException($"TOML integer value {raw} is out of range.");
        }

        reader.Read();
        return unchecked((nuint)raw);
    }

    public override void Write(TomlWriter writer, nuint value)
    {
        var asUlong = unchecked((ulong)value);
        if (asUlong > long.MaxValue)
        {
            throw new TomlException($"TOML integers are limited to signed 64-bit. Value {asUlong} cannot be written.");
        }

        writer.WriteIntegerValue(unchecked((long)asUlong));
    }
}
