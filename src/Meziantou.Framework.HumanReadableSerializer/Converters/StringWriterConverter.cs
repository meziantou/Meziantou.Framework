using System.Diagnostics;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class StringWriterConverter : HumanReadableConverter<StringWriter>
{
    protected override void WriteValue(HumanReadableTextWriter writer, StringWriter? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);
        writer.WriteValue(value.ToString());
    }
}

