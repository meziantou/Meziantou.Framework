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

internal sealed class TomlDateOnlyConverter : TomlConverter<DateOnly>
{
    public static TomlDateOnlyConverter Instance { get; } = new();

    public override DateOnly Read(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.DateTime)
        {
            throw reader.CreateException($"Expected {TomlTokenType.DateTime} token but was {reader.TokenType}.");
        }

        var value = reader.GetTomlDateTime();
        if (value.Kind != TomlDateTimeKind.LocalDate)
        {
            throw reader.CreateException($"Expected TOML local-date but was {value.Kind}.");
        }

        var dateOnly = DateOnly.FromDateTime(value.DateTime.DateTime);
        reader.Read();
        return dateOnly;
    }

    public override void Write(TomlWriter writer, DateOnly value)
    {
        writer.WriteDateTimeValue(new TomlDateTime(value.Year, value.Month, value.Day));
    }
}
