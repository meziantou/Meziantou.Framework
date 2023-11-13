using System.Diagnostics;
using System.Net;

namespace Meziantou.Framework.HumanReadable.Converters;
internal sealed class IPAddressConverter : HumanReadableConverter<IPAddress>
{
    protected override void WriteValue(HumanReadableTextWriter writer, IPAddress? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);
        writer.WriteValue(value.ToString());
    }
}
