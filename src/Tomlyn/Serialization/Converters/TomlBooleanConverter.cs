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

internal sealed class TomlBooleanConverter : TomlConverter<bool>
{
    public static TomlBooleanConverter Instance { get; } = new();

    public override bool Read(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.Boolean)
        {
            throw reader.CreateException($"Expected {TomlTokenType.Boolean} token but was {reader.TokenType}.");
        }

        var value = reader.GetBoolean();
        reader.Read();
        return value;
    }

    public override void Write(TomlWriter writer, bool value)
    {
        writer.WriteBooleanValue(value);
    }
}
