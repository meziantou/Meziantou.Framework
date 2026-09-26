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

internal sealed class TomlInt64Converter : TomlConverter<long>
{
    public static TomlInt64Converter Instance { get; } = new();

    public override long Read(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.Integer)
        {
            throw reader.CreateException($"Expected {TomlTokenType.Integer} token but was {reader.TokenType}.");
        }

        var value = reader.GetInt64();
        reader.Read();
        return value;
    }

    public override void Write(TomlWriter writer, long value)
    {
        writer.WriteIntegerValue(value);
    }
}
