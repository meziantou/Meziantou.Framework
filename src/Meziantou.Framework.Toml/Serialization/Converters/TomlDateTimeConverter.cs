using System;
using System.Collections.Generic;
using System.Globalization;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Syntax;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Serialization.Converters;

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
        writer.WriteDateTimeValue(TomlFormatHelper.ToTomlDateTime(value));
    }
}
