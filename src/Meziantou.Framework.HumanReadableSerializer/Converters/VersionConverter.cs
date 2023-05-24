using System.Diagnostics;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class VersionConverter : HumanReadableConverter<Version>
{
    protected override void WriteValue(HumanReadableTextWriter writer, Version? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value != null);

        writer.WriteValue(value.ToString());
    }
}

