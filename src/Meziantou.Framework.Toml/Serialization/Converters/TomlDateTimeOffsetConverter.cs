using System;
using System.Collections.Generic;
using System.Globalization;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Syntax;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Serialization.Converters;

internal sealed class TomlDateTimeOffsetConverter : TomlConverter<DateTimeOffset>
{
    public static TomlDateTimeOffsetConverter Instance { get; } = new();

    public override DateTimeOffset Read(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.DateTime)
        {
            throw reader.CreateException($"Expected {TomlTokenType.DateTime} token but was {reader.TokenType}.");
        }

        var value = reader.GetTomlDateTime();
        if (value.Kind == TomlDateTimeKind.LocalDateTime)
        {
            throw reader.CreateException("TOML local-date-time cannot be converted to DateTimeOffset by default.");
        }

        if (value.Kind is TomlDateTimeKind.LocalDate or TomlDateTimeKind.LocalTime)
        {
            throw reader.CreateException($"TOML {value.Kind} cannot be converted to DateTimeOffset.");
        }

        reader.Read();
        return value.DateTime;
    }

    public override void Write(TomlWriter writer, DateTimeOffset value)
    {
        writer.WriteDateTimeValue(TomlFormatHelper.ToTomlDateTime(value));
    }
}
