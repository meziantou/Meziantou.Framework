#if NET8_0_OR_GREATER
using System.Net;

namespace Meziantou.Framework.HumanReadable.Converters;
internal sealed class IPNetworkConverter : HumanReadableConverter<IPNetwork>
{
    protected override void WriteValue(HumanReadableTextWriter writer, IPNetwork value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue(value.ToString());
    }
}
#endif