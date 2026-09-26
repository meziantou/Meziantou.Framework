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

internal sealed class TomlTomlDateTimeConverter : TomlConverter<TomlDateTime>
{
    public static TomlTomlDateTimeConverter Instance { get; } = new();

    public override TomlDateTime Read(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.DateTime)
        {
            throw reader.CreateException($"Expected {TomlTokenType.DateTime} token but was {reader.TokenType}.");
        }

        var value = reader.GetTomlDateTime();
        reader.Read();
        return value;
    }

    public override void Write(TomlWriter writer, TomlDateTime value)
    {
        writer.WriteDateTimeValue(value);
    }
}
