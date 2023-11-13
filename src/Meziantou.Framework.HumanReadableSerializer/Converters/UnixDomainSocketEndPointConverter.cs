#if NETCOREAPP2_1_OR_GREATER
using System.Diagnostics;
using System.Net.Sockets;

namespace Meziantou.Framework.HumanReadable.Converters;
internal sealed class UnixDomainSocketEndPointConverter : HumanReadableConverter<UnixDomainSocketEndPoint>
{
    protected override void WriteValue(HumanReadableTextWriter writer, UnixDomainSocketEndPoint? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);
        writer.WriteValue(value.ToString());
    }
}
#endif