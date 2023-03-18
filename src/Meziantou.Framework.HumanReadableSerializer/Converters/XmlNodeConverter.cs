using System.Xml;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class XmlNodeConverter : HumanReadableConverter
{
    public override bool CanConvert(Type type) => typeof(XmlNode).IsAssignableFrom(type);

    public override void WriteValue(HumanReadableTextWriter writer, object? value, HumanReadableSerializerOptions options)
    {
        var xml = (XmlNode)value!;
        writer.WriteValue(xml.OuterXml);
    }
}
