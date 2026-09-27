using System;
using System.Collections.Generic;
using System.Globalization;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Syntax;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Serialization.Converters;

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
        writer.WriteDateTimeValue(TomlFormatHelper.ToTomlDateTime(value));
    }
}
