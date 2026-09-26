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

internal sealed class TomlDateTimeConverter : TomlConverter<DateTime>
{
    public static TomlDateTimeConverter Instance { get; } = new();

    public override DateTime Read(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.DateTime)
        {
            throw reader.CreateException($"Expected {TomlTokenType.DateTime} token but was {reader.TokenType}.");
        }

        var value = reader.GetTomlDateTime();
        var result = value.Kind switch
        {
            TomlDateTimeKind.OffsetDateTimeByNumber => value.DateTime.ToUniversalTime().UtcDateTime,
            TomlDateTimeKind.OffsetDateTimeByZ => value.DateTime.ToUniversalTime().UtcDateTime,
            TomlDateTimeKind.LocalDateTime => value.DateTime.DateTime,
            TomlDateTimeKind.LocalDate => value.DateTime.DateTime,
            TomlDateTimeKind.LocalTime => throw reader.CreateException("TOML local-time cannot be converted to DateTime without a date component."),
            _ => throw reader.CreateException($"Unsupported TOML datetime kind {value.Kind}."),
        };

        reader.Read();
        return result;
    }

    public override void Write(TomlWriter writer, DateTime value)
    {
        TomlDateTime toml;
        switch (value.Kind)
        {
            case DateTimeKind.Utc:
                toml = new TomlDateTime(new DateTimeOffset(value, TimeSpan.Zero), 0, TomlDateTimeKind.OffsetDateTimeByZ);
                break;
            case DateTimeKind.Local:
                toml = new TomlDateTime(new DateTimeOffset(value), 0, TomlDateTimeKind.OffsetDateTimeByNumber);
                break;
            default:
                toml = new TomlDateTime(value);
                break;
        }

        writer.WriteDateTimeValue(toml);
    }
}
