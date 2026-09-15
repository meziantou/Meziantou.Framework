using System.Diagnostics;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class ByteArrayConverter : HumanReadableConverter
{
    // HumanReadableConverter<byte[]> would also match sbyte[] and the arrays of byte-backed enums, as the runtime considers them assignable to byte[]
    public override bool CanConvert(Type type) => type == typeof(byte[]);

    public override void WriteValue(HumanReadableTextWriter writer, object? value, Type valueType, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);

        writer.WriteValue(Convert.ToBase64String((byte[])value));
    }
}
