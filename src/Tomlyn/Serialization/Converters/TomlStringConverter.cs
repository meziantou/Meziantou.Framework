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

internal sealed class TomlStringConverter : TomlConverter<string>
{
    public static TomlStringConverter Instance { get; } = new();

    public override string? Read(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.String)
        {
            throw reader.CreateException($"Expected {TomlTokenType.String} token but was {reader.TokenType}.");
        }

        var value = reader.GetString();
        reader.Read();
        return value;
    }

    public override void Write(TomlWriter writer, string value)
    {
        writer.WriteStringValue(value);
    }
}
