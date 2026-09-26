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

internal sealed class TomlTimeSpanConverter : TomlConverter<TimeSpan>
{
    public static TomlTimeSpanConverter Instance { get; } = new();

    public override TimeSpan Read(TomlReader reader)
    {
        if (reader.TokenType == TomlTokenType.DateTime)
        {
            var value = reader.GetTomlDateTime();
            if (value.Kind != TomlDateTimeKind.LocalTime)
            {
                throw reader.CreateException($"Expected TOML local-time but was {value.Kind}.");
            }

            var time = value.DateTime.TimeOfDay;
            reader.Read();
            return time;
        }

        throw reader.CreateException($"Expected {TomlTokenType.DateTime} token but was {reader.TokenType}.");
    }

    public override void Write(TomlWriter writer, TimeSpan value)
    {
        if (value < TimeSpan.Zero || value >= TimeSpan.FromDays(1))
        {
            throw new TomlException("TimeSpan values must be within a single day to be representable as TOML local-time.");
        }

        var dt = new DateTime(1, 1, 1, value.Hours, value.Minutes, value.Seconds, DateTimeKind.Unspecified).AddTicks(value.Ticks % TimeSpan.TicksPerSecond);
        var precision = 0;
        var fractionTicks = value.Ticks % TimeSpan.TicksPerSecond;
        if (fractionTicks != 0)
        {
            // Derive precision up to 7 digits from ticks (100ns).
            var fractional = (int)(fractionTicks * 10_000_000 / TimeSpan.TicksPerSecond);
            precision = 7;
            while (precision > 0 && fractional % 10 == 0)
            {
                fractional /= 10;
                precision--;
            }
        }

        var tomlTime = new TomlDateTime(new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Unspecified), TimeSpan.Zero), precision, TomlDateTimeKind.LocalTime);
        writer.WriteDateTimeValue(tomlTime);
    }
}
