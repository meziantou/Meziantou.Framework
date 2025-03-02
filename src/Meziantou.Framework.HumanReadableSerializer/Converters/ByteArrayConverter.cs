using System.Diagnostics;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class ByteArrayConverter : HumanReadableConverter<byte[]>
{
    protected override void WriteValue(HumanReadableTextWriter writer, byte[]? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);

        writer.WriteValue(Convert.ToBase64String(value));
    }
}
