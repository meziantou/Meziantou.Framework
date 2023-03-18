﻿using System.Globalization;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class CultureInfoConverter : HumanReadableConverter<CultureInfo>
{
    protected override void WriteValue(HumanReadableTextWriter writer, CultureInfo value, HumanReadableSerializerOptions options)
    {
        if (value == CultureInfo.InvariantCulture)
        {
            writer.WriteValue(value.EnglishName);
        }
        else
        {
            writer.WriteValue(value.Name);
        }
    }
}
