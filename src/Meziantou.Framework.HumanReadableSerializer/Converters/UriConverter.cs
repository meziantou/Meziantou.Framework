using System.Diagnostics;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class UriConverter : HumanReadableConverter<Uri>
{
    protected override void WriteValue(HumanReadableTextWriter writer, Uri? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value != null);
        writer.WriteValue(value.OriginalString);
    }
}

