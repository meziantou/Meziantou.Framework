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

internal sealed class TomlSingleConverter : TomlConverter<float>
{
    public static TomlSingleConverter Instance { get; } = new();

    public override float Read(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.Float && reader.TokenType != TomlTokenType.Integer)
        {
            throw reader.CreateException($"Expected {TomlTokenType.Float} token but was {reader.TokenType}.");
        }

        var raw = reader.GetDouble();
        if (!double.IsNaN(raw) && !double.IsInfinity(raw) && Math.Abs(raw) > float.MaxValue)
        {
            throw reader.CreateException($"TOML numeric value {raw} is out of range.");
        }

        reader.Read();
        return (float)raw;
    }

    public override void Write(TomlWriter writer, float value) => writer.WriteFloatValue(value);
}
