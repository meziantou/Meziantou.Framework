using System.Globalization;
using System.Net;

namespace Meziantou.Framework.HumanReadable.Converters;
internal sealed class HttpStatusCodeConverter : HumanReadableConverter<HttpStatusCode>
{
    protected override void WriteValue(HumanReadableTextWriter writer, HttpStatusCode value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue($"{((int)value).ToString(CultureInfo.InvariantCulture)} ({value})");
    }
}
