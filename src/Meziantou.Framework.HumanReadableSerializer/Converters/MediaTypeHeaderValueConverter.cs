using System.Diagnostics;
using System.Net.Http.Headers;

namespace Meziantou.Framework.HumanReadable.Converters;
internal sealed class MediaTypeHeaderValueConverter : HumanReadableConverter<MediaTypeHeaderValue>
{
    protected override void WriteValue(HumanReadableTextWriter writer, MediaTypeHeaderValue? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value != null);

        writer.WriteValue(value.ToString());
    }
}
