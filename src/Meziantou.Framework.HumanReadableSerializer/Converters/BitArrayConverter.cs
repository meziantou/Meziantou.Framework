﻿using System.Collections;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class BitArrayConverter : HumanReadableConverter<BitArray>
{
    protected override void WriteValue(HumanReadableTextWriter writer, BitArray value, HumanReadableSerializerOptions options)
    {
        Span<char> str = value.Count <= 128 ? stackalloc char[128] : new char[value.Count];
        for (var i = 0; i < value.Length; i++)
        {
            str[i] = value[i] ? '1' : '0';
        }

        str = str.Slice(0, value.Count);
        writer.WriteValue(str);
    }
}
