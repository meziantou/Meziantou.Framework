using System.Diagnostics;
using System.Xml;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class XmlNodeConverter : HumanReadableConverter<XmlNode>
{
    protected override void WriteValue(HumanReadableTextWriter writer, XmlNode? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);
        writer.WriteFormattedValue("application/xml", value.OuterXml);
    }
}
