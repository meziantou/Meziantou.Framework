using System.Diagnostics;
using System.Net.Sockets;

namespace Meziantou.Framework.HumanReadable.Converters;
internal sealed class UnixDomainSocketEndPointConverter : HumanReadableConverter<UnixDomainSocketEndPoint>
{
    [SuppressMessage("Design", "MA0150:Do not call the default object.ToString explicitly")]
    [SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "UnixDomainSocketEndPoint cannot be created on the browser, so there is no value to write")]
    protected override void WriteValue(HumanReadableTextWriter writer, UnixDomainSocketEndPoint? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);
        writer.WriteValue(value.ToString());
    }
}