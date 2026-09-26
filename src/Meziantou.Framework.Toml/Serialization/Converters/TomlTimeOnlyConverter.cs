using System;
using System.Collections.Generic;
using System.Globalization;
using Tomlyn.Model;
using Tomlyn.Syntax;
using Tomlyn.Text;

namespace Tomlyn.Serialization.Converters;

internal sealed class TomlTimeOnlyConverter : TomlConverter<TimeOnly>
{
    public static TomlTimeOnlyConverter Instance { get; } = new();

    public override TimeOnly Read(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.DateTime)
        {
            throw reader.CreateException($"Expected {TomlTokenType.DateTime} token but was {reader.TokenType}.");
        }

        var value = reader.GetTomlDateTime();
        if (value.Kind != TomlDateTimeKind.LocalTime)
        {
            throw reader.CreateException($"Expected TOML local-time but was {value.Kind}.");
        }

        var timeOnly = TimeOnly.FromDateTime(value.DateTime.DateTime);
        reader.Read();
        return timeOnly;
    }

    public override void Write(TomlWriter writer, TimeOnly value)
    {
        var dt = new DateTime(1, 1, 1, value.Hour, value.Minute, value.Second, DateTimeKind.Unspecified).AddTicks(value.Ticks % TimeSpan.TicksPerSecond);
        var precision = 0;
        if (value.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            precision = 7;
        }

        writer.WriteDateTimeValue(new TomlDateTime(new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Unspecified), TimeSpan.Zero), precision, TomlDateTimeKind.LocalTime));
    }
}
