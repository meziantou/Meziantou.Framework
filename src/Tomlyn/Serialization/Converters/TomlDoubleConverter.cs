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

internal sealed class TomlDoubleConverter : TomlConverter<double>
{
    public static TomlDoubleConverter Instance { get; } = new();

    public override double Read(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.Float && reader.TokenType != TomlTokenType.Integer)
        {
            throw reader.CreateException($"Expected {TomlTokenType.Float} token but was {reader.TokenType}.");
        }

        var value = reader.GetDouble();
        reader.Read();
        return value;
    }

    public override void Write(TomlWriter writer, double value)
    {
        writer.WriteFloatValue(value);
    }
}
