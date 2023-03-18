﻿namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class UriConverter : HumanReadableConverter<Uri>
{
    protected override void WriteValue(HumanReadableTextWriter writer, Uri value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue(value.ToString());
    }
}

