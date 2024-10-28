using System.Xml.Linq;

namespace Meziantou.Framework.HumanReadable.ValueFormatters;

public abstract class ValueFormatter
{
    public abstract void Format(HumanReadableTextWriter writer, string? value, HumanReadableSerializerOptions options);
}
