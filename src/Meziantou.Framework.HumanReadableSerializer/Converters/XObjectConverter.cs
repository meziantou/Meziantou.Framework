using System.Diagnostics;
using System.Xml.Linq;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class XObjectConverter : HumanReadableConverter<XObject>
{
    protected override void WriteValue(HumanReadableTextWriter writer, XObject? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);
        writer.WriteFormattedValue("application/xml", value.ToString() ?? "");
    }
}
