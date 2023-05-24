﻿using System.Diagnostics;

namespace Meziantou.Framework.HumanReadable.Converters;
internal sealed class EnumConverter : HumanReadableConverter
{
    public override bool CanConvert(Type type) => type.IsEnum;

    public override void WriteValue(HumanReadableTextWriter writer, object? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value != null);

        writer.WriteValue(value.ToString() ?? "");
    }
}
