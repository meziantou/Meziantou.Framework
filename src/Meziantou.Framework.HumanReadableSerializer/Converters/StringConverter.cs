using System.Diagnostics;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class StringConverter : HumanReadableConverter<string>
{
    protected override void WriteValue(HumanReadableTextWriter writer, string? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);

        writer.WriteValue(value);
    }
}

