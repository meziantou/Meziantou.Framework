using System.Xml.Linq;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class XObjectConverter : HumanReadableConverter
{
    public override bool CanConvert(Type type) => typeof(XObject).IsAssignableFrom(type);

    public override void WriteValue(HumanReadableTextWriter writer, object? value, HumanReadableSerializerOptions options)
    {
        var xml = (XObject)value!;
        writer.WriteValue(xml.ToString());
    }
}
